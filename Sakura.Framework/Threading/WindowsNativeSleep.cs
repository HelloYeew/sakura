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
    [DllImport("ntdll.dll", SetLastError = false)]
    private static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

    [DllImport("ntdll.dll", SetLastError = false)]
    private static extern int NtDelayExecution(bool alertable, ref long delayInterval);

    [DllImport("winmm.dll", SetLastError = false)]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", SetLastError = false)]
    private static extern uint timeEndPeriod(uint uPeriod);

    private const uint resolution1_ms = 10_000;

    // Initial conservative overshoot estimate: 0.2 ms.
    // This will be adapted downward rapidly after the first few sleeps.
    private double overshootEstimateSeconds = 0.0002;

    // EMA smoothing factor — higher = faster adaptation, more jitter.
    private const double ema_alpha = 0.1;

    public WindowsNativeSleep()
    {
        _ = timeBeginPeriod(1);
        _ = NtSetTimerResolution(resolution1_ms, true, out _);
    }

    public bool Sleep(TimeSpan duration)
    {
        long freq = Stopwatch.Frequency;
        long targetTick = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * freq);

        // Ask NtDelayExecution to sleep up to (target - overshoot estimate).
        // If the requested sleep is already shorter than our estimate, skip it.
        double sleepSeconds = duration.TotalSeconds - overshootEstimateSeconds;

        if (sleepSeconds > 0)
        {
            long now = Stopwatch.GetTimestamp();
            long spinStartTick = targetTick - (long)(overshootEstimateSeconds * freq);

            if (spinStartTick > now)
            {
                long delayTicks100Ns = -(long)((spinStartTick - now) * 10_000_000L / freq);
                _ = NtDelayExecution(false, ref delayTicks100Ns);
            }
        }

        // Measure actual overshoot: how early/late did NtDelay wake us?
        long afterSleep = Stopwatch.GetTimestamp();
        long spinStartTarget = targetTick - (long)(overshootEstimateSeconds * freq);
        double actualOvershootSeconds = Math.Max(0, (afterSleep - spinStartTarget) / (double)freq);

        // Update the rolling EMA estimate, but never let it go below 0 or above 2 ms.
        overshootEstimateSeconds = Math.Clamp(
            overshootEstimateSeconds + ema_alpha * (actualOvershootSeconds - overshootEstimateSeconds),
            0.0,
            0.002
        );

        // Busy-spin the remaining ticks — this is now as short as possible.
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
