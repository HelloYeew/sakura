// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Threading;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Sakura.Framework.Statistic;
using SDL;
using static SDL.SDL3;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// SDL3 implementation of <see cref="IAudioManager"/> that owns the output device, the mix thread, and
/// the two master mixers.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed unsafe class SDLAudioManager : IAudioManager, ISDLAudioContext, IDisposable
{
    /// <summary>
    /// Frames mixed per block. At 44.1kHz this is ~11.6ms of audio, small enough that a volume or
    /// seek change is applied promptly, large enough that per-block overhead is negligible.
    /// </summary>
    private const int mix_block_frames = 512;

    /// <summary>
    /// How much audio to keep queued on the device.
    /// </summary>
    /// <remarks>
    /// This is the output latency, and it is generous on purpose
    /// replaces this with <c>SDL_HINT_AUDIO_DEVICE_SAMPLE_FRAMES</c> and a configurable target once
    /// the GC can no longer interrupt the mix loop.
    /// </remarks>
    private const double target_queue_ms = 80;

    /// <summary>
    /// Frames the native engine mixes at a time. Not the device buffer — SDL asks for whatever its
    /// buffer needs and this is how finely that request is chopped up.
    /// </summary>
    private const int native_mix_block_frames = 128;

    /// <summary>
    /// Device buffer size asked of SDL, in frames, when nothing else is configured.
    /// </summary>
    /// <remarks>
    /// This is the output latency on the native engine, and therefore the single number this whole
    /// backend exists to lower: 128 frames is 2.7 ms at 48 kHz where SDL's own default on the
    /// reference machine was 1024 (21.3 ms).
    /// </remarks>
    internal const int DEFAULT_DEVICE_BUFFER_FRAMES = 128;

    /// <summary>
    /// Bounds on <c>FrameworkSetting.AudioDeviceBufferFrames</c>. Below the lower bound no
    /// driver will honor the request anyway; above the upper one the setting has stopped being a
    /// latency control.
    /// </summary>
    private const int min_device_buffer_frames = 32;
    private const int max_device_buffer_frames = 8192;

    /// <summary>
    /// How often to re-read the output device's format, in milliseconds.
    /// </summary>
    private const double device_check_interval_ms = 1000;

    private const int fallback_sample_rate = 44100;
    private const int output_channels = 2;

    public int SampleRate { get; }

    public int Channels => output_channels;

    public Reactive<double> MasterVolume { get; } = new Reactive<double>(1.0);
    public Reactive<double> TrackVolume { get; } = new Reactive<double>(1.0);
    public Reactive<double> SampleVolume { get; } = new Reactive<double>(1.0);

    private readonly ISDLMixer trackMixer;
    private readonly ISDLMixer sampleMixer;

    public IAudioMixer TrackMixer => trackMixer;
    public IAudioMixer SampleMixer => sampleMixer;

    /// <summary>
    /// The native mix engine, or null when this manager is mixing in managed code.
    /// </summary>
    /// <remarks>
    /// Null is a supported state, not a failure: the managed mixer is the reference implementation and
    /// the fallback for any platform <c>libsakura-audio</c> has not been built for. A backend that
    /// degrades to managed mixing is much better than one that refuses to start.
    /// </remarks>
    private readonly SakuraAudioEngine? nativeEngine;

    internal SakuraAudioEngine? NativeEngine => nativeEngine;

    /// <summary>
    /// Whether the device callback is mixing inside <c>libsakura-audio</c> rather than on this
    /// manager's own mix thread.
    /// </summary>
    internal bool UsesNativeMixEngine => nativeEngine != null;

    /// <summary>
    /// The device buffer SDL granted, in frames, or 0 where it would not say.
    /// </summary>
    /// <remarks>
    /// What was granted, not what was asked for — a driver is free to round the hint to its own
    /// quantum or ignore it outright, and only the granted figure is the output latency.
    /// </remarks>
    internal int DeviceBufferFrames => deviceBufferFrames;

    // The same two mixers as above at their concrete type, set only in managed mode, because Fill is
    // not part of the shared mixer surface.
    private readonly SDLAudioMixer? managedTrackMixer;
    private readonly SDLAudioMixer? managedSampleMixer;

    private readonly ConcurrentQueue<Action> audioThreadActions = new ConcurrentQueue<Action>();
    private readonly List<ISDLChannel> activeChannels = new List<ISDLChannel>();

    /// <summary>
    /// Immutable view of <see cref="activeChannels"/>, rebuilt only when membership changes, so the
    /// per-frame walk in <see cref="Update"/> allocates nothing.
    /// </summary>
    private volatile ISDLChannel[] activeChannelSnapshot = Array.Empty<ISDLChannel>();

    private readonly AudioDecodeScheduler decodeScheduler = new AudioDecodeScheduler();

    private readonly SDL_AudioStream* deviceStream;

    /// <summary>
    /// The push-model mix thread, or null when the native engine owns the device callback.
    /// </summary>
    private readonly Thread? mixThread;

    private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

    private readonly float[] mixBuffer = new float[mix_block_frames * output_channels];

    private readonly bool ownsAudioSubsystem;

    // Written on the mix thread, published to GlobalStatistics from Update on the audio thread so
    // the mix loop does no dictionary work.
    private long mixMicroseconds;
    private double queuedMilliseconds;

    /// <summary>
    /// The managed mixer's starvation measurement. Unused on the native engine, which counts its own
    /// accurately from a thread the GC cannot stop.
    /// </summary>
    private readonly DeviceStarvationTracker starvation = new DeviceStarvationTracker();

    /// <summary>
    /// Frames the device buffer actually turned out to be, or 0 where SDL would not say.
    /// </summary>
    /// <remarks>
    /// What was asked for and what was granted are different numbers: the hint is a request, and a
    /// driver is free to round it, clamp it, or ignore it. Only the granted one is a latency figure,
    /// so only the granted one is logged and published.
    /// </remarks>
    private int deviceBufferFrames;

    /// <summary>
    /// The device sample rate seen at the last <see cref="checkForDeviceChange"/>, used to notice
    /// the output device being swapped under us.
    /// </summary>
    private int lastSeenDeviceRate;

    private int lastSeenDeviceBufferFrames;

    /// <summary>
    /// The device buffer as a duration, from the device's own rate rather than the mix rate, since
    /// the two can differ after a device change.
    /// </summary>
    /// <remarks>
    /// Microseconds in an int rather than milliseconds in a double because this is
    /// written on the update thread and read by <see cref="OutputLatencyMs"/> from wherever a channel
    /// happens to be asked for its position, and a double is not guaranteed tear-free on the
    /// 32-bit runtimes this framework ships to. Microseconds are three more digits than a buffer
    /// period needs.
    /// </remarks>
    private volatile int deviceBufferMicroseconds;

    private double sinceDeviceCheckMs;

    private readonly UnderrunWatchdog underrunWatchdog = new UnderrunWatchdog();

    /// <summary>
    /// Device callbacks the native engine had served as of the previous <see cref="Update"/>.
    /// </summary>
    /// <remarks>
    /// The engine publishes the duration of the "most recent" callback, so a frame that saw no
    /// new callback would otherwise re-read the last one's figure and count it again. Comparing the
    /// count is what makes each frame's reading a fresh observation, and what tells an idle or stopped
    /// device apart from a device being served promptly.
    /// </remarks>
    private long lastObservedCallbacks;

    /// <summary>
    /// Milliseconds the device queue had spent dry as of the previous <see cref="Update"/>. The
    /// managed mixer's half of the same question.
    /// </summary>
    private double lastObservedStarvedMs;

    /// <summary>
    /// This manager's own underrun count, as of the last <see cref="Update"/>.
    /// </summary>
    /// <remarks>
    /// The published statistics are process-global and are overwritten by whichever manager updated
    /// most recently, which makes them useless for asking "did this device starve" See <see cref="UnderrunWatchdog"/>
    /// for more measurement info.
    /// </remarks>
    private long lastPublishedUnderruns;

    internal long Underruns => Interlocked.Read(ref lastPublishedUnderruns);

    /// <summary>
    /// Milliseconds the device spent with nothing to play, and the longest single interval of the mix
    /// loop. Both are zero on the native engine, which has no mix loop of its own to measure.
    /// </summary>
    internal double StarvedMilliseconds => nativeEngine == null ? starvation.StarvedMilliseconds : 0;

    internal double LongestMixGapMilliseconds => nativeEngine == null ? starvation.LongestGapMilliseconds : 0;

    /// <summary>
    /// The buffer size that was asked for, kept so the watchdog knows what to double.
    /// </summary>
    private readonly int requestedDeviceBufferFrames;

    /// <summary>
    /// Where to record a buffer size the machine turned out to need, or null where nothing is
    /// persisting settings — a test, or a host that configured the manager directly.
    /// </summary>
    private readonly Action<int>? persistDeviceBufferFrames;

    /// <summary>
    /// Sampled once per <see cref="Update"/> and read by every channel, so all positions in a frame
    /// are compensated by the same figure and no channel pays a P/Invoke per read.
    /// </summary>
    private volatile int outputLatencyFrames;

    private bool isDisposed;

    /// <param name="useNativeMixEngine">
    /// Whether to mix in <c>libsakura-audio</c> when it is available. False forces the managed
    /// reference mixer, which is what <see cref="AudioBackend.SDLManaged"/> selects.
    /// </param>
    /// <param name="requestedDeviceBufferFrames">
    /// Device buffer size to ask SDL for, in frames, or 0 to leave SDL's own default alone. Only
    /// meaningful on the native engine — see <see cref="applyDeviceBufferHint"/>.
    /// </param>
    /// <param name="persistDeviceBufferFrames">
    /// Called with a larger buffer size when this machine turns out not to keep up with the one it was
    /// given, so the next launch starts somewhere that works — see
    /// <see cref="handleSustainedUnderruns"/>. Null disables the backoff and leaves it a warning.
    /// </param>
    /// <exception cref="InvalidOperationException">SDL audio could not be started.</exception>
    public SDLAudioManager(bool useNativeMixEngine = true, int requestedDeviceBufferFrames = DEFAULT_DEVICE_BUFFER_FRAMES,
                           Action<int>? persistDeviceBufferFrames = null)
    {
        this.requestedDeviceBufferFrames = requestedDeviceBufferFrames;
        this.persistDeviceBufferFrames = persistDeviceBufferFrames;

        // Decoding runs through FFmpeg, so bring it up here rather than lazily on the first track:
        // a missing or mis-located native library should fail at startup, where it is diagnosable.
        FFmpegLibrary.EnsureInitialized();

        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
            throw new InvalidOperationException($"Failed to initialise SDL audio: {SDL_GetError()}");

        ownsAudioSubsystem = true;

        SampleRate = queryDeviceSampleRate();

        if (useNativeMixEngine)
            nativeEngine = tryCreateNativeEngine();

        applyDeviceBufferHint(requestedDeviceBufferFrames);

        var spec = new SDL_AudioSpec
        {
            format = SDLAudioConverter.SAMPLE_FORMAT,
            channels = output_channels,
            freq = SampleRate
        };

        if (nativeEngine != null)
        {
            // The callback is native, and reaching it involves no managed code at all: this is a raw
            // function pointer straight into libsakura-audio, not a marshalled delegate. A delegate
            // here would put a managed frame on the real-time thread and hand a GC pause the ability
            // to underrun the device, which is the entire thing this backend exists to avoid.
            var callback = (delegate* unmanaged[Cdecl]<IntPtr, SDL_AudioStream*, int, int, void>)SakuraAudioEngine.StreamCallback;
            deviceStream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, callback, nativeEngine.Handle);
        }
        else
        {
            // No callback: the managed mixer pushes from its own thread, so nothing SDL calls can ever
            // be waiting on managed code either. It buys that with a much larger queue — see
            // target_queue_ms.
            deviceStream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, null, IntPtr.Zero);
        }

        if (deviceStream == null)
        {
            nativeEngine?.Dispose();
            SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
            throw new InvalidOperationException($"Failed to open an SDL audio device: {SDL_GetError()}");
        }

        if (nativeEngine != null)
        {
            trackMixer = createNativeMixer(nativeEngine);
            sampleMixer = createNativeMixer(nativeEngine);
        }
        else
        {
            var managedTracks = new SDLAudioMixer(this);
            var managedSamples = new SDLAudioMixer(this);

            // Kept at their concrete type as well, because Fill is the managed mixer's alone: in
            // native mode there is no mix loop here to call it.
            managedTrackMixer = managedTracks;
            managedSampleMixer = managedSamples;

            trackMixer = managedTracks;
            sampleMixer = managedSamples;
        }

        trackMixer.IsRunning.Value = true;
        sampleMixer.IsRunning.Value = true;

        TrackVolume.ValueChanged += e => trackMixer.Volume.Value = e.NewValue * MasterVolume.Value;
        SampleVolume.ValueChanged += e => sampleMixer.Volume.Value = e.NewValue * MasterVolume.Value;
        MasterVolume.ValueChanged += e =>
        {
            trackMixer.Volume.Value = TrackVolume.Value * e.NewValue;
            sampleMixer.Volume.Value = SampleVolume.Value * e.NewValue;
        };

        if (!SDL_ResumeAudioStreamDevice(deviceStream))
            Logger.Error($"Failed to start the SDL audio device: {SDL_GetError()}");

        if (nativeEngine == null)
        {
            mixThread = new Thread(runMixLoop)
            {
                Name = "SdlAudioMix",
                IsBackground = true,

                // The highest priority in the engine: everything else can be late by a frame, this
                // cannot be late by a block.
                Priority = ThreadPriority.Highest
            };

            mixThread.Start();
        }

        logInitialisationDetails(requestedDeviceBufferFrames);
    }

    /// <summary>
    /// Asks SDL for a device buffer of <paramref name="requestedFrames"/>, which is the output
    /// latency and therefore the point of this backend.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only applied when the native engine is mixing. In managed mode the device buffer is not what
    /// sets the latency — the push model keeps <see cref="target_queue_ms"/> of audio ahead of the
    /// device on purpose, because a managed mix loop can be interrupted by the GC and that queue is
    /// what stops the interruption being audible. Shrinking the device buffer underneath a queue an
    /// order of magnitude larger buys nothing and costs a callback per 5 ms, so the fallback is left
    /// exactly as SDL configured it and the log says why.
    /// </para>
    /// <para>
    /// The hint is global to SDL and is read when a device is opened, so it must be set before
    /// <c>SDL_OpenAudioDeviceStream</c> and it affects any device opened after this point. It is a
    /// request: drivers round it to their own quantum, clamp it to their own floor, or ignore it.
    /// <see cref="deviceBufferFrames"/> is what was actually granted, and that is the number worth
    /// reading.
    /// </para>
    /// </remarks>
    private void applyDeviceBufferHint(int requestedFrames)
    {
        if (requestedFrames <= 0)
            return;

        if (nativeEngine == null)
        {
            Logger.Verbose($"Device buffer hint of {requestedFrames} frames not applied: the managed mixer's latency is its "
                           + $"{target_queue_ms} ms queue, not the device buffer.");
            return;
        }

        int frames = Math.Clamp(requestedFrames, min_device_buffer_frames, max_device_buffer_frames);

        if (frames != requestedFrames)
        {
            Logger.Warning($"An audio device buffer of {requestedFrames} frames is outside the supported range "
                           + $"{min_device_buffer_frames}-{max_device_buffer_frames}; using {frames}.");
        }

        if (!SDL_SetHint(SDL_HINT_AUDIO_DEVICE_SAMPLE_FRAMES, frames.ToString(CultureInfo.InvariantCulture)))
            Logger.Warning($"SDL would not accept a device buffer hint of {frames} frames: {SDL_GetError()}");
    }

    /// <summary>
    /// How far ahead of the listener the audio already produced is, in milliseconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A channel's position counts frames it has produced, and a produced frame is not an audible
    /// frame until the device has played it. Subtracting this is what makes
    /// <see cref="IAudioChannel.CurrentTime"/> mean "what the listener is hearing" rather than "what
    /// the mixer has reached", which is the difference between music and gameplay agreeing and
    /// drifting apart by a buffer.
    /// </para>
    /// <para>
    /// Two terms, and both are needed. <see cref="SDL_GetAudioStreamQueued"/> is what is waiting in
    /// the stream and has not been handed to the device — the managed mixer's whole latency, and
    /// essentially zero on the native engine, because there SDL asks the callback for exactly what it
    /// needs and takes it immediately, leaving nothing queued for a poll to find. The device buffer is
    /// the rest: audio the device has been given but has not finished playing. Reading only the queue
    /// would have compensated the managed path correctly and the native path not at all, which is the
    /// wrong way round — the native path is the one whose position feeds gameplay.
    /// </para>
    /// <para>
    /// Whatever the driver and the hardware hold below SDL is not reported by any portable API and is
    /// not included, so this is a floor on the true output latency rather than all of it. That is
    /// still the right number to subtract: it is the part that changes with the buffer size, and the
    /// remainder is a fixed offset that belongs in a user-facing calibration rather than in the clock.
    /// </para>
    /// </remarks>
    public double OutputLatencyMs => outputLatencyFrames / (double)SampleRate * 1000.0 + deviceBufferMicroseconds / 1000.0;

    /// <summary>
    /// Re-reads the queue depth so every channel in this frame compensates by the same figure.
    /// </summary>
    private void sampleOutputLatency()
    {
        int bytes = SDL_GetAudioStreamQueued(deviceStream);
        outputLatencyFrames = bytes <= 0 ? 0 : bytes / (sizeof(float) * output_channels);
    }

    /// <summary>
    /// Recomputes the device half of <see cref="OutputLatencyMs"/> after the device format is read.
    /// </summary>
    private void updateDeviceBufferDuration(int frames, int rate) =>
        deviceBufferMicroseconds = rate > 0 ? (int)Math.Round(frames / (double)rate * 1_000_000.0) : 0;

    /// <summary>
    /// This frame's answer, on the native engine, to whether the device is being served in time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The callback mixes on demand into the device's own buffer, so the deadline it is racing is that
    /// buffer's duration: take longer than <see cref="deviceBufferMicroseconds"/> to produce
    /// <see cref="deviceBufferFrames"/> and the device has run out before the audio arrived. That is
    /// the failure a larger buffer actually fixes, and the only one; it doubles the deadline while
    /// the per-callback fixed cost (draining the command queue, the stream sync, both of which walk
    /// every node whatever the block size) stays where it was.
    /// </para>
    /// <para>
    /// Sampled a frame once out of the several hundred callbacks a second rather than counted on the
    /// audio thread, which would mean a new native stat and a library round-trip. The phase is
    /// arbitrary, so it is a fair sample of callbacks, and the watchdog reads a share rather than a
    /// total precisely so a sampled figure is enough to act on.
    /// </para>
    /// <para>
    /// A callback duration of zero means the platform has no <c>timespec_get</c> (Android below API
    /// 29) and the engine could not time itself, so there is nothing to judge and every frame reads as
    /// idle. The backoff simply never fires there, which is the right way round.
    /// </para>
    /// </remarks>
    private UnderrunWatchdog.Observation observeNativeCallback(SakuraAudioStats stats)
    {
        if (stats.Callbacks == lastObservedCallbacks || stats.CallbackMicroseconds <= 0 || deviceBufferMicroseconds <= 0)
            return UnderrunWatchdog.Observation.Idle;

        lastObservedCallbacks = stats.Callbacks;

        return stats.CallbackMicroseconds > deviceBufferMicroseconds
            ? UnderrunWatchdog.Observation.Missed
            : UnderrunWatchdog.Observation.Met;
    }

    /// <summary>
    /// The same answer on the managed mixer, where the device is pushed to rather than pulled from.
    /// </summary>
    /// <remarks>
    /// There is no callback to time, so the failure is measured after the fact: the queue this mixer
    /// keeps ahead of the device ran dry, which <see cref="DeviceStarvationTracker"/> already records
    /// in milliseconds. Any increase since the last frame is a miss. Unlike the native engine's voice
    /// starvation this is genuinely device-level — a decoder falling behind pushes silence into a
    /// queue that stays full and does not show up here.
    /// </remarks>
    private UnderrunWatchdog.Observation observeManagedQueue()
    {
        double starved = starvation.StarvedMilliseconds;

        if (starved > lastObservedStarvedMs)
        {
            lastObservedStarvedMs = starved;
            return UnderrunWatchdog.Observation.Missed;
        }

        return runningVoices() > 0 ? UnderrunWatchdog.Observation.Met : UnderrunWatchdog.Observation.Idle;
    }

    /// <summary>
    /// Acts, once, when the device is starving steadily rather than occasionally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes an aggressive default shippable rather than a gamble. The shipped device
    /// buffer is chosen on measurement from the machines anyone has actually run — see AUDIO_SDL.md —
    /// and the thing measurement cannot rule out is scheduling jitter somewhere nobody has tried. So
    /// the default is a starting point that retreats under evidence rather than a promise.
    /// </para>
    /// <para>
    /// The retreat is persisted and takes effect next launch rather than being applied here. Growing
    /// the buffer live would mean tearing down and reopening the device underneath a callback that is
    /// very likely running, for an audible gap, on a code path that exists for nothing else — and the
    /// session it would rescue is one that has already been missing its deadlines for five seconds.
    /// Writing the number down costs one line of config and fixes the machine permanently.
    /// </para>
    /// </remarks>
    private void handleSustainedUnderruns(UnderrunWatchdog.Observation observation, double frameTime)
    {
        var verdict = underrunWatchdog.Poll(observation, frameTime);

        if (verdict == null)
            return;

        // Says what was measured, and stops there. Telling someone they heard crackling when they did
        // not is how a warning gets learned as noise — and a miss is a deadline the output path did not
        // make, which a driver with slack in its own buffering can still absorb without a click.
        string observed = $"The audio device went unfed on {verdict.Value.Misses} of the {verdict.Value.Observations} frames "
                          + $"sampled in the last {UnderrunWatchdog.CHECK_INTERVAL_MS / 1000:F0} seconds "
                          + $"({verdict.Value.Fraction:P0}). The device buffer is {deviceBufferFrames} frames "
                          + $"({deviceBufferMicroseconds / 1000.0:F1} ms). This is measured, not heard: at this rate it "
                          + "usually clicks, but not hearing anything does not mean nothing was missed.";

        // Only the native engine's latency is the device buffer; the managed mixer's is its own queue
        // and applyDeviceBufferHint does not even set the hint for it. Doubling a number that is never
        // read would be a silent no-op dressed up as a fix, so that path gets the warning and nothing
        // else.
        int? next = nativeEngine == null ? null : UnderrunWatchdog.NextBufferSize(requestedDeviceBufferFrames);

        if (next == null || persistDeviceBufferFrames == null)
        {
            // Nothing to do but say so. Either the buffer was SDL's own choice and is already the
            // driver's preference, or it is large enough that "too small" has stopped explaining
            // anything, or this is the managed mixer whose latency the buffer does not set, or nobody
            // gave this manager somewhere to write the answer down.
            Logger.Warning($"{observed} Raising AudioDeviceBufferFrames in framework.ini may help, but this buffer is "
                           + "already at or past the point where a larger one is the likely fix — something else is "
                           + "competing for the audio device. Reported once per run.");
            return;
        }

        persistDeviceBufferFrames(next.Value);

        Logger.Warning($"{observed} AudioDeviceBufferFrames has been raised to {next} for the next launch, which will "
                       + $"trade {(next.Value - requestedDeviceBufferFrames) / (double)SampleRate * 1000.0:F1} ms of extra "
                       + "output latency for a device that keeps up. Set it back in framework.ini to retry the smaller "
                       + "buffer. Reported once per run.");
    }

    /// <summary>
    /// Notices the output device being swapped or reconfigured under a running stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Polled rather than driven by <c>SDL_EVENT_AUDIO_DEVICE_*</c>, because the event queue belongs
    /// to the window and an audio backend that only works when a window is pumping events is a
    /// backend that stops working in tests and headless hosts. A device change is a human-scale
    /// event, so once a second is prompt enough and costs one struct fill.
    /// </para>
    /// <para>
    /// Nothing is reopened. The stream was opened on the default playback device and SDL migrates it
    /// itself, converting our fixed mix rate to whatever the new device wants — so pitch does not
    /// break, which is the failure this was originally expected to be. What does break is quieter:
    /// the latency figures are now measured against a buffer that no longer exists, and a mix rate
    /// that no longer matches the device means SDL is resampling everything a second time, after the
    /// voices already resampled it once. Both are worth a line in the log and neither is worth
    /// tearing down a working stream for.
    /// </para>
    /// </remarks>
    private void checkForDeviceChange(double frameTime)
    {
        sinceDeviceCheckMs += frameTime;

        if (sinceDeviceCheckMs < device_check_interval_ms)
            return;

        sinceDeviceCheckMs = 0;

        var device = SDL_GetAudioStreamDevice(deviceStream);

        if (device == 0)
        {
            // The stream is bound to nothing, so nothing is playing and nothing will say so. This is
            // the silent stop the poll exists to catch.
            if (lastSeenDeviceRate != 0)
            {
                Logger.Warning("The SDL audio device went away and the stream is no longer bound to one. Audio has stopped.");
                lastSeenDeviceRate = 0;
                lastSeenDeviceBufferFrames = 0;
            }

            return;
        }

        SDL_AudioSpec spec;
        int frames;

        if (!SDL_GetAudioDeviceFormat(device, &spec, &frames))
            return;

        if (spec.freq == lastSeenDeviceRate && frames == lastSeenDeviceBufferFrames)
            return;

        bool first = lastSeenDeviceRate == 0 && lastSeenDeviceBufferFrames == 0;

        lastSeenDeviceRate = spec.freq;
        lastSeenDeviceBufferFrames = frames;
        deviceBufferFrames = frames;
        updateDeviceBufferDuration(frames, spec.freq);

        if (first)
            return;

        double bufferMs = spec.freq > 0 ? frames / (double)spec.freq * 1000.0 : 0;
        Logger.Verbose($"Audio output device changed: now {SDL_GetAudioDeviceName(device)}, {spec.freq} Hz, "
                       + $"{frames} frames ({bufferMs:F1} ms). Any latency figure recorded before this line is stale.");

        if (spec.freq != SampleRate)
        {
            Logger.Warning($"The new audio device runs at {spec.freq} Hz and the mixer is fixed at {SampleRate} Hz, so SDL is "
                           + "resampling the output a second time. Restart to mix at the device's rate.");
        }
    }

    /// <summary>
    /// Brings up the native mix engine, or returns null to fall back to managed mixing.
    /// </summary>
    /// <remarks>
    /// Every failure here is a fallback rather than an exception. A platform the library has not been
    /// built for, an ABI mismatch, an SDL export that could not be resolved: none of them are reasons
    /// for the audio backend not to start, and all of them are reasons to say so in the log.
    /// </remarks>
    private SakuraAudioEngine? tryCreateNativeEngine()
    {
        if (!SakuraAudioEngine.IsAvailable)
            return null;

        // Without this the native engine has no way to hand frames to the device.
        if (!SakuraAudioEngine.TrySetSdlPut())
            return null;

        var engine = SakuraAudioEngine.Create(SampleRate, output_channels, native_mix_block_frames);

        if (engine == null)
            return null;

        return engine;
    }

    /// <summary>
    /// Creates a master mixer node and routes it into the engine's root.
    /// </summary>
    /// <exception cref="InvalidOperationException">The node pool is exhausted.</exception>
    private SDLNativeAudioMixer createNativeMixer(SakuraAudioEngine engine)
    {
        uint node = engine.CreateMixer();

        if (node == 0 || !engine.AddChild(engine.Root, node))
            throw new InvalidOperationException("The native mix engine would not create a master mixer.");

        return new SDLNativeAudioMixer(this, engine, node);
    }

    /// <summary>
    /// Reports what the audio stack actually resolved to at startup.
    /// </summary>
    /// <remarks>
    /// Mirrors what <see cref="BassEngine.BassAudioManager"/> logs — driver, device, buffer sizes —
    /// so a bug report from either backend carries the same information. The FFmpeg and decoder
    /// lines are specific to this one: with BASS the decoders are part of the library, whereas here
    /// they depend on how the shipped FFmpeg was configured, which is exactly the thing that is
    /// awkward to diagnose after the fact.
    /// </remarks>
    private void logInitialisationDetails(int localRequestedDeviceBufferFrames)
    {
        Logger.Verbose("🔈 SDL audio initialised");

        int version = SDL_GetVersion();
        Logger.Verbose($"SDL Version: {version / 1000000}.{version / 1000 % 1000}.{version % 1000}");
        Logger.Verbose($"SDL Revision: {SDL_GetRevision()}");

        Logger.Verbose($"SDL Audio Driver: {SDL_GetCurrentAudioDriver()} (available: {availableDrivers()})");

        var device = SDL_GetAudioStreamDevice(deviceStream);
        Logger.Verbose($"Device: {SDL_GetAudioDeviceName(device)}");

        SDL_AudioSpec deviceSpec;
        int deviceFrames;

        if (SDL_GetAudioDeviceFormat(device, &deviceSpec, &deviceFrames))
        {
            deviceBufferFrames = deviceFrames;
            lastSeenDeviceRate = deviceSpec.freq;
            lastSeenDeviceBufferFrames = deviceFrames;
            updateDeviceBufferDuration(deviceFrames, deviceSpec.freq);

            double deviceBufferMs = deviceSpec.freq > 0 ? deviceFrames / (double)deviceSpec.freq * 1000.0 : 0;
            Logger.Verbose($"Device format: {deviceSpec.freq} Hz, {deviceSpec.channels} ch, {formatName(deviceSpec.format)}");

            // Says both numbers, because a driver that ignored the hint is otherwise indistinguishable
            // from one that honored it
            string granted = localRequestedDeviceBufferFrames <= 0 || nativeEngine == null || deviceFrames == localRequestedDeviceBufferFrames
                ? string.Empty
                : $", asked for {localRequestedDeviceBufferFrames}";

            Logger.Verbose($"Device buffer: {deviceFrames} frames ({deviceBufferMs:F1} ms{granted})");
        }
        else
        {
            Logger.Verbose($"Device format: unavailable ({SDL_GetError()})");
        }

        Logger.Verbose($"Mix format: {SampleRate} Hz, {output_channels} ch, {formatName(SDLAudioConverter.SAMPLE_FORMAT)}");

        if (nativeEngine != null)
        {
            Logger.Verbose($"Mix engine: libsakura-audio (native, ABI {SakuraAudioNative.ABI_VERSION}), on the device callback");
            Logger.Verbose($"Mix block: {native_mix_block_frames} frames ({native_mix_block_frames / (double)SampleRate * 1000.0:F1} ms)");
        }
        else
        {
            // Worth a warning rather than a note: this is the fallback, it is not what the backend is
            // for, and its latency is an order of magnitude worse.
            Logger.Verbose("Mix engine: managed reference mixer, pushing from its own thread"
                           + (SakuraAudioEngine.IsAvailable ? " (native engine available but not selected)" : " (libsakura-audio unavailable)"));
            Logger.Verbose($"Mix block: {mix_block_frames} frames ({mix_block_frames / (double)SampleRate * 1000.0:F1} ms)");
            Logger.Verbose($"Target queue length: {target_queue_ms} ms");
        }

        logDecoderSupport();
    }

    private static string availableDrivers()
    {
        int count = SDL_GetNumAudioDrivers();

        if (count <= 0)
            return "none reported";

        string[] names = new string[count];

        for (int i = 0; i < count; i++)
            names[i] = SDL_GetAudioDriver(i) ?? "?";

        return string.Join(", ", names);
    }

    /// <summary>
    /// Trims SDL's enum spelling down to the part worth reading — <c>SDL_AUDIO_F32LE</c> to
    /// <c>F32LE</c>.
    /// </summary>
    private static string formatName(SDL_AudioFormat format) =>
        format.ToString().Replace("SDL_AUDIO_", string.Empty);

    /// <summary>
    /// Reports the FFmpeg build in use and which audio decoders it was compiled with.
    /// </summary>
    /// <remarks>
    /// Specific to this backend: with BASS the decoders are part of the library, whereas here they
    /// depend on how the shipped FFmpeg was configured. See <see cref="FFmpegLibrary.GetAudioDecoderSupport"/>.
    /// </remarks>
    private static void logDecoderSupport()
    {
        Logger.Verbose($"FFmpeg: {FFmpegLibrary.DescribeVersions()}");

        var support = FFmpegLibrary.GetAudioDecoderSupport();
        int total = support.Present.Count + support.Missing.Count;

        Logger.Verbose($"Audio decoders ({support.Present.Count}/{total}): {string.Join(", ", support.Present)}");

        if (support.Missing.Count > 0)
        {
            Logger.Warning($"Audio decoders missing from the shipped FFmpeg build: {string.Join(", ", support.Missing)}. " +
                           "Files in those formats will fail to load.");
        }
    }

    /// <summary>
    /// Uses the device's own rate where SDL will tell us, so the common case involves no resampling
    /// between our mix and the hardware.
    /// </summary>
    private static int queryDeviceSampleRate()
    {
        SDL_AudioSpec deviceSpec;
        int deviceFrames;

        if (SDL_GetAudioDeviceFormat(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &deviceSpec, &deviceFrames) && deviceSpec.freq > 0)
            return deviceSpec.freq;

        Logger.Verbose($"Could not query the audio device format ({SDL_GetError()}); defaulting to {fallback_sample_rate}Hz.");
        return fallback_sample_rate;
    }

    #region Mixing

    private void runMixLoop()
    {
        var stopwatch = new Stopwatch();

        // Starvation is derived from how long each iteration took rather than observed while it was
        // happening, because the event worth measuring is one that stops this thread. See
        // DeviceStarvationTracker.
        //
        // The interval runs from the top of one iteration to the top of the next, so that it *contains*
        // the sleep and the mixing. Measuring from the bottom of one to the top of the next — which is
        // what this did at first — times only the loop condition, reports a longest gap of 0.0 ms on a
        // run containing second-long collections, and can never see a stall at all.
        var interval = new Stopwatch();

        double playableAtIntervalStart = 0;
        double pushedDuringInterval = 0;
        double blockMs = mix_block_frames / (double)SampleRate * 1000.0;

        while (!cancellation.IsCancellationRequested)
        {
            if (interval.IsRunning)
            {
                // What the device had to play across the interval: what was already queued when it
                // began, plus whatever this loop managed to push into it before the interval ended.
                starvation.Observe(interval.Elapsed.TotalMilliseconds, playableAtIntervalStart + pushedDuringInterval,
                    runningVoices() > 0);
            }

            interval.Restart();

            double queued = queuedMs();
            queuedMilliseconds = queued;

            playableAtIntervalStart = queued;
            pushedDuringInterval = 0;

            if (queued >= target_queue_ms)
            {
                // Sleep well short of the queue depth, so scheduling jitter cannot drain it.
                Thread.Sleep(1);
            }
            else
            {
                stopwatch.Restart();
                mixOneBlock();
                mixMicroseconds = stopwatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;

                pushedDuringInterval += blockMs;
            }
        }
    }

    private void mixOneBlock()
    {
        var block = mixBuffer.AsSpan();
        block.Clear();

        try
        {
            managedTrackMixer?.Fill(block);
            managedSampleMixer?.Fill(block);
        }
        catch (Exception e)
        {
            // A throwing voice must not kill the mix thread and silence everything.
            block.Clear();
            Logger.Error("[SDLAudioManager] Mixing failed for a block.", e);
        }

        // Sources are not normalized — a lossy decoder reconstructing a hot master genuinely exceeds
        // unity — and summing several of them exceeds it further. Clamp here, at the one place the
        // audio leaves our control, rather than quietly wrapping in the driver.
        for (int i = 0; i < block.Length; i++)
            block[i] = Math.Clamp(block[i], -1f, 1f);

        fixed (float* pointer = block)
        {
            if (!SDL_PutAudioStreamData(deviceStream, (IntPtr)pointer, block.Length * sizeof(float)))
                Logger.Error($"SDL_PutAudioStreamData failed: {SDL_GetError()}");
        }
    }

    private double queuedMs()
    {
        int bytes = SDL_GetAudioStreamQueued(deviceStream);

        if (bytes <= 0)
            return 0;

        return bytes / (double)(sizeof(float) * output_channels) / SampleRate * 1000.0;
    }

    private int runningVoices() => trackMixer.RunningChannelCount + sampleMixer.RunningChannelCount;

    #endregion

    #region Channel lifetime

    /// <summary>
    /// Builds a managed-mixer channel over <paramref name="source"/>, routes it into
    /// <paramref name="mixer"/>, and registers it for decoding and shutdown.
    /// </summary>
    internal ISDLChannel CreateChannel(IPcmSource source, IAudioMixer mixer, bool streaming)
    {
        var channel = new SDLAudioChannel(this, source);

        register(channel, mixer, streaming ? source as IDecodeSource : null);

        return channel;
    }

    /// <summary>
    /// Builds a native voice over a shared PCM buffer — the sample path — and routes it into
    /// <paramref name="mixer"/>.
    /// </summary>
    /// <remarks>
    /// The buffer's reference count is claimed by the engine for as long as the voice holds it, so the
    /// caller may release its own claim whenever it likes without pulling audio out from under a
    /// playing hitsound.
    /// </remarks>
    /// <returns>Null when the engine's voice pool is exhausted.</returns>
    internal ISDLChannel? CreateNativeBufferChannel(uint buffer, double lengthMs, IAudioMixer mixer)
    {
        if (nativeEngine == null)
            return null;

        uint node = nativeEngine.CreateVoice();

        if (node == 0)
        {
            Logger.Warning("The native mix engine is out of voices; a sample will not play.");
            return null;
        }

        if (!nativeEngine.SetVoiceBuffer(node, buffer))
        {
            nativeEngine.DestroyNode(node);
            return null;
        }

        var channel = new SDLNativeAudioChannel(this, nativeEngine, node, lengthMs);

        register(channel, mixer, null);

        return channel;
    }

    /// <summary>
    /// Builds a native voice fed by a decode thread — the track path — and routes it into
    /// <paramref name="mixer"/>.
    /// </summary>
    /// <param name="encoded">The encoded audio. Ownership passes to the returned channel.</param>
    /// <param name="mixer">Where to route the voice.</param>
    /// <returns>Null when the engine's voice pool is exhausted.</returns>
    /// <exception cref="InvalidDataException">The source could not be decoded.</exception>
    internal ISDLChannel? CreateNativeStreamingChannel(Stream encoded, IAudioMixer mixer)
    {
        if (nativeEngine == null)
            return null;

        uint node = nativeEngine.CreateVoice();

        if (node == 0)
        {
            Logger.Warning("The native mix engine is out of voices; a track will not play.");
            encoded.Dispose();
            return null;
        }

        NativeStreamFeeder feeder;

        try
        {
            feeder = new NativeStreamFeeder(encoded, nativeEngine, node);
        }
        catch
        {
            nativeEngine.DestroyNode(node);
            throw;
        }

        var channel = new SDLNativeAudioChannel(this, nativeEngine, node, feeder.LengthMs, feeder);

        register(channel, mixer, feeder);

        return channel;
    }

    /// <summary>
    /// The bookkeeping every channel needs whichever engine mixes it: routing, decode registration,
    /// and undoing both when it is disposed.
    /// </summary>
    private void register(ISDLChannel channel, IAudioMixer mixer, IDecodeSource? decodeSource)
    {
        if (decodeSource != null)
            decodeScheduler.Register(decodeSource);

        lock (activeChannels)
        {
            activeChannels.Add(channel);
            activeChannelSnapshot = activeChannels.ToArray();
        }

        mixer.AddChannel(channel);

        channel.Disposed += () =>
        {
            if (decodeSource != null)
                decodeScheduler.Unregister(decodeSource);

            mixer.RemoveChannel(channel);

            lock (activeChannels)
            {
                activeChannels.Remove(channel);
                activeChannelSnapshot = activeChannels.ToArray();
            }
        };
    }

    #endregion

    #region IAudioManager

    public ITrack CreateTrack(Stream stream) => new SDLTrack(this, stream);

    public ITrack CreateTrackFromFile(string path) => new SDLTrack(this, path);

    public ISample CreateSample(Stream stream) => new SDLSample(this, stream);

    public ISample CreateSampleFromFile(string path) => new SDLSample(this, path);

    public void EnqueueAction(Action action)
    {
        if (action.IsNotNull())
            audioThreadActions.Enqueue(action);
    }

    public void WakeDecoder() => decodeScheduler.Wake();

    public void Update(double frameTime)
    {
        while (audioThreadActions.TryDequeue(out var action))
        {
            try
            {
                action.Invoke();
            }
            catch (Exception e)
            {
                Logger.Error("[SDLAudioManager] A queued audio action failed.", e);
            }
        }

        // Before PollEvents, because a channel that reads its position while handling an end or a
        // loop should see this frame's figure rather than the last one's.
        sampleOutputLatency();
        checkForDeviceChange(frameTime);

        if (nativeEngine != null)
        {
            // The audio thread cannot raise a managed event, so anything a voice signalled — it ended,
            // it wants its decoder moved to a loop point — is turned into events here. Walked over the
            // snapshot because a channel with AutoDispose set removes itself from the list as it goes.
            foreach (var channel in activeChannelSnapshot)
                channel.PollEvents();

            // The only place the native side frees anything. Skipping it leaks voices and sample PCM.
            nativeEngine.Maintain();

            var stats = nativeEngine.GetStats();

            // "Callback", not "Mix Block": there is a real device callback to time now.
            GlobalStatistics.Get<long>("Audio", "SDL Callback (µs)").Value = stats.CallbackMicroseconds;

            // Named for what it is. This counts blocks in which a voice had less audio than the mixer
            // asked for — a decoder that fell behind, most often a seek — and not the device failing
            // to be served, which is what "underrun" reads as and what the watchdog acts on.
            GlobalStatistics.Get<long>("Audio", "SDL Voice Starvations").Value = stats.Starvations;

            Interlocked.Exchange(ref lastPublishedUnderruns, stats.Starvations);
            handleSustainedUnderruns(observeNativeCallback(stats), frameTime);
            GlobalStatistics.Get<long>("Audio", "SDL Put Failures").Value = stats.PutFailures;
            GlobalStatistics.Get<int>("Audio", "SDL Active Voices").Value = stats.ActiveVoices;
            GlobalStatistics.Get<int>("Audio", "SDL Device Buffer (frames)").Value = deviceBufferFrames;
            GlobalStatistics.Get<double>("Audio", "SDL Output Latency (ms)").Value = Math.Round(OutputLatencyMs, 2);
            return;
        }

        // Counted in mix blocks of missing output, which is this mixer's natural unit — the native
        // engine's count is per voice per block and the two are not comparable. Starved (ms) is, and is
        // published by both for exactly that reason.
        double blockMs = mix_block_frames / (double)SampleRate * 1000.0;
        long managedUnderruns = starvation.CountIn(blockMs);

        Interlocked.Exchange(ref lastPublishedUnderruns, managedUnderruns);
        GlobalStatistics.Get<long>("Audio", "SDL Underruns").Value = managedUnderruns;
        GlobalStatistics.Get<double>("Audio", "SDL Starved (ms)").Value = Math.Round(starvation.StarvedMilliseconds, 1);
        GlobalStatistics.Get<double>("Audio", "SDL Longest Mix Gap (ms)").Value = Math.Round(starvation.LongestGapMilliseconds, 1);
        GlobalStatistics.Get<long>("Audio", "SDL Mix Block (µs)").Value = mixMicroseconds;

        handleSustainedUnderruns(observeManagedQueue(), frameTime);
        GlobalStatistics.Get<int>("Audio", "SDL Active Voices").Value = runningVoices();
        GlobalStatistics.Get<double>("Audio", "SDL Queued (ms)").Value = Math.Round(queuedMilliseconds, 1);
        GlobalStatistics.Get<int>("Audio", "SDL Device Buffer (frames)").Value = deviceBufferFrames;
        GlobalStatistics.Get<double>("Audio", "SDL Output Latency (ms)").Value = Math.Round(OutputLatencyMs, 2);
    }

    public void StopAll()
    {
        foreach (var channel in activeChannelSnapshot)
            channel.Stop();
    }

    #endregion

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;

        cancellation.Cancel();

        if (mixThread != null && !mixThread.Join(TimeSpan.FromSeconds(2)))
            Logger.Error("[SDLAudioManager] Mix thread did not exit in time.");

        // Before anything is torn down: stop the device, so the native callback cannot be running
        // while the graph it walks is being dismantled underneath it.
        if (deviceStream != null && nativeEngine != null)
            SDL_PauseAudioStreamDevice(deviceStream);

        ISDLChannel[] toDispose;

        lock (activeChannels)
        {
            toDispose = activeChannels.ToArray();
            activeChannels.Clear();
            activeChannelSnapshot = Array.Empty<ISDLChannel>();
        }

        foreach (var channel in toDispose)
            channel.Dispose();

        // Channel disposal is queued onto the audio thread, and nothing else will pump it now.
        Update(0);

        trackMixer.Dispose();
        sampleMixer.Dispose();
        Update(0);

        decodeScheduler.Dispose();

        if (deviceStream != null)
            SDL_DestroyAudioStream(deviceStream);

        // Only once the stream is gone, and with it any possibility of another callback. The native
        // side deliberately does not synchronise with a callback already in flight; that is the
        // caller's job, and this is the caller.
        nativeEngine?.Dispose();

        cancellation.Dispose();

        // Refcounted, so this releases only our own claim and leaves any other SDL audio user alone.
        if (ownsAudioSubsystem)
            SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);

        Logger.Verbose("🔈 SDL audio shut down");
    }
}
