// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Sakura.Framework.Threading;

/// <summary>
/// High-precision sleep on Windows.
/// Uses timeBeginPeriod(1) + NtDelayExecution for the bulk of the sleep,
/// then spin-waits for the sub-millisecond remainder to achieve precise timing.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsNativeSleep : INativeSleep
{
    // Raises/lowers the system timer resolution (100-ns units).
    [DllImport("ntdll.dll", SetLastError = false)]
    private static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

    // Relative sleep in 100-ns units (negative value = relative delay).
    [DllImport("ntdll.dll", SetLastError = false)]
    private static extern int NtDelayExecution(bool alertable, ref long delayInterval);

    // Raises the multimedia timer resolution to 1 ms system-wide.
    // This is necessary in addition to NtSetTimerResolution to ensure
    // NtDelayExecution actually wakes up on time.
    [DllImport("winmm.dll", SetLastError = false)]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", SetLastError = false)]
    private static extern uint timeEndPeriod(uint uPeriod);

    // 1 ms in 100-ns units.
    private const uint resolution1_ms = 10_000;

    /// <summary>
    /// How much time to leave for the spin-wait at the end.
    /// NtDelayExecution is unreliable below ~1 ms, so we sleep for
    /// (duration - spin_threshold) via NtDelay and spin the rest.
    /// </summary>
    private static readonly TimeSpan spin_threshold = TimeSpan.FromMilliseconds(1.5);

    private static readonly double ticks_per_ms = Stopwatch.Frequency / 1000.0;

    public WindowsNativeSleep()
    {
        timeBeginPeriod(1);
        NtSetTimerResolution(resolution1_ms, true, out _);
    }

    public bool Sleep(TimeSpan duration)
    {
        long targetTick = Stopwatch.GetTimestamp() + (long)(duration.TotalMilliseconds * ticks_per_ms);

        // Sleep the bulk via NtDelayExecution, leaving spin_threshold remaining.
        TimeSpan sleepDuration = duration - spin_threshold;

        if (sleepDuration > TimeSpan.Zero)
        {
            long interval = -(long)(sleepDuration.TotalNanoseconds / 100);
            NtDelayExecution(false, ref interval);
        }

        // Spin-wait for the remainder to hit the precise target.
        // SpinWait.SpinOnce() yields the CPU slice after a few spins,
        // keeping CPU usage low while still waking up precisely.
        var spinner = new SpinWait();
        while (Stopwatch.GetTimestamp() < targetTick)
            spinner.SpinOnce();

        return true;
    }

    public void Dispose()
    {
        _ = NtSetTimerResolution(resolution1_ms, false, out _);
        _ = timeEndPeriod(1);
    }
}
