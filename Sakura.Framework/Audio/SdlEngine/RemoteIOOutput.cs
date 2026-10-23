// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;
using static Sakura.Framework.Audio.SdlEngine.AudioToolboxNative;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The native engine playing through iOS's RemoteIO audio unit with nothing in between: it mixes on the system's own
/// I/O thread, straight into the buffer the hardware takes next.
/// </summary>
/// <remarks>
/// <para>
/// Through SDL, the mix went into an Audio Queue that SDL keeps six buffers deep at a phone's buffer size (more than
/// 30 ms) ahead of the system's I/O buffer. Here the I/O buffer is all there is, a few milliseconds at the audio session's
/// preferred duration, which the host sets. The unit's timestamps say when each render goes out
/// (<see cref="TrySampleLatency"/>) and when one was missed (<see cref="Discontinuities"/>), and the session's figures
/// (<see cref="ISystemAudioRoute"/>) the route after it.
/// </para>
/// <para>
/// The unit plays on whatever the route is, converting to its rate and channels itself, so unlike AAudio's stream it
/// never needs opening again. An interruption (a call, an alarm, Siri) stops it, and <see cref="Restart"/> starts it
/// again after one.
/// </para>
/// <para>
/// One thread controls it at a time: the audio thread, or the host while that thread is parked for the background.
/// </para>
/// </remarks>
internal sealed unsafe class RemoteIOOutput : IDisposable
{
    // The most frames the unit renders at a time. Its default, 1156, is less than the 4096 iOS asks for with the screen
    // locked, when a render that large would fail instead.
    private const uint maximum_frames_per_slice = 4096;

    private readonly SakuraAudioEngine engine;

    private nint unit;
    private bool initialised;

    // Whether it's meant to be playing, which an interruption doesn't change: Restart takes it back to this.
    private bool started;

    /// <summary>
    /// The rate the engine mixes at and the unit takes, converting where the hardware runs at another.
    /// </summary>
    public int SampleRate => engine.SampleRate;

    /// <summary>
    /// Render cycles whose timing didn't carry on from the one before, as the engine counts them from the unit's
    /// timestamps: each a cycle the output missed, or the unit stopping and starting again.
    /// </summary>
    public long Discontinuities => engine.TryGetDeviceTime(out var time) ? time.Discontinuities : 0;

    private RemoteIOOutput(SakuraAudioEngine engine)
    {
        this.engine = engine;
    }

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern ulong mach_absolute_time();

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int mach_timebase_info(out MachTimebaseInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MachTimebaseInfo
    {
        public uint Numer;
        public uint Denom;
    }

    // Host ticks to nanoseconds: 125/3 on Apple's own chips (24 MHz ticks), 1 on Intel Macs.
    private static readonly double nanoseconds_per_tick = mach_timebase_info(out var info) == 0 && info.Denom != 0 ? (double)info.Numer / info.Denom : 1;

    /// <summary>
    /// How long the frames mixed last take to go out of the device, from when the unit said its latest render goes out:
    /// the I/O buffer's share of the delay, measured. The route after the device (<see cref="ISystemAudioRoute"/>, less its
    /// buffer) comes on top. Unavailable until the unit has rendered, and where the library predates its timing.
    /// </summary>
    public bool TrySampleLatency(out double milliseconds)
    {
        milliseconds = 0;

        // The frames mixed are read first: a render landing before the timestamp is then counted in neither.
        long framesMixed = engine.GetStats().FramesMixed;

        if (!engine.TryGetDeviceTime(out var time))
            return false;

        long now = (long)(mach_absolute_time() * nanoseconds_per_tick);
        long goesOut = (long)(time.HostTime * nanoseconds_per_tick);

        milliseconds = AAudioOutput.LatencyMilliseconds(time.FramesMixed, goesOut, framesMixed, now, SampleRate);
        return true;
    }

    /// <summary>
    /// Whether this platform and the shipped <c>libsakura-audio</c> can play this way.
    /// </summary>
    public static bool IsAvailable => OperatingSystem.IsIOS() && SakuraAudioEngine.AudioUnitCallback != 0;

    /// <summary>
    /// Makes a RemoteIO unit for <paramref name="engine"/>, not started yet, or returns null where iOS won't.
    /// </summary>
    public static RemoteIOOutput? TryOpen(SakuraAudioEngine engine)
    {
        var output = new RemoteIOOutput(engine);

        try
        {
            if (output.open())
                return output;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Logger.Verbose($"RemoteIO isn't available ({e.GetType().Name}), so the audio plays through SDL.");
        }

        output.Dispose();
        return null;
    }

    private bool open()
    {
        var description = new AudioComponentDescription
        {
            ComponentType = TYPE_OUTPUT,
            ComponentSubType = SUBTYPE_REMOTE_IO,
            ComponentManufacturer = MANUFACTURER_APPLE
        };

        nint component = AudioComponentFindNext(0, &description);

        if (component == 0)
        {
            Logger.Error("Could not find the RemoteIO audio unit.");
            return false;
        }

        if (!succeeded(AudioComponentInstanceNew(component, out unit), "make the RemoteIO unit"))
        {
            unit = 0;
            return false;
        }

        // Interleaved floats at the engine's rate and width, which is what it mixes: one buffer the callback fills whole.
        uint frameBytes = (uint)(engine.Channels * sizeof(float));

        var format = new AudioStreamBasicDescription
        {
            SampleRate = engine.SampleRate,
            FormatID = FORMAT_LINEAR_PCM,
            FormatFlags = FORMAT_FLAG_IS_FLOAT | FORMAT_FLAG_IS_PACKED,
            BytesPerPacket = frameBytes,
            FramesPerPacket = 1,
            BytesPerFrame = frameBytes,
            ChannelsPerFrame = (uint)engine.Channels,
            BitsPerChannel = 32
        };

        if (!succeeded(AudioUnitSetProperty(unit, PROPERTY_STREAM_FORMAT, SCOPE_INPUT, OUTPUT_ELEMENT, &format, (uint)sizeof(AudioStreamBasicDescription)), "set the RemoteIO unit's format"))
            return false;

        uint maximumFrames = maximum_frames_per_slice;

        if (!succeeded(AudioUnitSetProperty(unit, PROPERTY_MAXIMUM_FRAMES_PER_SLICE, SCOPE_GLOBAL, 0, &maximumFrames, sizeof(uint)), "set the RemoteIO unit's largest render"))
            return false;

        // Native, straight into the engine: no managed code ever runs on the I/O thread.
        var callback = new AURenderCallbackStruct
        {
            InputProc = SakuraAudioEngine.AudioUnitCallback,
            InputProcRefCon = engine.Handle
        };

        if (!succeeded(AudioUnitSetProperty(unit, PROPERTY_SET_RENDER_CALLBACK, SCOPE_INPUT, OUTPUT_ELEMENT, &callback, (uint)sizeof(AURenderCallbackStruct)), "set the RemoteIO unit's render callback"))
            return false;

        if (!succeeded(AudioUnitInitialize(unit), "start up the RemoteIO unit"))
            return false;

        initialised = true;
        return true;
    }

    /// <summary>
    /// Starts the unit playing.
    /// </summary>
    public bool Start()
    {
        started = true;
        return unit != 0 && succeeded(AudioOutputUnitStart(unit), "start the RemoteIO unit");
    }

    /// <summary>
    /// Stops the unit. Once this returns, the engine isn't being mixed.
    /// </summary>
    public void Pause()
    {
        started = false;

        if (unit != 0)
            succeeded(AudioOutputUnitStop(unit), "stop the RemoteIO unit");
    }

    /// <summary>
    /// Starts the unit again after an interruption stopped it, if it was playing before.
    /// </summary>
    public void Restart()
    {
        if (started)
            Start();
    }

    /// <summary>
    /// What the output is, for the log and the statistics.
    /// </summary>
    public string Describe() => unit == 0 ? "RemoteIO (closed)" : $"RemoteIO, {SampleRate} Hz, {engine.Channels} ch";

    public void Dispose()
    {
        if (unit == 0)
            return;

        // Stopped first, so no render is still mixing the engine once this returns.
        AudioOutputUnitStop(unit);

        if (initialised)
            AudioUnitUninitialize(unit);

        AudioComponentInstanceDispose(unit);
        unit = 0;
        initialised = false;
    }

    private static bool succeeded(int status, string what)
    {
        if (status == NO_ERR)
            return true;

        Logger.Error($"Could not {what}: {DescribeStatus(status)}");
        return false;
    }
}
