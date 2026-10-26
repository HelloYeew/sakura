// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Sakura.Framework.Logging;
using static Sakura.Framework.Audio.SdlEngine.AAudioNative;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The native engine playing through Android's AAudio with nothing in between: it mixes on AAudio's own
/// high-priority thread, straight into AAudio's buffer.
/// </summary>
/// <remarks>
/// <para>
/// Through SDL, the mix ran on SDL's thread into a buffer SDL kept up to two periods ahead of AAudio's, and SDL
/// kept the stream to itself, so neither how often the output ran dry nor when the audio reached the listener could
/// be read. Here both can: <see cref="Xruns"/>, and <see cref="TrySampleLatency"/> from AAudio's timestamps,
/// which count the time spent below the app too, in the system's mixer and the hardware, and on recent Android a
/// Bluetooth headset's.
/// </para>
/// <para>
/// One thread controls it at a time: the audio thread, or the host while that thread is parked for the background.
/// </para>
/// </remarks>
internal sealed class AAudioOutput : IDisposable
{
    private readonly SakuraAudioEngine engine;
    private readonly int requestedRate;
    private readonly int usage;

    private nint stream;
    private bool started;

    // Once set, no stream opens again: the engine a new one would mix is freed after this is.
    private bool disposed;

    // The buffer size last asked for, asked for again on a reopened stream.
    private int requestedBufferFrames;

    // Underruns counted by the streams a reopen replaced: AAudio's count starts again with each stream.
    private int earlierXruns;

    /// <summary>
    /// The rate the stream runs at, which is the one asked for: AAudio converts where the hardware runs at another.
    /// </summary>
    public int SampleRate { get; private set; }

    /// <summary>
    /// Frames the output takes at a time, and so how much the buffer grows or shrinks by.
    /// </summary>
    public int FramesPerBurst { get; private set; }

    /// <summary>
    /// The largest <see cref="BufferFrames"/> can be.
    /// </summary>
    public int CapacityFrames { get; private set; }

    /// <summary>
    /// Times AAudio disconnected the stream (the output changed, headphones plugged in or out) and it was opened again.
    /// </summary>
    public int Reopens { get; private set; }

    /// <summary>
    /// Whether the stream is gone: AAudio disconnected it, it was closed, or a reopen failed. <see cref="Reopen"/> makes
    /// another.
    /// </summary>
    /// <remarks>
    /// The state is fetched from the audio service first. Read as it was last updated, it can stay what it was for good
    /// after the service let the stream go: a stream with no callback running (paused for the background) only takes
    /// the disconnect in when asked, and a callback thread that stopped (a write the service timed out) said so to an
    /// error callback alone.
    /// </remarks>
    public bool Disconnected
    {
        get
        {
            if (stream == 0)
                return true;

            int result = AAudioStream_waitForStateChange(stream, STREAM_STATE_UNKNOWN, out int state, 0);

            if (result == ERROR_DISCONNECTED)
                return true;

            if (result != OK)
                state = AAudioStream_getState(stream);

            return state == STREAM_STATE_DISCONNECTED;
        }
    }

    /// <summary>
    /// Whether there's a stream open, mixing the engine on its callback once started. Without one, nothing does.
    /// </summary>
    public bool HasStream => stream != 0;

    /// <summary>
    /// Whether the stream is meant to be playing: <see cref="Start"/> was called last rather than <see cref="Pause"/>,
    /// whether or not it took.
    /// </summary>
    public bool Started => started;

    /// <summary>
    /// The ID of the device the stream plays on, as Android's AudioDeviceInfo has it, or 0 where there's no stream or
    /// AAudio doesn't say.
    /// </summary>
    public int DeviceId => stream == 0 ? 0 : AAudioStream_getDeviceId(stream);

    /// <summary>
    /// Frames the stream holds ahead of the output, which is the part of the latency the app decides.
    /// </summary>
    public int BufferFrames => stream == 0 ? 0 : AAudioStream_getBufferSizeInFrames(stream);

    /// <summary>
    /// How many times the output has run dry since the first stream opened: each a gap the listener may have heard.
    /// </summary>
    public int Xruns => earlierXruns + currentXruns();

    private AAudioOutput(SakuraAudioEngine engine, int sampleRate, int usage)
    {
        this.engine = engine;
        requestedRate = sampleRate;
        this.usage = usage;
    }

    /// <summary>
    /// Whether this platform and the shipped <c>libsakura-audio</c> can play this way.
    /// </summary>
    public static bool IsAvailable => OperatingSystem.IsAndroid() && SakuraAudioEngine.AAudioCallback != 0;

    /// <summary>
    /// Opens a stream for <paramref name="engine"/>, not started yet, or returns null where AAudio won't.
    /// </summary>
    /// <param name="engine">The engine to mix, at its own rate and channel count.</param>
    /// <param name="usage">What the sound is for (<see cref="USAGE_GAME"/> or <see cref="USAGE_MEDIA"/>), which the
    /// system's volume and mixing follow.</param>
    public static AAudioOutput? TryOpen(SakuraAudioEngine engine, int usage)
    {
        try
        {
            var output = new AAudioOutput(engine, engine.SampleRate, usage);
            return output.open() ? output : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Logger.Verbose($"AAudio isn't available ({e.GetType().Name}), so the audio plays through SDL.");
            return null;
        }
    }

    private bool open()
    {
        int result = AAudio_createStreamBuilder(out nint builder);

        if (result != OK)
        {
            Logger.Error($"Could not set up an AAudio stream: {describe(result)}");
            return false;
        }

        try
        {
            AAudioStreamBuilder_setFormat(builder, FORMAT_PCM_FLOAT);
            AAudioStreamBuilder_setChannelCount(builder, engine.Channels);
            AAudioStreamBuilder_setSampleRate(builder, requestedRate);

            // Shared rather than exclusive: an exclusive stream skips the system's own effects, such as a phone's
            // equaliser, so the app would sound unlike every other one there.
            AAudioStreamBuilder_setPerformanceMode(builder, PERFORMANCE_MODE_LOW_LATENCY);
            AAudioStreamBuilder_setSharingMode(builder, SHARING_MODE_SHARED);
            AAudioStreamBuilder_setUsage(builder, usage);
            AAudioStreamBuilder_setContentType(builder, CONTENT_TYPE_MUSIC);

            // Native, straight into the engine: no managed code ever runs on AAudio's thread.
            AAudioStreamBuilder_setDataCallback(builder, SakuraAudioEngine.AAudioCallback, engine.Handle);

            result = AAudioStreamBuilder_openStream(builder, out stream);
        }
        finally
        {
            AAudioStreamBuilder_delete(builder);
        }

        if (result != OK)
        {
            stream = 0;
            Logger.Error($"Could not open an AAudio stream: {describe(result)}");
            return false;
        }

        SampleRate = AAudioStream_getSampleRate(stream);
        FramesPerBurst = AAudioStream_getFramesPerBurst(stream);
        CapacityFrames = AAudioStream_getBufferCapacityInFrames(stream);
        return true;
    }

    /// <summary>
    /// Starts the stream playing, or once it's open again, where AAudio disconnected it.
    /// </summary>
    public bool Start()
    {
        started = true;
        return !Disconnected && succeeded(AAudioStream_requestStart(stream), "start");
    }

    public void Pause()
    {
        started = false;

        if (stream != 0)
            succeeded(AAudioStream_requestPause(stream), "pause");
    }

    /// <summary>
    /// Asks for a buffer of <paramref name="frames"/>, which AAudio rounds to its bursts and keeps within
    /// <see cref="CapacityFrames"/>. Kept for a reopened stream too.
    /// </summary>
    /// <returns>The size granted, or 0 where there's no stream.</returns>
    public int SetBufferFrames(int frames)
    {
        requestedBufferFrames = frames;

        if (stream == 0)
            return 0;

        int granted = AAudioStream_setBufferSizeInFrames(stream, frames);
        grantedBufferFrames = granted > 0 ? granted : BufferFrames;
        return grantedBufferFrames;
    }

    // What the last SetBufferFrames was given, to tell when the stream's buffer changed under it.
    private int grantedBufferFrames;

    /// <summary>
    /// Whether the stream's buffer is no longer the size it was given. On the path older phones take, that's the system
    /// having rebuilt the track under the stream (its audio service restarted): at AAudio's default size, tens of
    /// milliseconds more, and with its own frame counts, against which the stream's timestamps no longer add up.
    /// <see cref="Reopen"/> starts over with a stream that's whole.
    /// </summary>
    public bool Rebuilt => stream != 0 && grantedBufferFrames > 0 && BufferFrames != grantedBufferFrames;

    /// <summary>
    /// Opens another stream in place of one AAudio disconnected, as it does when the output changes, with the same
    /// buffer, playing if the old one was.
    /// </summary>
    /// <returns>Whether it opened. If not, try again later: the output may still be changing. Never once disposed.</returns>
    public bool Reopen()
    {
        // A disposed output reads as disconnected, and its owner may still be polling it while it shuts down.
        if (disposed)
            return false;

        Close();

        if (!open())
            return false;

        Reopens++;

        if (requestedBufferFrames > 0)
            SetBufferFrames(requestedBufferFrames);

        if (started)
            Start();

        return true;
    }

    /// <summary>
    /// How long the frames written last take to reach the listener, from where AAudio says the output is now.
    /// Unavailable until the stream has been running a moment, and while it's paused.
    /// </summary>
    public bool TrySampleLatency(out double milliseconds)
    {
        milliseconds = 0;

        if (stream == 0 || AAudioStream_getTimestamp(stream, CLOCK_MONOTONIC, out long presentedFrame, out long presentedNanoseconds) != OK)
            return false;

        long now = MonotonicNanoseconds();

        if (now == 0)
            return false;

        milliseconds = LatencyMilliseconds(presentedFrame, presentedNanoseconds, AAudioStream_getFramesWritten(stream), now, SampleRate);
        return true;
    }

    /// <summary>
    /// The delay between now and the last frame written being heard, given that <paramref name="presentedFrame"/>
    /// was heard at <paramref name="presentedNanoseconds"/>.
    /// </summary>
    internal static double LatencyMilliseconds(long presentedFrame, long presentedNanoseconds, long framesWritten, long nowNanoseconds, int sampleRate)
    {
        double writtenHeardAt = presentedNanoseconds + (framesWritten - presentedFrame) * 1_000_000_000.0 / sampleRate;
        return (writtenHeardAt - nowNanoseconds) / 1_000_000.0;
    }

    /// <summary>
    /// What the stream turned out to be, for the log and the statistics.
    /// </summary>
    public string Describe()
    {
        if (stream == 0)
            return "AAudio (closed)";

        string mode = AAudioStream_getPerformanceMode(stream) == PERFORMANCE_MODE_LOW_LATENCY ? "low latency" : "not low latency";
        string sharing = AAudioStream_getSharingMode(stream) == SHARING_MODE_EXCLUSIVE ? "exclusive" : "shared";

        return $"AAudio, device {AAudioStream_getDeviceId(stream)}, {SampleRate} Hz, {mode}, {sharing}, {FramesPerBurst}-frame bursts";
    }

    private int currentXruns() => stream == 0 ? 0 : Math.Max(0, AAudioStream_getXRunCount(stream));

    /// <summary>
    /// Closes the stream, keeping what it was set to (its buffer, and whether it plays) for <see cref="Reopen"/>. Once
    /// this returns no callback mixes the engine, until another stream opens.
    /// </summary>
    public void Close()
    {
        earlierXruns += currentXruns();
        close();
    }

    private void close()
    {
        if (stream == 0)
            return;

        // Stopped first, so no callback is still mixing the engine once this returns.
        if (AAudioStream_requestStop(stream) == OK)
        {
            int state = AAudioStream_getState(stream);

            while (state == STREAM_STATE_STOPPING)
            {
                if (AAudioStream_waitForStateChange(stream, STREAM_STATE_STOPPING, out state, 100_000_000) != OK)
                    break;
            }
        }

        // Oboe's wait between stopping a stream and closing it, a burst and at least 10 ms: up to Android 11 a callback
        // could still come in after the stop, and closing under it crashed inside libaaudio.
        Thread.Sleep(Math.Max(10, FramesPerBurst * 1000 / Math.Max(1, SampleRate) + 1));

        AAudioStream_close(stream);
        stream = 0;
    }

    public void Dispose()
    {
        disposed = true;
        close();
    }

    private static bool succeeded(int result, string what)
    {
        if (result == OK)
            return true;

        Logger.Error($"Could not {what} the AAudio stream: {describe(result)}");
        return false;
    }

    private static string describe(int result) => Marshal.PtrToStringUTF8(AAudio_convertResultToText(result)) ?? result.ToString();
}
