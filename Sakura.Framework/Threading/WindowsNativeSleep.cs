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

    // Leave this many ticks at the end for the spin phase.
    // Must be less than one frame at the highest target rate (1ms at 1000 Hz).
    // 0.5 ms gives NtDelay time to wake and leaves a short spin window.
    private static readonly long spin_ticks = (long)(Stopwatch.Frequency * 0.0005);

    public WindowsNativeSleep()
    {
        _ = timeBeginPeriod(1);
        _ = NtSetTimerResolution(resolution1_ms, true, out _);
    }

    public bool Sleep(TimeSpan duration)
    {
        long targetTick = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);

        // Use NtDelayExecution for everything except the last 0.5 ms.
        long spinStartTick = targetTick - spin_ticks;
        long now = Stopwatch.GetTimestamp();

        if (spinStartTick > now)
        {
            // Convert remaining ticks to 100-ns units for NtDelayExecution.
            long delayTicks100Ns = -(long)((spinStartTick - now) * 10_000_000L / Stopwatch.Frequency);
            _ = NtDelayExecution(false, ref delayTicks100Ns);
        }

        // Busy-spin for the final 0.5 ms to land precisely on targetTick.
        // Thread.SpinWait(1) burns a very small number of cycles without yielding
        // to the scheduler, giving sub-100us precision.
        while (Stopwatch.GetTimestamp() < targetTick)
            Thread.SpinWait(1);

        return true;
    }

    public void Dispose()
    {
        _ = NtSetTimerResolution(resolution1_ms, false, out _);
        _ = timeEndPeriod(1);
    }
}
