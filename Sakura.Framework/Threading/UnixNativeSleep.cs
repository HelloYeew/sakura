// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Threading;

/// <summary>
/// High-precision sleep on Unix/macOS using nanosleep(2).
/// Falls back gracefully if libc cannot be found.
/// </summary>
internal class UnixNativeSleep : INativeSleep
{
    [StructLayout(LayoutKind.Sequential)]
    private struct TimeSpec
    {
        public nint Seconds;
        public nint NanoSeconds;
    }

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    private static extern int nanosleep(in TimeSpec duration, out TimeSpec remaining);

    private const int eintr = 4;

    public static bool IsAvailable { get; } = probe();

    /// <summary>
    /// Probes whether nanosleep is available by calling it with a short duration.
    /// </summary>
    /// <returns>True if nanosleep is available; false if it throws (e.g. due to missing libc).</returns>
    private static bool probe()
    {
        try
        {
            var t = new TimeSpec
            {
                Seconds = 0,
                NanoSeconds = 1
            };
            nanosleep(in t, out _);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Sleep(TimeSpan duration)
    {
        const long ns_per_second = 1_000_000_000L;
        long totalNs = (long)duration.TotalNanoseconds;

        var ts = new TimeSpec
        {
            Seconds = (nint)(totalNs / ns_per_second),
            NanoSeconds = (nint)(totalNs % ns_per_second),
        };

        int ret;
        while ((ret = nanosleep(in ts, out var rem)) == -1 && Marshal.GetLastPInvokeError() == eintr)
        {
            // Interrupted by a signal — sleep the remaining time.
            ts = rem;
        }

        return ret == 0;
    }

    public void Dispose() { }
}
