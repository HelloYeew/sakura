// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using ManagedBass;
using ManagedBass.Mix;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Audio.BassEngine;

/// <summary>
/// BASS implementation of <see cref="IAudioChannel"/>.
/// </summary>
internal class BassAudioChannel : IAudioChannel
{
    public event Action OnStart;
    public event Action OnStop;
    public event Action OnEnd;

    public ReactiveBool IsRunning { get; } = new ReactiveBool();

    public int ChannelHandle { get; }
    private readonly float originalFrequency;

    private readonly BassAudioManager manager;
    private readonly bool isStream;
    private SyncProcedure endSyncProcedure; // Keep a reference to prevent GC
    private bool isLooping;
    private int cachedLevel;
    private long lastLevelFetchTick;
    public bool AutoDispose { get; set; } = false;

    public BassAudioMixer Mixer { get; internal set; }

    public BassAudioChannel(int channelHandle, BassAudioManager manager, bool isStream, BassAudioMixer mixer = null)
    {
        ChannelHandle = channelHandle;
        this.manager = manager;
        this.isStream = isStream;
        Mixer = mixer;

        Bass.ChannelGetAttribute(ChannelHandle, ChannelAttribute.Frequency, out float freq);
        originalFrequency = freq > 0 ? freq : 44100;

        // Set up a sync to fire the OnEnd event
        endSyncProcedure = new SyncProcedure(OnChannelEnd);
        Bass.ChannelSetSync(ChannelHandle, SyncFlags.End | SyncFlags.Mixtime, 0, endSyncProcedure);

        // Set up reactive property bindings
        IsRunning.ValueChanged += e =>
        {
            if (e.NewValue) OnStart?.Invoke();
            else OnStop?.Invoke();
        };

        Volume.ValueChanged += e => BassUtils.CheckError(Bass.ChannelSetAttribute(ChannelHandle, ChannelAttribute.Volume, (float)e.NewValue), "setting volume");
        bool isFreqInitialized = false;
        Frequency.ValueChanged += e =>
        {
            if (!isFreqInitialized)
            {
                isFreqInitialized = true;
                if (e.NewValue == 1.0) return;
            }
            BassUtils.CheckError(Bass.ChannelSetAttribute(ChannelHandle, ChannelAttribute.Frequency, (float)(e.NewValue * originalFrequency)), "setting frequency");
        };
        Balance.ValueChanged += e => BassUtils.CheckError(Bass.ChannelSetAttribute(ChannelHandle, ChannelAttribute.Pan, (float)e.NewValue), "setting balance");
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
        manager.ScheduleMainThreadAction(() =>
        {
            OnEnd?.Invoke();
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
        if (Mixer != null)
        {
            BassUtils.CheckError(BassMix.ChannelRemoveFlag(ChannelHandle, BassFlags.MixerChanPause), "resuming mixer channel");
            IsRunning.Value = true;
        }
        else if (BassUtils.CheckError(Bass.ChannelPlay(ChannelHandle, false), "playing channel"))
        {
            IsRunning.Value = true;
        }
    }

    public void Stop()
    {
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
    }

    public void Pause()
    {
        if (Mixer != null)
        {
            BassUtils.CheckError(BassMix.ChannelAddFlag(ChannelHandle, BassFlags.MixerChanPause), "pausing mixer channel");
            IsRunning.Value = false;
        }
        else if (BassUtils.CheckError(Bass.ChannelPause(ChannelHandle), "pausing channel"))
        {
            IsRunning.Value = false;
        }
    }

    public Reactive<double> Volume { get; } = new Reactive<double>(1.0);
    public Reactive<double> Frequency { get; } = new Reactive<double>(1.0);
    public Reactive<double> Balance { get; } = new Reactive<double>(0.0);
    private double restartPoint;

    public double CurrentTime
    {
        get
        {
            long pos = Bass.ChannelGetPosition(ChannelHandle);
            return Bass.ChannelBytes2Seconds(ChannelHandle, pos) * 1000.0;
        }
        set
        {
            long pos = Bass.ChannelSeconds2Bytes(ChannelHandle, value / 1000.0);
            Bass.ChannelSetPosition(ChannelHandle, pos);
        }
    }

    public double Length
    {
        get
        {
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
            if (isLooping)
                Bass.ChannelFlags(ChannelHandle, BassFlags.Loop, BassFlags.Loop);
            else
                Bass.ChannelFlags(ChannelHandle, BassFlags.Default, BassFlags.Loop);
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
        // snapshot without advancing the decode stream twice.
        if (currentTick - lastLevelFetchTick < 15)
        {
            return cachedLevel;
        }

        lastLevelFetchTick = currentTick;

        if (Mixer != null)
        {
            // Use Mix version of it to prevent consuming the buffer
            cachedLevel = BassMix.ChannelGetLevel(ChannelHandle);
        }
        else
        {
            cachedLevel = Bass.ChannelGetLevel(ChannelHandle);
        }

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

    private bool isDisposed;

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        Mixer?.RemoveChannel(this);

        // If it's a stream, we free the stream.
        // If it's a sample channel, BASS manages it, and it's freed when the sample is freed.
        if (isStream)
        {
            Bass.StreamFree(ChannelHandle);
        }

        manager.RemoveChannel(this);

        IsRunning.Value = false;
        OnStart = null;
        OnStop = null;
        OnEnd = null;

        // Unpin the sync procedure
        endSyncProcedure = null;
    }
}
