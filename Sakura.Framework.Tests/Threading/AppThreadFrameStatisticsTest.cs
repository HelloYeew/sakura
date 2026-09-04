// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Statistic;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Tests.Threading;

/// <summary>
/// Covers the distinction the performance overlay is built on
/// what a frame's work cost, as opposed to how long the throttle held the thread for afterward.
/// </summary>
[TestFixture]
public class AppThreadFrameStatisticsTest
{
    private const double target_hz = 100;
    private const double budget_ms = 1000.0 / target_hz;
    private const double work_ms = 2;

    /// <summary>
    /// Burns CPU for a fixed duration. A sleep would be at the mercy of the OS timer; the overlay's
    /// whole point is measuring work, so the test should do actual work.
    /// </summary>
    private static void spin(double milliseconds)
    {
        long until = Stopwatch.GetTimestamp() + (long)(milliseconds / 1000.0 * Stopwatch.Frequency);

        while (Stopwatch.GetTimestamp() < until)
            Thread.SpinWait(1);
    }

    private static ThreadFrameSample[] collect(AppThread thread, int minimumFrames, int timeoutMilliseconds = 5000)
    {
        var destination = new ThreadFrameSample[ThreadFrameStatistics.CAPACITY];
        long cursor = 0;

        var collected = new List<ThreadFrameSample>();
        var timeout = Stopwatch.StartNew();

        while (collected.Count < minimumFrames && timeout.ElapsedMilliseconds < timeoutMilliseconds)
        {
            int count = thread.FrameStatistics.Drain(destination, ref cursor, out _);

            for (int i = 0; i < count; i++)
                collected.Add(destination[i]);

            Thread.Sleep(5);
        }

        return collected.ToArray();
    }

    [Test]
    public void TestBusyTimeExcludesTheThrottleWait()
    {
        var thread = new AppThread("TestThread", () => spin(work_ms), () => target_hz);

        ThreadFrameSample[] samples;

        thread.StartMultiThreaded();

        try
        {
            samples = collect(thread, 20);
        }
        finally
        {
            thread.StopMultiThreaded();
        }

        Assert.That(samples, Is.Not.Empty);

        // Skip the first few frames since the thread is still settling onto its deadline and the JIT is
        // still compiling the frame action.
        var settled = samples.Skip(5).ToArray();
        Assert.That(settled, Is.Not.Empty);

        double medianBusy = median(settled.Select(s => s.BusyMilliseconds));
        double medianElapsed = median(settled.Select(s => s.ElapsedMilliseconds));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settled.Select(s => s.BudgetMilliseconds), Is.All.EqualTo(budget_ms));

            // The point of the whole exercise: busy reflects the ~2ms of work, not the ~10ms period the
            // throttle produces. Before this existed, the only number available was the period.
            Assert.That(medianBusy, Is.EqualTo(work_ms).Within(2), $"busy should track the work done, was {medianBusy:F2}ms");
            Assert.That(medianElapsed, Is.EqualTo(budget_ms).Within(4), $"elapsed should track the frame period, was {medianElapsed:F2}ms");
        }
        Assert.That(medianBusy, Is.LessThan(medianElapsed / 2), "busy and the frame period should not be the same measurement");

        // A thread using a fifth of its budget is not missing deadlines.
        Assert.That(settled.Count(s => s.MissedDeadline), Is.Zero);
    }

    [Test]
    public void TestOverrunningWorkIsReportedAsAMissedDeadline()
    {
        // Twice the budget: the throttle cannot hold this thread to its target rate, and every frame
        // should say so.
        var thread = new AppThread("TestOverrunThread", () => spin(budget_ms * 2), () => target_hz);

        ThreadFrameSample[] samples;

        thread.StartMultiThreaded();

        try
        {
            samples = collect(thread, 10);
        }
        finally
        {
            thread.StopMultiThreaded();
        }

        var settled = samples.Skip(3).ToArray();
        Assert.That(settled, Is.Not.Empty);

        Assert.That(settled.Count(s => s.MissedDeadline), Is.GreaterThan(settled.Length / 2));
    }

    [Test]
    public void TestUnthrottledThreadRecordsNoBudget()
    {
        var thread = new AppThread("TestUnthrottledThread", () => spin(0.5), () => 0);

        ThreadFrameSample[] samples;

        thread.StartMultiThreaded();

        try
        {
            samples = collect(thread, 20);
        }
        finally
        {
            thread.StopMultiThreaded();
        }

        Assert.That(samples, Is.Not.Empty);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(samples.Select(s => s.BudgetMilliseconds), Is.All.Zero);
            Assert.That(samples.Any(s => s.MissedDeadline), Is.False, "a thread with no target rate has no deadline to miss");
        }
    }

    /// <summary>
    /// Time spent waiting on a device is reported on its own and kept out of the busy figure, so a
    /// frame that blocks on the display does not read as a frame that is short of headroom.
    /// </summary>
    [Test]
    public void TestBlockedTimeIsReportedSeparatelyFromBusyTime()
    {
        const double blocked_ms = 3;

        double lastBlocked = 0;

        var thread = new AppThread("TestBlockingThread", () =>
        {
            spin(work_ms);

            // Stands in for the buffer swap: the frame action does the waiting, then hands back how
            // long it waited, exactly as AppHost.PerformDraw does.
            long start = Stopwatch.GetTimestamp();
            spin(blocked_ms);
            lastBlocked = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }, () => target_hz)
        {
            GetBlockedMilliseconds = () => lastBlocked
        };

        ThreadFrameSample[] samples;

        thread.StartMultiThreaded();

        try
        {
            samples = collect(thread, 20);
        }
        finally
        {
            thread.StopMultiThreaded();
        }

        var settled = samples.Skip(5).ToArray();
        Assert.That(settled, Is.Not.Empty);

        double medianBusy = median(settled.Select(s => s.BusyMilliseconds));
        double medianBlocked = median(settled.Select(s => s.BlockedMilliseconds));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(medianBlocked, Is.EqualTo(blocked_ms).Within(2), $"blocked time should track the wait, was {medianBlocked:F2}ms");
            Assert.That(medianBusy, Is.EqualTo(work_ms).Within(2), $"busy time should exclude the wait, was {medianBusy:F2}ms");
        }

        using (Assert.EnterMultipleScope())
        {
            // The frame occupied ~5ms of its 10ms budget, but only ~2ms of that was work. Charging the
            // wait to busy would have put it at half its budget instead of a fifth.
            Assert.That(medianBusy + medianBlocked, Is.EqualTo(work_ms + blocked_ms).Within(2));
            Assert.That(settled.Count(s => s.MissedDeadline), Is.Zero, "blocking on a device is not overrunning the budget");
        }
    }

    [Test]
    public void TestThreadsWithoutABlockingHookReportNone()
    {
        var thread = new AppThread("TestNoBlockingThread", () => spin(work_ms), () => target_hz);

        ThreadFrameSample[] samples;

        thread.StartMultiThreaded();

        try
        {
            samples = collect(thread, 10);
        }
        finally
        {
            thread.StopMultiThreaded();
        }

        Assert.That(samples, Is.Not.Empty);
        Assert.That(samples.Select(s => s.BlockedMilliseconds), Is.All.Zero);
    }

    [Test]
    public void TestRunSingleFrameRecordsTheBudgetItIsGiven()
    {
        var thread = new AppThread("TestSingleFrameThread", () => spin(work_ms), () => target_hz);

        // Single-threaded execution runs every thread once per main-loop iteration, so the budget is
        // the caller's, not the one this thread's own target rate would imply.
        const double shared_budget_ms = 4;

        thread.RunSingleFrame(shared_budget_ms);

        var destination = new ThreadFrameSample[4];
        long cursor = 0;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(thread.FrameStatistics.Drain(destination, ref cursor, out _), Is.EqualTo(1));
            Assert.That(destination[0].BudgetMilliseconds, Is.EqualTo(shared_budget_ms));
            Assert.That(destination[0].BusyMilliseconds, Is.GreaterThan(0));
        }
    }

    private static double median(IEnumerable<double> values)
    {
        double[] sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
