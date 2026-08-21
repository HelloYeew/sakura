// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
/// SDL3 implementation of <see cref="IAudioManager"/>: owns the output device, the mix thread, and
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
    /// This is the output latency, and it is generous on purpose — see the class remarks. Phase 3
    /// replaces this with <c>SDL_HINT_AUDIO_DEVICE_SAMPLE_FRAMES</c> and a configurable target once
    /// the GC can no longer interrupt the mix loop.
    /// </remarks>
    private const double target_queue_ms = 80;

    /// <summary>
    /// Frames the native engine mixes at a time. Not the device buffer — SDL asks for whatever its
    /// buffer needs and this is how finely that request is chopped up.
    /// </summary>
    private const int native_mix_block_frames = 128;

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
    private long underruns;
    private long mixMicroseconds;
    private double queuedMilliseconds;

    private bool isDisposed;

    /// <param name="useNativeMixEngine">
    /// Whether to mix in <c>libsakura-audio</c> when it is available. False forces the managed
    /// reference mixer, which is what <see cref="AudioBackend.SDLManaged"/> selects.
    /// </param>
    /// <exception cref="InvalidOperationException">SDL audio could not be started.</exception>
    public SDLAudioManager(bool useNativeMixEngine = true)
    {
        // Decoding runs through FFmpeg, so bring it up here rather than lazily on the first track:
        // a missing or mis-located native library should fail at startup, where it is diagnosable.
        FFmpegLibrary.EnsureInitialized();

        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
            throw new InvalidOperationException($"Failed to initialise SDL audio: {SDL_GetError()}");

        ownsAudioSubsystem = true;

        SampleRate = queryDeviceSampleRate();

        if (useNativeMixEngine)
            nativeEngine = tryCreateNativeEngine();

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

        logInitialisationDetails();
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
    private static SDLNativeAudioMixer createNativeMixer(SakuraAudioEngine engine)
    {
        uint node = engine.CreateMixer();

        if (node == 0 || !engine.AddChild(engine.Root, node))
            throw new InvalidOperationException("The native mix engine would not create a master mixer.");

        return new SDLNativeAudioMixer(engine, node);
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
    private void logInitialisationDetails()
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
            double deviceBufferMs = deviceSpec.freq > 0 ? deviceFrames / (double)deviceSpec.freq * 1000.0 : 0;
            Logger.Verbose($"Device format: {deviceSpec.freq} Hz, {deviceSpec.channels} ch, {formatName(deviceSpec.format)}");
            Logger.Verbose($"Device buffer: {deviceFrames} frames ({deviceBufferMs:F1} ms)");
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

        while (!cancellation.IsCancellationRequested)
        {
            double queued = queuedMs();
            queuedMilliseconds = queued;

            if (queued >= target_queue_ms)
            {
                // Sleep well short of the queue depth, so scheduling jitter cannot drain it.
                Thread.Sleep(1);
                continue;
            }

            // Nothing queued while audio is playing means the device ran dry and the listener heard
            // it. This is the number that says whether the design is holding up.
            if (queued <= 0 && runningVoices() > 0)
                Interlocked.Increment(ref underruns);

            stopwatch.Restart();
            mixOneBlock();
            mixMicroseconds = stopwatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;
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

        var channel = new SDLNativeAudioChannel(nativeEngine, node, lengthMs);

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

        var channel = new SDLNativeAudioChannel(nativeEngine, node, feeder.LengthMs, feeder);

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
            GlobalStatistics.Get<long>("Audio", "SDL Underruns").Value = stats.Starvations;
            GlobalStatistics.Get<long>("Audio", "SDL Put Failures").Value = stats.PutFailures;
            GlobalStatistics.Get<int>("Audio", "SDL Active Voices").Value = stats.ActiveVoices;
            return;
        }

        GlobalStatistics.Get<long>("Audio", "SDL Underruns").Value = Interlocked.Read(ref underruns);
        GlobalStatistics.Get<long>("Audio", "SDL Mix Block (µs)").Value = mixMicroseconds;
        GlobalStatistics.Get<int>("Audio", "SDL Active Voices").Value = runningVoices();
        GlobalStatistics.Get<double>("Audio", "SDL Queued (ms)").Value = Math.Round(queuedMilliseconds, 1);
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
