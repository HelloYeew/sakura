// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Threading;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Threading;

/// <summary>
/// Waits for a frame deadline by sleeps through the bulk of the wait with the platform's
/// <see cref="INativeSleep"/>, then busy-spins the last stretch (the guard) so the frame lands on time.
/// </summary>
/// <remarks>
/// The guard is learned per thread rather than fixed, because how late the OS wakes a sleeping thread
/// differs by an order of magnitude between platforms. nanosleep on macOS and Linux wakes within tens of
/// microseconds, while a Windows high-resolution waitable timer has been measured waking ~0.5ms late
/// even for very short requests. A fixed guard is either wasted spinning on the former or too short on
/// the latter, and every wake that overruns it becomes a late frame that resets the thread's cadence.
/// </remarks>
internal sealed class FrameWaiter : IDisposable
{
    /// <summary>
    /// The fraction of wakes the guard aims to cover. The rest overrun it into a late frame.
    /// </summary>
    private const double target_quantile = 0.99;

    /// <summary>
    /// How far one sample moves the guard. A late wake raises it by ~<see cref="target_quantile"/> of
    /// this and an on-time one lowers it by the remainder, which settles where 1% of wakes are late. It
    /// adapts upward within a few dozen late wakes, yet a single outlier (a preemption, a GC suspension)
    /// moves it no further than any other late wake would.
    /// </summary>
    private const double step_us = 20;

    private const double initial_guard_us = 500;

    /// <summary>
    /// Past this, frames are allowed to run late rather than spin away most of a frame. Only reachable
    /// when the OS cannot sleep accurately at all.
    /// </summary>
    private const double max_guard_us = 2000;

    private static readonly double ticks_per_us = Stopwatch.Frequency / 1_000_000.0;
    private static readonly double step_ticks = step_us * ticks_per_us;
    private static readonly double max_guard_ticks = max_guard_us * ticks_per_us;
    private static readonly double timespan_ticks_per_stopwatch_tick = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

    private readonly INativeSleep? nativeSleep;
    private readonly GlobalStatistic<double>? statGuard;

    /// <summary>
    /// How much of each precise wait is left to the spin, in stopwatch ticks.
    /// </summary>
    internal double GuardTicks { get; private set; } = initial_guard_us * ticks_per_us;

    /// <param name="name">The owning thread, used to label its guard in <see cref="GlobalStatistics"/>.</param>
    public FrameWaiter(string name)
        : this(CreateNativeSleep())
    {
        statGuard = GlobalStatistics.Get<double>("Threading", $"{name} Sleep Guard", StatisticKind.Gauge, StatisticUnit.Milliseconds);

        Logger.Debug($"FrameWaiter '{name}' initialized with native sleep: {(nativeSleep != null ? nativeSleep.GetType().Name : "none")}");
    }

    internal FrameWaiter(INativeSleep? nativeSleep)
    {
        this.nativeSleep = nativeSleep;
    }

    /// <summary>
    /// Blocks until <paramref name="deadline"/>, a <see cref="Stopwatch.GetTimestamp"/> value.
    /// </summary>
    /// <param name="deadline">The timestamp to return at.</param>
    /// <param name="precise">
    /// Whether to spin the guard. When false the whole wait is slept and the OS's lateness accepted to save
    /// CPU. Those wakes also leave the guard alone, since they come from an inactive window whose timers
    /// may be throttled and are not the ones a focused window will see.
    /// </param>
    public void WaitUntil(long deadline, bool precise)
    {
        long now = Stopwatch.GetTimestamp();

        if (!precise)
        {
            if (deadline > now)
                sleep(deadline - now);

            return;
        }

        long sleepTicks = deadline - now - (long)GuardTicks;

        if (sleepTicks > 0)
        {
            sleep(sleepTicks);
            RecordOvershoot(Stopwatch.GetTimestamp() - (now + sleepTicks));
        }

        // Thread.SpinWait issues a CPU pause hint without yielding, which would overshoot a
        // sub-millisecond deadline.
        while (Stopwatch.GetTimestamp() < deadline)
            Thread.SpinWait(1);
    }

    /// <summary>
    /// Folds one sleep's lateness into the guard, tracking its <see cref="target_quantile"/>.
    /// </summary>
    /// <param name="overshootTicks">How long after the requested time the sleep returned, in stopwatch ticks.</param>
    internal void RecordOvershoot(long overshootTicks)
    {
        double guard = overshootTicks > GuardTicks
            ? GuardTicks + step_ticks * target_quantile
            : GuardTicks - step_ticks * (1 - target_quantile);

        GuardTicks = Math.Clamp(guard, 0, max_guard_ticks);

        if (statGuard != null)
            statGuard.Value = GuardTicks / ticks_per_us / 1000;
    }

    public void Dispose() => nativeSleep?.Dispose();

    private void sleep(long ticks)
    {
        var duration = TimeSpan.FromTicks((long)(ticks * timespan_ticks_per_stopwatch_tick));

        if (nativeSleep?.Sleep(duration) != true)
            Thread.Sleep(duration);
    }

    internal static INativeSleep? CreateNativeSleep()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsNativeSleep();

        return UnixNativeSleep.IsAvailable ? new UnixNativeSleep() : null;
    }
}
