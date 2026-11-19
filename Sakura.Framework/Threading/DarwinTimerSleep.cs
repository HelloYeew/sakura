// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Threading;

/// <summary>
/// A sleep on macOS and iOS: a timer on a kqueue of its own, next to a user event that <see cref="Wake"/> triggers, so another
/// thread can end the sleep early.
/// </summary>
/// <remarks>
/// A critical timer is the precise one: the kernel fires it on time instead of delaying it to batch wake-ups. An ordinary
/// timer there may wake up to an eighth of its length late, capped at a millisecond: about 0.9 ms on a 120 Hz frame wait, even
/// at the highest priority. A critical timer wakes within tens of microseconds at a priority above 37, idle or under load. It
/// costs the system the batching, so it's only for precise waits; an ordinary one is late the way nanosleep is.
/// </remarks>
internal sealed class DarwinTimerSleep : INativeSleep
{
    private const string lib_system = "/usr/lib/libSystem.dylib";

    private const short evfilt_timer = -7;
    private const short evfilt_user = -10;
    private const ushort ev_add = 0x0001;
    private const ushort ev_delete = 0x0002;
    private const ushort ev_oneshot = 0x0010;
    private const ushort ev_clear = 0x0020;
    private const ushort ev_error = 0x4000;
    private const uint note_absolute = 0x00000008;
    private const uint note_critical = 0x00000020;
    private const uint note_machtime = 0x00000100;
    private const uint note_trigger = 0x01000000;
    private const int eintr = 4;

    private const ulong timer_ident = 1;
    private const ulong wake_ident = 2;

    /// <summary>
    /// Whether the platform has the timer.
    /// </summary>
    public static bool IsAvailable => OperatingSystem.IsMacOS() || OperatingSystem.IsIOS();

    private static readonly double mach_units_per_nanosecond = machUnitsPerNanosecond();

    private readonly int queue;
    private readonly uint timerFlags;

    private DarwinTimerSleep(int queue, bool critical)
    {
        this.queue = queue;
        timerFlags = critical ? note_machtime | note_absolute | note_critical : note_machtime | note_absolute;
    }

    /// <summary>
    /// Opens the sleep's own queue with its wake event, or returns null if the system won't give one.
    /// </summary>
    /// <param name="critical">Whether its timer is critical: precise, at the cost of the system's batching.</param>
    public static unsafe DarwinTimerSleep? Create(bool critical)
    {
        int queue = kqueue();

        if (queue < 0)
            return null;

        // Cleared as it's taken, so one wake ends one sleep however many times it was triggered.
        var wakeEvent = new KEvent64
        {
            Ident = wake_ident,
            Filter = evfilt_user,
            Flags = ev_add | ev_clear
        };

        if (kevent64(queue, &wakeEvent, 1, null, 0, 0, IntPtr.Zero) != 0)
        {
            close(queue);
            return null;
        }

        return new DarwinTimerSleep(queue, critical);
    }

    public unsafe SleepResult Sleep(TimeSpan duration)
    {
        long nanoseconds = (long)duration.TotalNanoseconds;

        if (nanoseconds <= 0)
            return SleepResult.Slept;

        // Absolute, so a wait that a signal interrupts resumes towards the same moment.
        ulong deadline = mach_absolute_time() + (ulong)(nanoseconds * mach_units_per_nanosecond);

        var change = new KEvent64
        {
            Ident = timer_ident,
            Filter = evfilt_timer,
            Flags = ev_add | ev_oneshot,
            FilterFlags = timerFlags,
            Data = (long)deadline
        };

        KEvent64 fired;

        while (true)
        {
            int count = kevent64(queue, &change, 1, &fired, 1, 0, IntPtr.Zero);

            if (count == 1)
            {
                if ((fired.Flags & ev_error) != 0)
                    return SleepResult.Failed;

                if (fired.Filter != evfilt_user)
                    return SleepResult.Slept;

                // The timer is still set; left, it would fire into the next sleep and end that one early.
                var cancel = new KEvent64
                {
                    Ident = timer_ident,
                    Filter = evfilt_timer,
                    Flags = ev_delete
                };

                _ = kevent64(queue, &cancel, 1, null, 0, 0, IntPtr.Zero);
                return SleepResult.Woken;
            }

            if (count == -1 && Marshal.GetLastPInvokeError() == eintr)
                continue;

            return SleepResult.Failed;
        }
    }

    public unsafe void Wake()
    {
        var trigger = new KEvent64
        {
            Ident = wake_ident,
            Filter = evfilt_user,
            FilterFlags = note_trigger
        };

        _ = kevent64(queue, &trigger, 1, null, 0, 0, IntPtr.Zero);
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
