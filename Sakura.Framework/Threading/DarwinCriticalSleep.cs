// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Threading;

/// <summary>
/// A precise sleep on macOS and iOS: a kqueue timer marked critical, which the kernel fires on time instead of delaying
/// it to batch wake-ups. An ordinary sleep there may wake up to an eighth of its length late, capped at a millisecond:
/// about 0.9 ms on a 120 Hz frame wait, even at the highest priority. A critical timer wakes within tens of microseconds
/// at a priority above 37, idle or under load. It costs the system the batching, so it's only for precise waits.
/// </summary>
internal sealed class DarwinCriticalSleep : INativeSleep
{
    private const string lib_system = "/usr/lib/libSystem.dylib";

    private const short evfilt_timer = -7;
    private const ushort ev_add = 0x0001;
    private const ushort ev_oneshot = 0x0010;
    private const ushort ev_error = 0x4000;
    private const uint note_absolute = 0x00000008;
    private const uint note_critical = 0x00000020;
    private const uint note_machtime = 0x00000100;
    private const int eintr = 4;

    /// <summary>
    /// Whether the platform has the timer.
    /// </summary>
    public static bool IsAvailable => OperatingSystem.IsMacOS() || OperatingSystem.IsIOS();

    private static readonly double mach_units_per_nanosecond = machUnitsPerNanosecond();

    private readonly int queue;

    private DarwinCriticalSleep(int queue)
    {
        this.queue = queue;
    }

    /// <summary>
    /// Opens the sleep's own queue, or returns null if the system won't give one.
    /// </summary>
    public static DarwinCriticalSleep? Create()
    {
        int queue = kqueue();

        return queue >= 0 ? new DarwinCriticalSleep(queue) : null;
    }

    public unsafe bool Sleep(TimeSpan duration)
    {
        long nanoseconds = (long)duration.TotalNanoseconds;

        if (nanoseconds <= 0)
            return true;

        // Absolute, so a wait that a signal interrupts resumes towards the same moment.
        ulong deadline = mach_absolute_time() + (ulong)(nanoseconds * mach_units_per_nanosecond);

        var change = new KEvent64
        {
            Ident = 1,
            Filter = evfilt_timer,
            Flags = ev_add | ev_oneshot,
            FilterFlags = note_machtime | note_absolute | note_critical,
            Data = (long)deadline
        };

        KEvent64 fired;

        while (true)
        {
            int count = kevent64(queue, &change, 1, &fired, 1, 0, IntPtr.Zero);

            if (count == 1)
                return (fired.Flags & ev_error) == 0;

            if (count == -1 && Marshal.GetLastPInvokeError() == eintr)
                continue;

            return false;
        }
    }

    public void Dispose() => close(queue);

    private static double machUnitsPerNanosecond()
    {
        if (!IsAvailable || mach_timebase_info(out var timebase) != 0 || timebase.Numerator == 0)
            return 1;

        return (double)timebase.Denominator / timebase.Numerator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEvent64
    {
        public ulong Ident;
        public short Filter;
        public ushort Flags;
        public uint FilterFlags;
        public long Data;
        public ulong UserData;
        public ulong Extension0;
        public ulong Extension1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MachTimebaseInfo
    {
        public uint Numerator;
        public uint Denominator;
    }

    [DllImport(lib_system, SetLastError = true)]
    private static extern int kqueue();

    [DllImport(lib_system, SetLastError = true)]
    private static extern unsafe int kevent64(int queue, KEvent64* changes, int changeCount, KEvent64* events, int eventCount, uint flags, IntPtr timeout);

    [DllImport(lib_system)]
    private static extern int close(int descriptor);

    [DllImport(lib_system)]
    private static extern ulong mach_absolute_time();

    [DllImport(lib_system)]
    private static extern int mach_timebase_info(out MachTimebaseInfo info);
}
