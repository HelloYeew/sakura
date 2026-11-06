// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;
using static Sakura.Framework.Audio.SdlEngine.PipeWireNative;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The native engine playing through a PipeWire stream on Linux with nothing of SDL's in between: libpipewire calls the
/// engine's process function on its real-time data thread, which mixes straight into the stream's buffer.
/// </summary>
/// <remarks>
/// <para>
/// The stream asks the graph for the same quantum SDL's does (node.latency: the buffer asked for), so the delay is much
/// the same. What it adds is knowing it: <see cref="TrySampleLatency"/> reads the stream's time (pw_stream_get_time_n),
/// the graph's delay to the device and what's still queued, as PipeWire measures them.
/// </para>
/// <para>
/// It plays on the system's default output, and the session manager moves it when the default changes. A device picked
/// by name plays through SDL instead, which knows PipeWire's node for the name it lists.
/// </para>
/// <para>
/// libpipewire runs its loop on a thread of its own. Every call on the stream is made with that loop locked.
/// </para>
/// </remarks>
internal sealed unsafe class PipeWireOutput : IDisposable
{
    // The quantum asked for when the settings ask for none: SDL's default for PipeWire.
    private const int default_frames = 1024;

    private readonly SakuraAudioEngine engine;
    private readonly int framesPerCycle;

    private nint loop;
    private nint context;
    private nint core;
    private nint stream;
    private nint native;

    // What libpipewire keeps pointers to for the stream's life: its listener's events and hook, and its format.
    private StreamEvents* events;
    private nint hook;
    private byte* format;

    private string libraryVersion = "unknown";

    // The engine's figures at the last latency sample, whose difference is what it writes a cycle.
    private long sampledCallbacks;
    private long sampledFramesMixed;
    private double framesPerCycleSeen;

    /// <summary>
    /// The rate the engine mixes at, which the stream runs at.
    /// </summary>
    public int SampleRate => engine.SampleRate;

    /// <summary>
    /// The quantum asked of the graph, in frames: the app's own part of the delay, as it writes one each cycle.
    /// </summary>
    public int FramesPerCycle => framesPerCycle;

    private PipeWireOutput(SakuraAudioEngine engine, int framesPerCycle)
    {
        this.engine = engine;
        this.framesPerCycle = framesPerCycle;
    }

    /// <summary>
    /// Whether this platform and the shipped <c>libsakura-audio</c> can play this way. Whether libpipewire is there, and
    /// PipeWire running, is only found out when a stream opens.
    /// </summary>
    public static bool IsAvailable => OperatingSystem.IsLinux() && SakuraAudioEngine.PipeWireCallback != 0;

    /// <summary>
    /// Opens a stream on the default output for <paramref name="engine"/>, inactive until <see cref="Start"/>, or returns
    /// null where PipeWire won't.
    /// </summary>
    /// <param name="engine">The engine it plays.</param>
    /// <param name="requestedFrames">The quantum to ask the graph for, in the engine's frames, or 0 for SDL's default.</param>
    /// <param name="appName">The app's name, which PipeWire's mixers show the stream as.</param>
    public static PipeWireOutput? TryOpen(SakuraAudioEngine engine, int requestedFrames, string appName)
    {
        var output = new PipeWireOutput(engine, requestedFrames > 0 ? requestedFrames : default_frames);

        try
        {
            if (output.open(appName))
                return output;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Logger.Verbose($"libpipewire isn't available ({e.GetType().Name}), so the audio plays through SDL.");
        }

        output.Dispose();
        return null;
    }

    private bool open(string appName)
    {
        pw_init(0, 0);
        string? version = Marshal.PtrToStringUTF8(pw_get_library_version());
        libraryVersion = version ?? "unknown";

        loop = pw_thread_loop_new("SakuraPipeWire", 0);

        if (loop == 0)
            return failed("make libpipewire's loop");

        context = pw_context_new(pw_thread_loop_get_loop(loop), 0, 0);

        if (context == 0)
            return failed("make libpipewire's context");

        if (pw_thread_loop_start(loop) != 0)
            return failed("start libpipewire's loop");

        pw_thread_loop_lock(loop);

        try
        {
            core = pw_context_connect(context, 0, 0);

            if (core == 0)
            {
                Logger.Verbose("PipeWire isn't running, so the audio plays through SDL.");
                return false;
            }

            // As SDL's stream asks, so the graph runs the same quantum for it.
            nint properties = pw_properties_new_string("media.type=Audio media.category=Playback media.role=Game node.always-process=true");
            pw_properties_set(properties, "node.latency", $"{framesPerCycle}/{SampleRate}");
            pw_properties_set(properties, "node.rate", $"1/{SampleRate}");
            pw_properties_set(properties, "node.name", appName);
            pw_properties_set(properties, "node.description", appName);
            pw_properties_set(properties, "application.name", appName);

            // The stream takes the properties.
            stream = pw_stream_new(core, appName, properties);

            if (stream == 0)
                return failed("make a PipeWire stream");

            var config = new SakuraAudioPipeWireConfig
            {
                Stream = stream,
                Dequeue = GetExport("pw_stream_dequeue_buffer"),
                Queue = GetExport("pw_stream_queue_buffer"),
                FramesPerCycle = framesPerCycle,

                // Before 0.3.49, pw_buffer has no requested field to read.
                HasRequested = IsAtLeast(version, 0, 3, 49) ? 1 : 0
            };

            native = SakuraAudioNative.sakura_audio_pipewire_create(engine.Handle, &config);

            if (native == 0)
                return failed("make the native PipeWire output");

            // After the output, which is the listener's data and needed the stream first.
            events = (StreamEvents*)NativeMemory.AllocZeroed((nuint)sizeof(StreamEvents));
            events->Version = STREAM_EVENTS_VERSION;
            events->Process = SakuraAudioEngine.PipeWireCallback;

            hook = (nint)NativeMemory.AllocZeroed((nuint)HOOK_SIZE);
            pw_stream_add_listener(stream, hook, events, native);

            byte[] pod = AudioFormat(SampleRate, engine.Channels);
            format = (byte*)NativeMemory.Alloc((nuint)pod.Length);
            pod.CopyTo(new Span<byte>(format, pod.Length));

            nint parameter = (nint)format;

            // Real-time: the process function is native and does nothing that waits.
            int connected = pw_stream_connect(stream, DIRECTION_OUTPUT, ID_ANY,
                STREAM_FLAG_AUTOCONNECT | STREAM_FLAG_INACTIVE | STREAM_FLAG_MAP_BUFFERS | STREAM_FLAG_RT_PROCESS, &parameter, 1);

            if (connected < 0)
                return failed($"connect the PipeWire stream ({connected})");

            return true;
        }
        finally
        {
            pw_thread_loop_unlock(loop);
        }
    }

    /// <summary>
    /// Starts the stream playing.
    /// </summary>
    public void Start()
    {
        if (stream == 0)
            return;

        pw_thread_loop_lock(loop);
        int result = pw_stream_set_active(stream, true);
        pw_thread_loop_unlock(loop);

        if (result < 0)
            Logger.Error($"Could not start the PipeWire stream ({result}).");
    }

    /// <summary>
    /// Whether the stream went into error, or lost its connection: it plays no more, and wants opening again.
    /// </summary>
    public bool Failed
    {
        get
        {
            if (stream == 0)
                return true;

            pw_thread_loop_lock(loop);
            int state = pw_stream_get_state(stream, null);
            pw_thread_loop_unlock(loop);

            return state is STREAM_STATE_ERROR or STREAM_STATE_UNCONNECTED;
        }
    }

    /// <summary>
    /// How long the frames mixed last take to be heard, from the stream's time: when the latest cycle's first frame is
    /// heard (the graph's delay, and what's queued and in the resampler, less the time since the snapshot), and that
    /// cycle's frames after it. Unavailable until the graph has run a cycle.
    /// </summary>
    public bool TrySampleLatency(out double milliseconds)
    {
        milliseconds = 0;

        if (stream == 0)
            return false;

        // What the engine writes a cycle, as its own count shows it: the graph's quantum, whatever node.latency asked.
        var stats = engine.GetStats();
        long callbacks = stats.Callbacks - sampledCallbacks;

        if (callbacks > 0 && sampledCallbacks > 0)
            framesPerCycleSeen = (stats.FramesMixed - sampledFramesMixed) / (double)callbacks;

        sampledCallbacks = stats.Callbacks;
        sampledFramesMixed = stats.FramesMixed;

        Time time;

        pw_thread_loop_lock(loop);
        int result = pw_stream_get_time_n(stream, &time, (nuint)sizeof(Time));
        pw_thread_loop_unlock(loop);

        if (result < 0 || time.Now == 0 || time.RateDenominator == 0)
            return false;

        // Both CLOCK_MONOTONIC: Stopwatch is on Linux, and so is the stream's time.
        long now = (long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency));

        milliseconds = LatencyMilliseconds(time, now, framesPerCycleSeen > 0 ? framesPerCycleSeen : framesPerCycle, SampleRate);
        return true;
    }

    /// <summary>
    /// When the last frame mixed is heard, from a time snapshot. libpipewire's documentation works out when the first
    /// frame of a buffer written in the snapshot's cycle is heard: the queued and buffered frames at the stream's rate,
    /// and the graph's delay in its own rate's units, less what's passed since the snapshot. The cycle's frames come
    /// after it. Without them a sink with no delay of its own reads as heard before now.
    /// </summary>
    internal static double LatencyMilliseconds(Time time, long nowNanoseconds, double framesPerCycle, int sampleRate)
    {
        double delaySeconds = time.Delay * (double)time.RateNumerator / time.RateDenominator;
        double elapsedSeconds = (nowNanoseconds - time.Now) / 1_000_000_000.0;

        return ((time.Queued + time.Buffered + framesPerCycle) / (double)sampleRate + delaySeconds - elapsedSeconds) * 1000.0;
    }

    /// <summary>
    /// What the stream is, for the log and the statistics.
    /// </summary>
    public string Describe() => $"PipeWire {libraryVersion} on the default output, {SampleRate} Hz, {engine.Channels} ch float, a {framesPerCycle}-frame quantum "
                                + $"({framesPerCycle * 1000.0 / SampleRate:F1} ms)";

    public void Dispose()
    {
        if (loop != 0)
        {
            pw_thread_loop_lock(loop);

            if (stream != 0)
            {
                // Once it's gone, the real-time thread can't call the process function again.
                pw_stream_disconnect(stream);
                pw_stream_destroy(stream);
                stream = 0;
            }

            pw_thread_loop_unlock(loop);
            pw_thread_loop_stop(loop);
        }

        if (native != 0)
        {
            SakuraAudioNative.sakura_audio_pipewire_destroy(native);
            native = 0;
        }

        // The loop has stopped, so nothing else is calling into these.
        if (core != 0)
        {
            pw_core_disconnect(core);
            core = 0;
        }

        if (context != 0)
        {
            pw_context_destroy(context);
            context = 0;
        }

        if (loop != 0)
        {
            pw_thread_loop_destroy(loop);
            loop = 0;
        }

        NativeMemory.Free(events);
        NativeMemory.Free((void*)hook);
        NativeMemory.Free(format);
        events = null;
        hook = 0;
        format = null;
    }

    private static bool failed(string what)
    {
        Logger.Error($"Could not {what}, so the audio plays through SDL.");
        return false;
    }
}
