// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The parts of Android's AAudio (<c>libaaudio.so</c>, API 26 and up; usage and content type from 28) that
/// <see cref="AAudioOutput"/> uses. Every call is made off the audio callback.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class AAudioNative
{
    private const string lib_name = "aaudio";

    public const int OK = 0;
    public const int ERROR_DISCONNECTED = -899;

    public const int FORMAT_PCM_FLOAT = 2;
    public const int SHARING_MODE_EXCLUSIVE = 0;
    public const int SHARING_MODE_SHARED = 1;
    public const int PERFORMANCE_MODE_LOW_LATENCY = 12;
    public const int USAGE_MEDIA = 1;
    public const int USAGE_GAME = 14;
    public const int CONTENT_TYPE_MUSIC = 2;

    public const int STREAM_STATE_STOPPING = 9;
    public const int STREAM_STATE_DISCONNECTED = 13;

    // The clock AAudio's timestamps are read against.
    public const int CLOCK_MONOTONIC = 1;

    [DllImport(lib_name)]
    public static extern int AAudio_createStreamBuilder(out nint builder);

    [DllImport(lib_name)]
    public static extern nint AAudio_convertResultToText(int result);

    [DllImport(lib_name)]
    public static extern void AAudioStreamBuilder_setFormat(nint builder, int format);

    [DllImport(lib_name)]
    public static extern void AAudioStreamBuilder_setChannelCount(nint builder, int channelCount);

    [DllImport(lib_name)]
    public static extern void AAudioStreamBuilder_setSampleRate(nint builder, int sampleRate);

    [DllImport(lib_name)]
    public static extern void AAudioStreamBuilder_setPerformanceMode(nint builder, int mode);

    [DllImport(lib_name)]
    public static extern void AAudioStreamBuilder_setSharingMode(nint builder, int sharingMode);

    [DllImport(lib_name)]
    public static extern void AAudioStreamBuilder_setUsage(nint builder, int usage);

    [DllImport(lib_name)]
    public static extern void AAudioStreamBuilder_setContentType(nint builder, int contentType);

    [DllImport(lib_name)]
    public static extern void AAudioStreamBuilder_setDataCallback(nint builder, nint callback, nint userData);

    [DllImport(lib_name)]
    public static extern int AAudioStreamBuilder_openStream(nint builder, out nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStreamBuilder_delete(nint builder);

    [DllImport(lib_name)]
    public static extern int AAudioStream_requestStart(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_requestPause(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_requestStop(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_close(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_waitForStateChange(nint stream, int inputState, out int nextState, long timeoutNanoseconds);

    [DllImport(lib_name)]
    public static extern int AAudioStream_getState(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_getSampleRate(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_getFramesPerBurst(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_getBufferSizeInFrames(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_setBufferSizeInFrames(nint stream, int numFrames);

    [DllImport(lib_name)]
    public static extern int AAudioStream_getBufferCapacityInFrames(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_getXRunCount(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_getTimestamp(nint stream, int clockId, out long framePosition, out long timeNanoseconds);

    [DllImport(lib_name)]
    public static extern long AAudioStream_getFramesWritten(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_getSharingMode(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_getPerformanceMode(nint stream);

    [DllImport(lib_name)]
    public static extern int AAudioStream_getDeviceId(nint stream);

    /// <summary>
    /// The time on <see cref="CLOCK_MONOTONIC"/>, in nanoseconds, to set AAudio's timestamps against.
    /// </summary>
    public static long MonotonicNanoseconds() => clock_gettime(CLOCK_MONOTONIC, out var time) == 0 ? (long)time.Seconds * 1_000_000_000 + time.Nanoseconds : 0;

    [DllImport("libc")]
    private static extern int clock_gettime(int clockId, out Timespec time);

    // Both fields are a C long: 64 bits on 64-bit Android, 32 on 32-bit.
    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public nint Seconds;
        public nint Nanoseconds;
    }
}
