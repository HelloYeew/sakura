// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Threading;
using ManagedBass;
using ManagedBass.Mix;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio.BassEngine;

/// <summary>
/// BASS implementation of <see cref="IAudioChannel"/>.
/// </summary>
internal class BassAudioChannel : IAudioChannel
{
    public event Action OnStart = () => { };
    public event Action OnStop = () => { };
    public event Action OnEnd = () => { };

    /// <summary>
    /// Raised once this channel's BASS handles have been freed, on the audio thread.
    /// </summary>
    /// <remarks>
    /// Used by whatever created the channel to release resources the channel was reading from —
    /// notably <see cref="BassTrack"/>'s unmanaged data block, which must outlive every channel
    /// decoding out of it. Raised after <see cref="Bass.StreamFree"/>, which is the earliest point
    /// at which BASS is known to be done with it.
    /// </remarks>
    internal event Action Disposed;

    public ReactiveBool IsRunning { get; } = new ReactiveBool();

    public int ChannelHandle { get; }

    private readonly BassAudioManager manager;
    private readonly bool isStream;
    private SyncProcedure endSyncProcedure; // Keep a reference to prevent GC
    private bool isLooping;
    private int cachedLevel;
    private long lastLevelFetchTick;

    private readonly float[] fftBuffer = new float[ChannelAmplitudes.AMPLITUDES_SIZE];
    private readonly float[] frequencyAmplitudes = new float[ChannelAmplitudes.AMPLITUDES_SIZE];
    private long lastAmplitudesFetchTick;
    private ChannelAmplitudes cachedAmplitudes = ChannelAmplitudes.Empty;

    // Per-frame temporal damping of the spectrum so visualisers receive a smooth signal rather
    // than the raw, jittery FFT. Each refresh eases the stored value toward the new reading,
    // retaining this fraction of the old value per ~60fps frame (higher = smoother but laggier).
    // Made framerate-independent by raising it to (elapsedMs / referenceFrameMs).
    private const double amplitude_retain_per_frame = 0.4;
    private const double amplitude_reference_frame_ms = 1000.0 / 60.0;

    public bool AutoDispose { get; set; } = false;

    public BassAudioMixer Mixer { get; internal set; }

    public BassAudioChannel(int channelHandle, BassAudioManager manager, bool isStream, BassAudioMixer mixer = null!)
    {
        ChannelHandle = channelHandle;
        this.manager = manager;
        this.isStream = isStream;
        Mixer = mixer;

        Bass.ChannelGetAttribute(ChannelHandle, ChannelAttribute.Frequency, out float freq);
        float originalFrequency1 = freq > 0 ? freq : 44100;

        // Set up a sync to fire the OnEnd event
        endSyncProcedure = OnChannelEnd;
        Bass.ChannelSetSync(ChannelHandle, SyncFlags.End | SyncFlags.Mixtime, 0, endSyncProcedure);

        // Set up reactive property bindings
        IsRunning.ValueChanged += e =>
        {
            var handler = e.NewValue ? OnStart : OnStop;
            manager.RaiseEvent(() => handler?.Invoke());
        };

        Volume.ValueChanged += e => manager.EnqueueAction(() =>
        {
            if (isDisposed) return;
            BassUtils.CheckError(Bass.ChannelSetAttribute(ChannelHandle, ChannelAttribute.Volume, (float)e.NewValue), "setting volume");
        });

        Volume.ValueChanged += e => manager.EnqueueAction(() => BassUtils.CheckError(Bass.ChannelSetAttribute(ChannelHandle, ChannelAttribute.Volume, (float)e.NewValue), "setting volume"));
        bool isFreqInitialized = false;
        Frequency.ValueChanged += e =>
        {
            manager.EnqueueAction(() =>
            {
                if (isDisposed) return;
                if (!isFreqInitialized)
                {
                    isFreqInitialized = true;
                    if (Math.Abs(e.NewValue - 1.0) < 0.001) return;
                }
                BassUtils.CheckError(Bass.ChannelSetAttribute(ChannelHandle, ChannelAttribute.Frequency, (float)(e.NewValue * originalFrequency1)), "setting frequency");
            });
        };

        Balance.ValueChanged += e => manager.EnqueueAction(() =>
        {
            if (isDisposed) return;
            BassUtils.CheckError(Bass.ChannelSetAttribute(ChannelHandle, ChannelAttribute.Pan, (float)e.NewValue), "setting balance");
        });

        Tempo.ValueChanged += e => manager.EnqueueAction(() =>
        {
            if (isDisposed) return;

            // BASS expects tempo as a percentage change from normal speed
            // (0 = normal, +50 = 1.5x, -50 = 0.5x). Valid range is roughly -95..+5000.
            double multiplier = Math.Clamp(e.NewValue, 0.05, 51.0);
            float percent = (float)((multiplier - 1.0) * 100.0);
            BassUtils.CheckError(Bass.ChannelSetAttribute(ChannelHandle, ChannelAttribute.Tempo, percent), "setting tempo");
        });
    }

    private void OnChannelEnd(int handle, int channel, int data, IntPtr user)
    {
        // If we are handling a custom RestartPoint, we manually seek and play here.
        if (isLooping)
        {
            long pos = Bass.ChannelSeconds2Bytes(ChannelHandle, restartPoint / 1000.0);
            Bass.ChannelSetPosition(ChannelHandle, pos);
            // Bass.ChannelPlay(ChannelHandle, false); // Resume immediately for gapless playback
        }

        // Schedule the event to run on the main audio thread (via manager update).
        manager.EnqueueAction(() =>
        {
            var ended = OnEnd;
            manager.RaiseEvent(() => ended?.Invoke());

            if (!isLooping)
            {
                IsRunning.Value = false;
                if (AutoDispose)
                    Dispose();
            }
        });
    }

    public void Play()
    {
        manager.EnqueueAction(() =>
        {
            if (isDisposed) return;
            if (Mixer != null)
            {
                BassUtils.CheckError(BassMix.ChannelRemoveFlag(ChannelHandle, BassFlags.MixerChanPause), "resuming mixer channel");
                IsRunning.Value = true;
            }
            else if (BassUtils.CheckError(Bass.ChannelPlay(ChannelHandle, false), "playing channel"))
            {
                IsRunning.Value = true;
            }
        });
    }

    public void Stop()
    {
        manager.EnqueueAction(() =>
        {
            if (isDisposed) return;
            if (Mixer != null)
            {
                BassUtils.CheckError(BassMix.ChannelAddFlag(ChannelHandle, BassFlags.MixerChanPause), "stopping mixer channel");
                Bass.ChannelSetPosition(ChannelHandle, 0);
                IsRunning.Value = false;
            }
            else if (BassUtils.CheckError(Bass.ChannelStop(ChannelHandle), "stopping channel"))
            {
                Bass.ChannelSetPosition(ChannelHandle, 0);
                IsRunning.Value = false;
            }
        });
    }

    public void Pause()
    {
        manager.EnqueueAction(() =>
        {
            if (isDisposed) return; // Guard clause
            if (Mixer != null)
            {
                BassUtils.CheckError(BassMix.ChannelAddFlag(ChannelHandle, BassFlags.MixerChanPause), "pausing mixer channel");
                IsRunning.Value = false;
            }
            else if (BassUtils.CheckError(Bass.ChannelPause(ChannelHandle), "pausing channel"))
            {
                IsRunning.Value = false;
            }
        });
    }

    public Reactive<double> Volume { get; } = new Reactive<double>(1.0);
    public Reactive<double> Frequency { get; } = new Reactive<double>(1.0);
    public Reactive<double> Balance { get; } = new Reactive<double>(0.0);
    public Reactive<double> Tempo { get; } = new Reactive<double>(1.0);
    private double restartPoint;

    /// <summary>
    /// Where a posted-but-not-yet-applied seek was sent, in microseconds, or -1 when none is outstanding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A seek is queued onto the audio thread rather than applied inline, so for up to one frame
    /// <c>Bass.ChannelGetPosition</c> still reports the position being left. Pairing the old cursor with
    /// a caller that believes it has already seeked reads as the track jumping backwards and then
    /// forwards, which <c>TrackClock</c> turns into audible desync — and this backend is the default, so
    /// it is the one where that mattered most.
    /// </para>
    /// <para>
    /// So the target is reported until the seek lands, which is the same thing the SDL backend's native
    /// engine does with its seek epoch. Found by the cross-backend conformance suite, which asked all
    /// three backends whether a read straight after a seek sees the new position; only the native SDL
    /// engine said yes.
    /// </para>
    /// <para>
    /// Microseconds in a <c>long</c> so that the field can be published and cleared atomically without a
    /// lock, since the getter is called from whichever thread wants a position.
    /// </para>
    /// </remarks>
    private long pendingSeekMicroseconds = -1;

    /// <summary>
    /// This channel's position as the listener is hearing it, in bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Bass.ChannelGetPosition</c> on a channel plugged into a BASSmix mixer is the <em>decoding</em>
    /// position: how far the mixer has pulled the source, which runs ahead of what is audible by
    /// however much of the mixer's playback buffer is filled — 100 ms, set in
    /// <see cref="BassAudioManager"/>. Reporting that as <see cref="CurrentTime"/> puts everything
    /// synced to the music a tenth of a second ahead of the music, which for a rhythm game is the
    /// difference between a chart that lines up and one that does not.
    /// </para>
    /// <para>
    /// <c>BassMix.ChannelGetPosition</c> is BASS's own answer to the same question with the mixer's
    /// buffering taken off, and it is available because <see cref="BassAudioMixer"/> adds every channel
    /// with <c>MixerChanBuffer</c>. Measured on real hardware at ~115 ms behind the decoding position,
    /// which is the 100 ms playback buffer plus the device's own 17 ms
    /// </para>
    /// </remarks>
    private long audiblePosition()
    {
        // Only while it is actually playing. BASSmix answers from a record of where the source was in
        // the mixer's output going back one buffer, so a channel that has just been stopped and rewound
        // is still described by that record as being where it was before — Stop would stop rewinding as
        // far as any caller could tell. It is also the right answer for a paused channel: the mixer's
        // buffer plays its tail out rather than dropping it, so once that tail has drained the audible
        // position has caught up with the decoding cursor, and the cursor is where playback resumes.
        if (Mixer != null && IsRunning.Value)
        {
            long audible = BassMix.ChannelGetPosition(ChannelHandle);

            // -1 is a channel BASSmix has no record of at this position: a source not (or no longer)
            // plugged into a mixer, or one asked before the mixer has pulled it at all. The decoding
            // position is the only answer available then, and it is the right one for a channel with no
            // mixer buffer in front of it.
            if (audible >= 0)
                return audible;
        }

        return Bass.ChannelGetPosition(ChannelHandle);
    }

    public double CurrentTime
    {
        get
        {
            if (isDisposed)
                return 0;

            long pending = Interlocked.Read(ref pendingSeekMicroseconds);

            if (pending >= 0)
                return pending / 1000.0;

            return Bass.ChannelBytes2Seconds(ChannelHandle, audiblePosition()) * 1000.0;
        }
        set
        {
            if (isDisposed) return;

            long target = (long)(Math.Max(0, value) * 1000.0);
            Interlocked.Exchange(ref pendingSeekMicroseconds, target);

            manager.EnqueueAction(() =>
            {
                if (isDisposed) return;

                long pos = Bass.ChannelSeconds2Bytes(ChannelHandle, value / 1000.0);
                Bass.ChannelSetPosition(ChannelHandle, pos);

                // Only if this is still the outstanding seek: a second one posted in the meantime owns
                // the reported position now, and clearing it here would briefly report the position this
                // seek landed on rather than the one the caller last asked for.
                Interlocked.CompareExchange(ref pendingSeekMicroseconds, -1, target);
            });
        }
    }

    public double Length
    {
        get
        {
            if (isDisposed)
                return 0;
            long len = Bass.ChannelGetLength(ChannelHandle);
            return Bass.ChannelBytes2Seconds(ChannelHandle, len) * 1000.0;
        }
    }

    public double RestartPoint
    {
        get => restartPoint;
        set
        {
            restartPoint = value;
            updateLoopState();
        }
    }

    public bool Looping
    {
        get => isLooping;
        set
        {
            isLooping = value;
            Bass.ChannelFlags(ChannelHandle, isLooping ? BassFlags.Loop : BassFlags.Default, BassFlags.Loop);
        }
    }

    private void updateLoopState()
    {
        // If looping and the restart point exactly 0, BASS loop is perfect
        if (isLooping && restartPoint == 0)
        {
            Bass.ChannelFlags(ChannelHandle, BassFlags.Loop, BassFlags.Loop);
        }
        else
        {
            // If RestartPoint got set, turn off native looping so our OnChannelEnd sync catches it.
            Bass.ChannelFlags(ChannelHandle, BassFlags.Default, BassFlags.Loop);
        }
    }

    private int getCurrentLevel()
    {
        long currentTick = Environment.TickCount64;

        // Cache the level for 15ms (roughly one frame at 60fps).
        // This ensures left and right properties read the exact same buffer
        // snapshot without advancing the decoded stream twice.
        if (currentTick - lastLevelFetchTick < 15)
        {
            return cachedLevel;
        }

        lastLevelFetchTick = currentTick;

        // Use Mix version of it to prevent consuming the buffer
        cachedLevel = Mixer != null ? BassMix.ChannelGetLevel(ChannelHandle) : Bass.ChannelGetLevel(ChannelHandle);

        return cachedLevel;
    }

    public float AmplitudeLeft
    {
        get
        {
            int level = getCurrentLevel();
            return level != -1 ? (level & 0xFFFF) / 32768f : 0f;
        }
    }

    public float AmplitudeRight
    {
        get
        {
            int level = getCurrentLevel();
            return level != -1 ? ((level >> 16) & 0xFFFF) / 32768f : 0f;
        }
    }

    public ChannelAmplitudes CurrentAmplitudes
    {
        get
        {
            if (isDisposed)
                return ChannelAmplitudes.Empty;

            if (!IsRunning.Value)
            {
                Array.Clear(frequencyAmplitudes, 0, frequencyAmplitudes.Length);
                cachedAmplitudes = new ChannelAmplitudes(0f, 0f, frequencyAmplitudes);
                return cachedAmplitudes;
            }

            long currentTick = Environment.TickCount64;

            long elapsed = currentTick - lastAmplitudesFetchTick;
            if (elapsed < 15)
                return cachedAmplitudes;

            lastAmplitudesFetchTick = currentTick;

            int result = Mixer != null
                ? BassMix.ChannelGetData(ChannelHandle, fftBuffer, (int)DataFlags.FFT512)
                : Bass.ChannelGetData(ChannelHandle, fftBuffer, (int)DataFlags.FFT512);

            if (result < 0)
            {
                Array.Clear(frequencyAmplitudes, 0, frequencyAmplitudes.Length);
                cachedAmplitudes = new ChannelAmplitudes(AmplitudeLeft, AmplitudeRight, frequencyAmplitudes);
                return cachedAmplitudes;
            }

            // Ease each bin toward its new reading instead of snapping. Framerate-independent:
            // the fraction of the OLD value retained shrinks as more time passes.
            float retain = (float)Math.Pow(amplitude_retain_per_frame, elapsed / amplitude_reference_frame_ms);
            for (int i = 0; i < frequencyAmplitudes.Length; i++)
                frequencyAmplitudes[i] = fftBuffer[i] + (frequencyAmplitudes[i] - fftBuffer[i]) * retain;

            cachedAmplitudes = new ChannelAmplitudes(AmplitudeLeft, AmplitudeRight, frequencyAmplitudes);
            return cachedAmplitudes;
        }
    }

    private bool isDisposed;

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        manager.EnqueueAction(() =>
        {
            Mixer?.RemoveChannel(this);

            if (isStream)
            {
                Bass.StreamFree(ChannelHandle);
            }

            manager.RemoveChannel(this);

            IsRunning.Value = false;
            OnStart = null!;
            OnStop = null!;
            OnEnd = null!;

            // Unpin the sync procedure
            endSyncProcedure = null;

            var disposed = Disposed;
            Disposed = null;
            disposed?.Invoke();
        });
    }
}
