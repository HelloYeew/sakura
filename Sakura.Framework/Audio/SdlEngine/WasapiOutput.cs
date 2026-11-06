// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Threading;
using Sakura.Framework.Logging;
using Sakura.Framework.Threading;
using static Sakura.Framework.Audio.SdlEngine.WasapiNative;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The native engine playing through WASAPI on Windows with nothing of SDL's in between: it mixes on a render thread of
/// the output's own, straight into the endpoint's buffer.
/// </summary>
/// <remarks>
/// <para>
/// Shared mode by default, through <c>IAudioClient3</c> at the period asked for, rounded to what the engine allows. That
/// shortens the shared engine's own period on the endpoint while it plays. Where the endpoint mixes at another rate than
/// the engine, or the system has no <c>IAudioClient3</c>, it's a shared stream the system converts for, at its default
/// period. Exclusive mode, where it's asked for and the device takes a format, plays straight to the device at its
/// shortest period: it skips the system's mixer and effects, and takes the device from every other app while it plays,
/// voice chat included.
/// </para>
/// <para>
/// Every COM object is made, used and released on the render thread, which joins COM's multithreaded apartment, so none
/// crosses apartments. The thread registers with MMCSS as a "Pro Audio" task, then stays in the native loop until it's
/// stopped: no managed code runs on the audio path, and a garbage collection doesn't stop it.
/// </para>
/// <para>
/// The loop publishes when each cycle's first frame is heard, from <c>IAudioClock</c>'s position at its QPC time, which
/// <see cref="TrySampleLatency"/> reads as AAudio's timestamps are read. Where the device goes away or stops asking for
/// audio, the loop returns and <see cref="Stopped"/> says so, for the manager to open the stream again.
/// </para>
/// </remarks>
internal sealed class WasapiOutput : IDisposable
{
    private readonly SakuraAudioEngine engine;
    private readonly bool exclusive;
    private readonly int requestedFrames;

    private RenderStream? stream;
    private bool started;

    /// <summary>
    /// The rate the engine mixes at, which the stream runs at too.
    /// </summary>
    public int SampleRate => engine.SampleRate;

    /// <summary>
    /// Whether the stream plays in exclusive mode, which it does only where it was asked to and the device took it.
    /// </summary>
    public bool IsExclusive => stream?.Exclusive ?? false;

    /// <summary>
    /// The endpoint buffer the engine keeps filled, in frames: the app's own part of the delay.
    /// </summary>
    public int BufferFrames => stream?.BufferFrames ?? 0;

    /// <summary>
    /// The endpoint the stream plays on, by its ID, or null while there's no stream.
    /// </summary>
    public string? EndpointId => stream?.EndpointId;

    /// <summary>
    /// The endpoint's name, as SDL lists it, or null while there's no stream.
    /// </summary>
    public string? DeviceName => stream?.DeviceName;

    /// <summary>
    /// Whether there's no stream playing: it never opened, or its device went away or stopped asking for audio.
    /// </summary>
    public bool Stopped => stream == null || stream.Finished;

    private WasapiOutput(SakuraAudioEngine engine, bool exclusive, int requestedFrames)
    {
        this.engine = engine;
        this.exclusive = exclusive;
        this.requestedFrames = requestedFrames;
    }

    /// <summary>
    /// Whether this platform and the shipped <c>libsakura-audio</c> can play this way.
    /// </summary>
    public static bool IsAvailable => OperatingSystem.IsWindows() && SakuraAudioEngine.HasWasapiOutput;

    /// <summary>
    /// Opens a stream for <paramref name="engine"/>, not started yet, or returns null where WASAPI won't.
    /// </summary>
    /// <param name="engine">The engine it plays.</param>
    /// <param name="deviceName">The endpoint to play on, by SDL's name for it, or empty for the system's default.</param>
    /// <param name="exclusive">Whether to ask for exclusive mode.</param>
    /// <param name="requestedFrames">The period to ask the shared engine for, in the engine's frames, or 0 for its shortest.</param>
    public static WasapiOutput? TryOpen(SakuraAudioEngine engine, string deviceName, bool exclusive, int requestedFrames)
    {
        var output = new WasapiOutput(engine, exclusive, requestedFrames);

        if (output.Reopen(deviceName))
            return output;

        output.Dispose();
        return null;
    }

    /// <summary>
    /// The endpoint a stream would play on now for <paramref name="deviceName"/>: the one of that name while it's
    /// plugged in, else the system's default.
    /// </summary>
    /// <returns>Its ID, or null where there's no output at all.</returns>
    public static string? FindEndpoint(string deviceName)
    {
        nint enumerator = CreateEnumerator();

        if (enumerator == 0)
            return null;

        nint device = findDevice(enumerator, deviceName, out _);

        try
        {
            return device != 0 ? GetId(device) : null;
        }
        finally
        {
            Release(device);
            Release(enumerator);
        }
    }

    /// <summary>
    /// Closes the stream and opens one on the endpoint for <paramref name="deviceName"/>, playing if this was started.
    /// </summary>
    /// <returns>Whether the new stream opened. Without it, nothing plays until the next try.</returns>
    public bool Reopen(string deviceName)
    {
        stream?.Dispose();
        stream = null;

        var next = new RenderStream(engine, deviceName, exclusive, requestedFrames);

        if (!next.Open())
        {
            next.Dispose();
            return false;
        }

        stream = next;

        if (started)
            stream.Start();

        return true;
    }

    /// <summary>
    /// Starts the stream playing.
    /// </summary>
    public void Start()
    {
        started = true;
        stream?.Start();
    }

    /// <summary>
    /// How long the frames mixed last take to be heard, from when the loop said its latest cycle goes out: everything up
    /// to the device's position, the endpoint buffer included. Unavailable until the stream has run a cycle, and where
    /// the library predates its timing.
    /// </summary>
    public bool TrySampleLatency(out double milliseconds)
    {
        milliseconds = 0;

        if (Stopped)
            return false;

        // The frames mixed are read first: a cycle landing before the device time is then counted in neither.
        long framesMixed = engine.GetStats().FramesMixed;

        if (!engine.TryGetDeviceTime(out var time))
            return false;

        // Both QPC: Stopwatch is QPC on Windows, and the device time is QPC's in 100 ns units.
        long now = (long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency));
        long heardAt = (long)time.HostTime * 100;

        milliseconds = AAudioOutput.LatencyMilliseconds(time.FramesMixed, heardAt, framesMixed, now, SampleRate);
        return true;
    }

    /// <summary>
    /// What the stream turned out to be, for the log and the statistics.
    /// </summary>
    public string Describe() => stream == null || stream.Finished ? "WASAPI (no stream)" : stream.Describe();

    public void Dispose()
    {
        stream?.Dispose();
        stream = null;
    }

    /// <summary>
    /// The period a low-latency shared stream asks for: <paramref name="requestedFrames"/> rounded to a multiple of the
    /// engine's fundamental period, within its minimum and maximum, as <c>InitializeSharedAudioStream</c> takes. The
    /// minimum where nothing's asked for.
    /// </summary>
    internal static uint LowLatencyPeriod(int requestedFrames, uint fundamentalPeriod, uint minimumPeriod, uint maximumPeriod)
    {
        if (requestedFrames <= 0 || fundamentalPeriod == 0)
            return minimumPeriod;

        uint period = fundamentalPeriod * (uint)Math.Max(1, Math.Round(requestedFrames / (double)fundamentalPeriod));
        return Math.Clamp(period, minimumPeriod, Math.Max(minimumPeriod, maximumPeriod));
    }

    /// <summary>
    /// The native render loop's sample format for a WASAPI stream format.
    /// </summary>
    /// <returns>One of the <c>SAMPLE_*</c> constants in <see cref="SakuraAudioNative"/>, or -1 for one it doesn't write.</returns>
    internal static int SampleFormatOf(WaveFormatExtensible format)
    {
        ushort tag = format.Format.FormatTag;
        bool isFloat = tag == WAVE_FORMAT_IEEE_FLOAT || (tag == WAVE_FORMAT_EXTENSIBLE && format.SubFormat == SUBTYPE_IEEE_FLOAT);
        bool isPcm = tag == WAVE_FORMAT_PCM || (tag == WAVE_FORMAT_EXTENSIBLE && format.SubFormat == SUBTYPE_PCM);

        if (isFloat)
            return format.Format.BitsPerSample == 32 ? SakuraAudioNative.SAMPLE_FLOAT32 : -1;

        if (!isPcm)
            return -1;

        // 24 valid bits in a 32-bit container are aligned to the top, which is the 32-bit write.
        return format.Format.BitsPerSample switch
        {
            16 => SakuraAudioNative.SAMPLE_INT16,
            24 => SakuraAudioNative.SAMPLE_INT24,
            32 => SakuraAudioNative.SAMPLE_INT32,
            _ => -1
        };
    }

    /// <summary>
    /// An extensible PCM or float format, for a stream the engine writes at <paramref name="sampleRate"/>.
    /// </summary>
    internal static WaveFormatExtensible MakeFormat(int sampleRate, int channels, uint channelMask, int sampleFormat)
    {
        (ushort bits, ushort validBits, Guid subFormat) = sampleFormat switch
        {
            SakuraAudioNative.SAMPLE_FLOAT32 => ((ushort)32, (ushort)32, SUBTYPE_IEEE_FLOAT),
            SakuraAudioNative.SAMPLE_INT16 => ((ushort)16, (ushort)16, SUBTYPE_PCM),
            SakuraAudioNative.SAMPLE_INT24 => ((ushort)24, (ushort)24, SUBTYPE_PCM),
            _ => ((ushort)32, (ushort)24, SUBTYPE_PCM)
        };

        ushort blockAlign = (ushort)(channels * bits / 8);

        return new WaveFormatExtensible
        {
            Format = new WaveFormatEx
            {
                FormatTag = WAVE_FORMAT_EXTENSIBLE,
                Channels = (ushort)channels,
                SamplesPerSecond = (uint)sampleRate,
                AverageBytesPerSecond = (uint)(sampleRate * blockAlign),
                BlockAlign = blockAlign,
                BitsPerSample = bits,
                ExtraSize = EXTENSIBLE_EXTRA_SIZE
            },
            ValidBitsPerSample = validBits,
            ChannelMask = channelMask,
            SubFormat = subFormat
        };
    }

    /// <summary>
    /// The endpoint named, else the system's default.
    /// </summary>
    private static nint findDevice(nint enumerator, string deviceName, out bool onDefault)
    {
        nint device = deviceName.Length > 0 ? FindOutput(enumerator, deviceName) : 0;
        onDefault = device == 0;
        return onDefault ? GetDefaultOutput(enumerator) : device;
    }

    /// <summary>
    /// One stream on one endpoint, and the render thread that opens, plays and closes it.
    /// </summary>
    private sealed unsafe class RenderStream : IDisposable
    {
        private const int open_timeout_ms = 5000;
        private const int close_timeout_ms = 3000;

        // The 100 ns units WASAPI's durations are in.
        private const double units_per_second = 10_000_000.0;

        // The exclusive formats tried, best first. Most devices take only integers there.
        private static readonly int[] exclusive_formats = new[]
        {
            SakuraAudioNative.SAMPLE_FLOAT32,
            SakuraAudioNative.SAMPLE_INT32,
            SakuraAudioNative.SAMPLE_INT24,
            SakuraAudioNative.SAMPLE_INT16
        };

        private readonly SakuraAudioEngine engine;
        private readonly string deviceName;
        private readonly bool exclusiveWanted;
        private readonly int requestedFrames;

        private readonly ManualResetEventSlim opened = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim startRequested = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim closeRequested = new ManualResetEventSlim(false);

        // Taken around the native output's making and the decision to run it, so a stop asked for from another thread
        // is never missed: either the stop sees the output, or the render thread sees the stop.
        private readonly Lock gate = new Lock();
        private bool closing;
        private nint native;

        private Thread? thread;
        private nint eventHandle;
        private nint client;
        private nint renderClient;
        private nint clock;

        private volatile bool finished;

        // Read once the stream has opened, which the opened event orders after the render thread wrote them.
        public bool Exclusive { get; private set; }
        public int BufferFrames { get; private set; }
        public string? EndpointId { get; private set; }
        public string? DeviceName { get; private set; }
        private string mode = string.Empty;
        private string formatName = string.Empty;

        /// <summary>
        /// Whether the loop has returned (or never ran): the stream plays no more.
        /// </summary>
        public bool Finished => finished;

        public RenderStream(SakuraAudioEngine engine, string deviceName, bool exclusiveWanted, int requestedFrames)
        {
            this.engine = engine;
            this.deviceName = deviceName;
            this.exclusiveWanted = exclusiveWanted;
            this.requestedFrames = requestedFrames;
        }

        /// <summary>
        /// Starts the render thread and waits for it to open the stream.
        /// </summary>
        /// <returns>Whether it opened.</returns>
        public bool Open()
        {
            thread = new Thread(run)
            {
                Name = "WasapiRender",
                IsBackground = true,
                Priority = ThreadPriority.Highest
            };

            thread.Start();

            if (!opened.Wait(open_timeout_ms))
            {
                Logger.Error($"WASAPI didn't open a stream within {open_timeout_ms / 1000} seconds, so the audio plays through SDL.");
                return false;
            }

            return native != 0;
        }

        /// <summary>
        /// Lets the render thread start the stream.
        /// </summary>
        public void Start() => startRequested.Set();

        public string Describe() =>
            $"WASAPI {mode} on {DeviceName ?? "the default output"}, {engine.SampleRate} Hz, {formatName}, a buffer of {BufferFrames} frames "
            + $"({BufferFrames * 1000.0 / engine.SampleRate:F1} ms)";

        private void run()
        {
            using var mmcss = OperatingSystem.IsWindows() ? WindowsMultimediaScheduler.Register("WasapiRender", "Pro Audio") : null;

            try
            {
                try
                {
                    open();
                }
                catch (EntryPointNotFoundException)
                {
                    Logger.Verbose("This libsakura-audio predates its WASAPI output, so the audio plays through SDL.");
                }
                catch (Exception e)
                {
                    Logger.Error("Could not open a WASAPI stream, so the audio plays through SDL.", e);
                }
                finally
                {
                    opened.Set();
                }

                if (native == 0)
                    return;

                WaitHandle.WaitAny(new[] { startRequested.WaitHandle, closeRequested.WaitHandle });

                bool play;

                lock (gate)
                    play = !closing;

                int result = play ? SakuraAudioNative.sakura_audio_wasapi_run(native) : SakuraAudioNative.OK;
                finished = true;

                if (result != SakuraAudioNative.OK)
                    Logger.Verbose($"The WASAPI stream on {DeviceName} stopped ({describe(result)}), so it's opened again.");

                closeRequested.Wait();
            }
            finally
            {
                finished = true;
                release();
            }
        }

        /// <summary>
        /// Opens the stream, on the render thread: the endpoint, a client in the best mode it takes, its services and
        /// the event, then the native loop over them.
        /// </summary>
        private void open()
        {
            nint enumerator = CreateEnumerator();

            if (enumerator == 0)
            {
                Logger.Error("Could not list Windows' audio endpoints, so the audio plays through SDL.");
                return;
            }

            nint device = findDevice(enumerator, deviceName, out bool onDefault);
            Release(enumerator);

            if (device == 0)
            {
                Logger.Verbose("Windows has no audio output, so the audio plays through SDL.");
                return;
            }

            try
            {
                EndpointId = GetId(device);
                DeviceName = GetName(device);

                if (!onDefault || deviceName.Length == 0)
                    Logger.Verbose($"Opening a WASAPI stream on {DeviceName}");
                else
                    Logger.Verbose($"Opening a WASAPI stream on {DeviceName}, the default, until {deviceName} is plugged in");

                if (!initializeClient(device, out WaveFormatExtensible format))
                    return;

                int sampleFormat = SampleFormatOf(format);

                if (!succeeded(GetBufferSize(client, out uint bufferFrames), "read the WASAPI buffer's size"))
                    return;

                eventHandle = CreateEventW(null, 0, 0, null);

                if (eventHandle == 0 || !succeeded(SetEventHandle(client, eventHandle), "give the WASAPI stream its event"))
                    return;

                renderClient = GetRenderClient(client);
                clock = GetClock(client);

                if (renderClient == 0)
                {
                    Logger.Error("The WASAPI stream has no render client, so the audio plays through SDL.");
                    return;
                }

                ulong frequency = 0;

                if (clock != 0 && GetClockFrequency(clock, out frequency) < 0)
                    frequency = 0;

                var config = new SakuraAudioWasapiConfig
                {
                    ClockFrequency = frequency,
                    AudioClient = client,
                    RenderClient = renderClient,

                    // No device time without the clock's frequency to read its position by.
                    AudioClock = frequency > 0 ? clock : 0,
                    Event = eventHandle,
                    Wait = WaitForSingleObject,
                    BufferFrames = (int)bufferFrames,
                    Channels = format.Format.Channels,
                    SampleFormat = sampleFormat,
                    Exclusive = Exclusive ? 1 : 0
                };

                BufferFrames = (int)bufferFrames;

                nint output = SakuraAudioNative.sakura_audio_wasapi_create(engine.Handle, &config);

                if (output == 0)
                {
                    Logger.Error($"libsakura-audio can't play a WASAPI stream of {formatName}, so the audio plays through SDL.");
                    return;
                }

                lock (gate)
                    native = output;
            }
            finally
            {
                Release(device);
            }
        }

        /// <summary>
        /// Makes and initializes the client: exclusive where it was asked for and the device takes a format, then a
        /// low-latency shared stream, then a shared one the system converts for.
        /// </summary>
        private bool initializeClient(nint device, out WaveFormatExtensible format)
        {
            format = default;

            client = ActivateClient(device, out bool client3);

            if (client == 0 || !succeeded(GetMixFormat(client, out var mix), "read the endpoint's mix format"))
                return false;

            int channels = mix.Format.Channels;
            uint channelMask = mix.ChannelMask;

            if (exclusiveWanted)
            {
                if (initializeExclusive(device, channels, channelMask, ref client3, out format))
                    return true;

                Logger.Verbose($"{DeviceName} wouldn't play in exclusive mode, so it plays shared.");
            }

            // Low latency needs the engine's own format: no conversion on the way.
            if (client3 && mix.Format.SamplesPerSecond == engine.SampleRate && SampleFormatOf(mix) >= 0)
            {
                if (GetSharedModeEnginePeriod(client, &mix, out _, out uint fundamental, out uint minimum, out uint maximum) >= 0)
                {
                    uint period = LowLatencyPeriod(requestedFrames, fundamental, minimum, maximum);

                    if (InitializeSharedAudioStream(client, STREAM_FLAG_EVENT_CALLBACK, period, &mix) >= 0)
                    {
                        format = mix;
                        mode = $"shared, at a {period}-frame period ({period * 1000.0 / engine.SampleRate:F1} ms)";
                        formatName = describeFormat(format);
                        return true;
                    }
                }

                // A failed initialize leaves the client unusable.
                reactivate(device, out client3);
            }

            // Floats at the engine's rate, which the system converts to the mix format.
            format = MakeFormat(engine.SampleRate, channels, channelMask, SakuraAudioNative.SAMPLE_FLOAT32);

            fixed (WaveFormatExtensible* shared = &format)
            {
                if (!succeeded(Initialize(client, SHARE_MODE_SHARED, STREAM_FLAG_EVENT_CALLBACK | STREAM_FLAG_AUTO_CONVERT_PCM | STREAM_FLAG_SRC_DEFAULT_QUALITY, 0, 0, shared),
                        "open a shared WASAPI stream"))
                    return false;
            }

            mode = mix.Format.SamplesPerSecond == engine.SampleRate ? "shared" : $"shared, converted to the endpoint's {mix.Format.SamplesPerSecond} Hz";
            formatName = describeFormat(format);
            return true;
        }

        /// <summary>
        /// An exclusive stream at the device's shortest period, in the first format it takes at the engine's rate.
        /// </summary>
        private bool initializeExclusive(nint device, int channels, uint channelMask, ref bool client3, out WaveFormatExtensible format)
        {
            format = default;

            if (GetDevicePeriod(client, out _, out long minimumPeriod) < 0 || minimumPeriod <= 0)
                return false;

            foreach (int sampleFormat in exclusive_formats)
            {
                var candidate = MakeFormat(engine.SampleRate, channels, channelMask, sampleFormat);

                if (IsFormatSupported(client, SHARE_MODE_EXCLUSIVE, &candidate) != 0)
                    continue;

                int result = Initialize(client, SHARE_MODE_EXCLUSIVE, STREAM_FLAG_EVENT_CALLBACK, minimumPeriod, minimumPeriod, &candidate);

                // The buffer has to line up with what the device moves at a time: asked for again at the size it gave.
                if (result == AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED && GetBufferSize(client, out uint alignedFrames) >= 0)
                {
                    long aligned = (long)Math.Round(units_per_second * alignedFrames / engine.SampleRate);

                    reactivate(device, out client3);
                    result = client == 0 ? result : Initialize(client, SHARE_MODE_EXCLUSIVE, STREAM_FLAG_EVENT_CALLBACK, aligned, aligned, &candidate);
                }

                if (result >= 0)
                {
                    format = candidate;
                    Exclusive = true;
                    mode = $"exclusive, at a {minimumPeriod / 10_000.0:F1} ms period";
                    formatName = describeFormat(format);
                    return true;
                }

                Logger.Verbose($"{DeviceName} wouldn't open exclusive in {describeFormat(candidate)} ({describe(result)}).");
                reactivate(device, out client3);

                if (client == 0)
                    return false;
            }

            return false;
        }

        /// <summary>
        /// Replaces a client that failed to initialize, which can't be tried again.
        /// </summary>
        private void reactivate(nint device, out bool client3)
        {
            Release(client);
            client = ActivateClient(device, out client3);
        }

        private void release()
        {
            lock (gate)
            {
                if (native != 0)
                {
                    SakuraAudioNative.sakura_audio_wasapi_destroy(native);
                    native = 0;
                }
            }

            if (client != 0)
                Reset(client);

            Release(clock);
            Release(renderClient);
            Release(client);
            clock = renderClient = client = 0;

            if (eventHandle != 0)
            {
                CloseHandle(eventHandle);
                eventHandle = 0;
            }
        }

        /// <summary>
        /// Stops the loop, wakes it so it notices at once, and waits for the render thread to close the stream.
        /// </summary>
        public void Dispose()
        {
            if (thread == null)
                return;

            bool wake;

            lock (gate)
            {
                closing = true;
                wake = native != 0;

                if (wake)
                    SakuraAudioNative.sakura_audio_wasapi_stop(native);
            }

            // Only while there's a loop: the event is then kept until the close below, which a stream that never opened
            // doesn't wait for.
            if (wake)
                SetEvent(eventHandle);

            closeRequested.Set();

            if (!thread.Join(close_timeout_ms))
                Logger.Error($"The WASAPI render thread didn't close within {close_timeout_ms / 1000} seconds.");

            thread = null;
        }

        private static string describeFormat(WaveFormatExtensible format) => SampleFormatOf(format) switch
        {
            SakuraAudioNative.SAMPLE_FLOAT32 => $"{format.Format.Channels} ch float",
            SakuraAudioNative.SAMPLE_INT16 => $"{format.Format.Channels} ch 16-bit",
            SakuraAudioNative.SAMPLE_INT24 => $"{format.Format.Channels} ch 24-bit",
            SakuraAudioNative.SAMPLE_INT32 => format.ValidBitsPerSample == 24 ? $"{format.Format.Channels} ch 24-bit in 32" : $"{format.Format.Channels} ch 32-bit",
            _ => $"{format.Format.Channels} ch, format {format.Format.FormatTag}"
        };

        private static string describe(int result) => result switch
        {
            SakuraAudioNative.TIMEOUT => "no event for two seconds",
            SakuraAudioNative.ERROR => "the wait failed",
            AUDCLNT_E_DEVICE_INVALIDATED => "the device went away",
            _ => $"0x{result:X8}"
        };

        private static bool succeeded(int result, string what)
        {
            if (result >= 0)
                return true;

            Logger.Error($"Could not {what}: {describe(result)}, so the audio plays through SDL.");
            return false;
        }
    }
}
