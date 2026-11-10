// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;
using static Sakura.Framework.Audio.SdlEngine.AudioToolboxNative;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The native engine playing through one of Apple's output audio units with nothing in between, RemoteIO on iOS and the
/// HAL output on macOS: it mixes on the system's own I/O thread, straight into the buffer the hardware takes next.
/// </summary>
/// <remarks>
/// <para>
/// Through SDL, the mix went into an Audio Queue that SDL keeps several buffers deep ahead of the system's I/O buffer:
/// more than 30 ms at a phone's buffer size, and 32 ms at the desktop's 128 frames. Here the I/O buffer is all there is.
/// The unit's timestamps say when each render goes out (<see cref="TrySampleLatency"/>) and when one was missed
/// (<see cref="Discontinuities"/>), and the system's figures (<see cref="ISystemAudioRoute"/>) the route after it.
/// </para>
/// <para>
/// RemoteIO plays on whatever the route is, at the audio session's I/O buffer duration, which the host sets. It converts
/// to the route's rate and channels itself, so unlike AAudio's stream it never needs opening again. An interruption (a
/// call, an alarm, Siri) stops it, and <see cref="Restart"/> starts it again after one.
/// </para>
/// <para>
/// The HAL output plays on one device, the one it's given (<see cref="PlayOn"/>): the manager follows the device the
/// player picked, or the system's default. Each device is asked for an I/O buffer as long as the one asked for, which it
/// keeps for this process alone. The unit converts to the device's rate and channels itself as well.
/// </para>
/// <para>
/// One thread controls it at a time: the audio thread, or the host while that thread is parked for the background.
/// </para>
/// </remarks>
internal sealed unsafe class AudioUnitOutput : IDisposable
{
    // The most frames the unit renders at a time. Its default, 1156, is less than the 4096 iOS asks for with the screen
    // locked, when a render that large would fail instead.
    private const uint maximum_frames_per_slice = 4096;

    private readonly SakuraAudioEngine engine;
    private readonly uint subtype;

    // How long an I/O buffer to ask each device for, on macOS: 0 leaves the device's own.
    private readonly double bufferMilliseconds;

    private nint unit;
    private bool initialised;

    // Whether it's meant to be playing, which an interruption doesn't change: Restart takes it back to this.
    private bool started;

    /// <summary>
    /// The unit's name, for the log: RemoteIO or the HAL output.
    /// </summary>
    public string Kind => PlaysOnADevice ? "HAL output" : "RemoteIO";

    /// <summary>
    /// Whether it plays on a device it's given (the HAL output, on macOS), rather than wherever the system routes the
    /// sound (RemoteIO).
    /// </summary>
    public bool PlaysOnADevice => subtype == SUBTYPE_HAL_OUTPUT;

    /// <summary>
    /// The Core Audio device the HAL output plays on, or 0 for RemoteIO.
    /// </summary>
    public uint Device { get; private set; }

    /// <summary>
    /// The name SDL lists <see cref="Device"/> under, or null for RemoteIO and where the device can't say. Read from Core
    /// Audio once, as the unit moves onto the device: each read is a round trip to the audio server.
    /// </summary>
    public string? DeviceName { get; private set; }

    /// <summary>
    /// The I/O buffer <see cref="Device"/> gave, in the engine's frames, or 0 for RemoteIO and where the device won't say.
    /// </summary>
    public int BufferFrames
    {
        get
        {
            if (Device == 0
                || !CoreAudioNative.TryGet(Device, CoreAudioNative.PROPERTY_BUFFER_FRAME_SIZE, CoreAudioNative.SCOPE_GLOBAL, out uint frames)
                || !CoreAudioNative.TryGet(Device, CoreAudioNative.PROPERTY_NOMINAL_SAMPLE_RATE, CoreAudioNative.SCOPE_GLOBAL, out double deviceRate)
                || deviceRate <= 0)
                return 0;

            return (int)Math.Round(frames * (double)SampleRate / deviceRate);
        }
    }

    /// <summary>
    /// The rate the engine mixes at and the unit takes, converting where the hardware runs at another.
    /// </summary>
    public int SampleRate => engine.SampleRate;

    /// <summary>
    /// Render cycles whose timing didn't carry on from the one before, as the engine counts them from the unit's
    /// timestamps: each a cycle the output missed, or the unit stopping and starting again.
    /// </summary>
    public long Discontinuities => engine.TryGetDeviceTime(out var time) ? time.Discontinuities : 0;

    private AudioUnitOutput(SakuraAudioEngine engine, uint subtype, double bufferMilliseconds)
    {
        this.engine = engine;
        this.subtype = subtype;
        this.bufferMilliseconds = bufferMilliseconds;
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
    public static bool IsAvailable => (OperatingSystem.IsIOS() || OperatingSystem.IsMacOS()) && SakuraAudioEngine.AudioUnitCallback != 0;

    /// <summary>
    /// Makes the platform's output unit for <paramref name="engine"/>, not started yet, or returns null where the system
    /// won't.
    /// </summary>
    /// <param name="engine">The engine it plays.</param>
    /// <param name="device">On macOS, the Core Audio device to play on. Ignored on iOS.</param>
    /// <param name="bufferFrames">
    /// On macOS, how long an I/O buffer to ask each device for, in the engine's frames, or 0 to leave the device's own.
    /// Ignored on iOS, where the audio session sets it.
    /// </param>
    public static AudioUnitOutput? TryOpen(SakuraAudioEngine engine, uint device = 0, int bufferFrames = 0)
    {
        bool macOS = OperatingSystem.IsMacOS();
        var output = new AudioUnitOutput(engine, macOS ? SUBTYPE_HAL_OUTPUT : SUBTYPE_REMOTE_IO,
            macOS && bufferFrames > 0 ? bufferFrames * 1000.0 / engine.SampleRate : 0);

        try
        {
            if (output.open(device))
                return output;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Logger.Verbose($"The {output.Kind} unit isn't available ({e.GetType().Name}), so the audio plays through SDL.");
        }

        output.Dispose();
        return null;
    }

    private bool open(uint device)
    {
        var description = new AudioComponentDescription
        {
            ComponentType = TYPE_OUTPUT,
            ComponentSubType = subtype,
            ComponentManufacturer = MANUFACTURER_APPLE
        };

        nint component = AudioComponentFindNext(0, &description);

        if (component == 0)
        {
            Logger.Error($"Could not find the {Kind} audio unit.");
            return false;
        }

        if (!succeeded(AudioComponentInstanceNew(component, out unit), $"make the {Kind} unit"))
        {
            unit = 0;
            return false;
        }

        // Before its format: the unit's other side takes the device's.
        if (PlaysOnADevice && !useDevice(device))
            return false;

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

        if (!succeeded(AudioUnitSetProperty(unit, PROPERTY_STREAM_FORMAT, SCOPE_INPUT, OUTPUT_ELEMENT, &format, (uint)sizeof(AudioStreamBasicDescription)), $"set the {Kind} unit's format"))
            return false;

        uint maximumFrames = maximum_frames_per_slice;

        if (!succeeded(AudioUnitSetProperty(unit, PROPERTY_MAXIMUM_FRAMES_PER_SLICE, SCOPE_GLOBAL, 0, &maximumFrames, sizeof(uint)), $"set the {Kind} unit's largest render"))
            return false;

        // Native, straight into the engine: no managed code ever runs on the I/O thread.
        var callback = new AURenderCallbackStruct
        {
            InputProc = SakuraAudioEngine.AudioUnitCallback,
            InputProcRefCon = engine.Handle
        };

        if (!succeeded(AudioUnitSetProperty(unit, PROPERTY_SET_RENDER_CALLBACK, SCOPE_INPUT, OUTPUT_ELEMENT, &callback, (uint)sizeof(AURenderCallbackStruct)), $"set the {Kind} unit's render callback"))
            return false;

        if (!succeeded(AudioUnitInitialize(unit), $"start up the {Kind} unit"))
            return false;

        initialised = true;
        return true;
    }

    /// <summary>
    /// Moves the HAL output onto <paramref name="device"/>: stopped, given the device and its I/O buffer, and started
    /// again if it was playing. It stays on the device it was on where the new one won't take it.
    /// </summary>
    /// <returns>Whether it plays on <paramref name="device"/> now.</returns>
    public bool PlayOn(uint device)
    {
        if (unit == 0 || !PlaysOnADevice)
            return false;

        if (device == Device)
            return true;

        // Stopped first, so no render is still mixing the engine while the unit changes.
        AudioOutputUnitStop(unit);

        if (initialised)
        {
            AudioUnitUninitialize(unit);
            initialised = false;
        }

        bool moved = useDevice(device);

        if (!succeeded(AudioUnitInitialize(unit), $"start up the {Kind} unit again"))
            return false;

        initialised = true;

        if (started)
            succeeded(AudioOutputUnitStart(unit), $"start the {Kind} unit again");

        return moved;
    }

    /// <summary>
    /// Points the HAL output at <paramref name="device"/> and asks the device for the I/O buffer.
    /// </summary>
    private bool useDevice(uint device)
    {
        if (device == 0)
        {
            Logger.Error("There's no audio output device to play on.");
            return false;
        }

        if (!succeeded(AudioUnitSetProperty(unit, PROPERTY_CURRENT_DEVICE, SCOPE_GLOBAL, 0, &device, sizeof(uint)), $"play on audio output device {device}"))
            return false;

        Device = device;
        DeviceName = CoreAudioNative.GetDeviceName(device);
        askForBuffer(device);
        return true;
    }

    /// <summary>
    /// Asks <paramref name="device"/> for an I/O buffer as long as the one asked for, in its own frames, within what it
    /// allows. It's this process's alone: other apps keep theirs.
    /// </summary>
    private void askForBuffer(uint device)
    {
        if (bufferMilliseconds <= 0
            || !CoreAudioNative.TryGet(device, CoreAudioNative.PROPERTY_NOMINAL_SAMPLE_RATE, CoreAudioNative.SCOPE_GLOBAL, out double deviceRate) || deviceRate <= 0)
            return;

        double frames = Math.Max(1, Math.Round(bufferMilliseconds * deviceRate / 1000.0));

        if (CoreAudioNative.TryGet(device, CoreAudioNative.PROPERTY_BUFFER_FRAME_SIZE_RANGE, CoreAudioNative.SCOPE_GLOBAL, out CoreAudioNative.AudioValueRange range)
            && range.Maximum >= range.Minimum)
            frames = Math.Clamp(frames, range.Minimum, range.Maximum);

        int status = CoreAudioNative.Set(device, CoreAudioNative.PROPERTY_BUFFER_FRAME_SIZE, CoreAudioNative.SCOPE_GLOBAL, (uint)frames);

        if (status != CoreAudioNative.NO_ERR)
            Logger.Verbose($"Audio output device {device} wouldn't take an I/O buffer of {frames} frames ({DescribeStatus(status)}), so it keeps its own.");
    }

    /// <summary>
    /// Starts the unit playing.
    /// </summary>
    public bool Start()
    {
        started = true;
        return unit != 0 && succeeded(AudioOutputUnitStart(unit), $"start the {Kind} unit");
    }

    /// <summary>
    /// Stops the unit. Once this returns, the engine isn't being mixed.
    /// </summary>
    public void Pause()
    {
        started = false;

        if (unit != 0)
            succeeded(AudioOutputUnitStop(unit), $"stop the {Kind} unit");
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
    public string Describe()
    {
        if (unit == 0)
            return $"{Kind} (closed)";

        return PlaysOnADevice
            ? $"the HAL output on {DeviceName ?? $"device {Device}"}, {SampleRate} Hz, {engine.Channels} ch"
            : $"RemoteIO, {SampleRate} Hz, {engine.Channels} ch";
    }

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
