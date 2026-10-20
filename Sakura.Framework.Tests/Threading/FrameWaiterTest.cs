// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Statistic;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Tests.Threading;

public class FrameWaiterTest
{
    private static long ticks(double microseconds) => (long)(microseconds * Stopwatch.Frequency / 1_000_000.0);

    private static double microseconds(double ticks) => ticks * 1_000_000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Stands in for a platform sleep that always wakes a fixed time late. Spins rather than sleeps so the
    /// lateness is exactly what the test asked for, not whatever the machine running it adds.
    /// </summary>
    private class LateSleep : INativeSleep
    {
        private readonly long lateTicks;

        public LateSleep(double lateMicroseconds)
        {
            lateTicks = ticks(lateMicroseconds);
        }

        public bool Sleep(TimeSpan duration)
        {
            long until = Stopwatch.GetTimestamp() + (long)(duration.Ticks * Stopwatch.Frequency / (double)TimeSpan.TicksPerSecond) + lateTicks;

            while (Stopwatch.GetTimestamp() < until)
                Thread.SpinWait(1);

            return true;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Settles from either side, since the starting guard sits between the two platforms it has to serve:
    /// above what macOS needs and below what Windows needs.
    /// </summary>
    [TestCase(100, Description = "on-time sleeps, like nanosleep")]
    [TestCase(1500, Description = "late sleeps, like a loaded Windows timer")]
    public void TestGuardSettlesOnTheNinetyNinthPercentile(double maxOvershootMicroseconds)
    {
        var waiter = new FrameWaiter(nativeSleep: null);
        var random = new Random(1234);

        for (int i = 0; i < 50_000; i++)
            waiter.RecordOvershoot(ticks(random.NextDouble() * maxOvershootMicroseconds));

        Assert.That(microseconds(waiter.GuardTicks), Is.EqualTo(maxOvershootMicroseconds * 0.99).Within(50));
    }

    [Test]
    public void TestOneOutlierMovesTheGuardOnlyOneStep()
    {
        var waiter = new FrameWaiter(nativeSleep: null);
        var random = new Random(1234);

        for (int i = 0; i < 50_000; i++)
            waiter.RecordOvershoot(ticks(random.NextDouble() * 100));

        double settled = microseconds(waiter.GuardTicks);

        // A preemption or a GC suspension landing in one sleep.
        waiter.RecordOvershoot(ticks(10_000));

        Assert.That(microseconds(waiter.GuardTicks) - settled, Is.LessThanOrEqualTo(20));
    }

    [Test]
    public void TestGuardIsCappedWhenSleepIsUseless()
    {
        var waiter = new FrameWaiter(nativeSleep: null);

        for (int i = 0; i < 10_000; i++)
            waiter.RecordOvershoot(ticks(15_600));

        Assert.That(microseconds(waiter.GuardTicks), Is.EqualTo(2000).Within(1));
    }

    [Test]
    public void TestPreciseWaitNeverReturnsEarlyAndLearnsTheLateness()
    {
        // Late by more than the starting guard, the case that used to turn into late frames.
        const double late_us = 800;

        using var waiter = new FrameWaiter(new LateSleep(late_us));

        for (int i = 0; i < 200; i++)
        {
            long deadline = Stopwatch.GetTimestamp() + ticks(2000);

            waiter.WaitUntil(deadline, precise: true);

            Assert.That(Stopwatch.GetTimestamp(), Is.GreaterThanOrEqualTo(deadline), $"wait {i} returned early");
        }

        Assert.That(microseconds(waiter.GuardTicks), Is.GreaterThanOrEqualTo(late_us - 20));
    }

    [Test]
    public void TestImpreciseWaitLeavesTheGuardAlone()
    {
        using var waiter = new FrameWaiter(new LateSleep(800));
        double before = waiter.GuardTicks;

        for (int i = 0; i < 20; i++)
            waiter.WaitUntil(Stopwatch.GetTimestamp() + ticks(1000), precise: false);

        Assert.That(waiter.GuardTicks, Is.EqualTo(before));
    }

    /// <summary>
    /// The audio thread and the multi-threaded input loop never spin, so they should not list a guard
    /// that would sit at its starting value forever and read as if it were in use.
    /// </summary>
    [Test]
    public void TestGuardIsListedOnlyOnceTheThreadSpins()
    {
        const string name = "TestGuardListingThread";

        using var waiter = new FrameWaiter(name);

        waiter.WaitUntil(Stopwatch.GetTimestamp() + ticks(1000), precise: false);
        Assert.That(isListed(), Is.False, "listed after an imprecise wait");

        waiter.WaitUntil(Stopwatch.GetTimestamp() + ticks(2000), precise: true);
        Assert.That(isListed(), Is.True, "not listed after a precise wait");

        static bool isListed()
        {
            foreach (var stat in GlobalStatistics.GetStatistics())
            {
                if (stat.Group == "Threading" && stat.Name == $"{name} Sleep Guard")
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Not a check: prints how late this machine's <see cref="INativeSleep"/> wakes, and how close a
    /// 1000 Hz <see cref="FrameWaiter"/> lands, so platforms can be compared on real hardware.
    /// Run with <c>dotnet test --filter Name=TestReportPlatformSleepAccuracy</c>.
    /// </summary>
    [Test]
    [Explicit("Measurement report, takes several seconds")]
    public void TestReportPlatformSleepAccuracy()
    {
        using var sleeper = FrameWaiter.CreateNativeSleep();
        Assume.That(sleeper, Is.Not.Null, "no native sleep on this platform");

        TestContext.Out.WriteLine($"{sleeper!.GetType().Name}, overshoot in microseconds");
        TestContext.Out.WriteLine("request      p50      p90      p99      max");

        foreach (double requestUs in new[] { 50.0, 100, 250, 500, 1000, 2000 })
        {
            var overshoots = new List<double>();

            for (int i = 0; i < 500; i++)
            {
                long start = Stopwatch.GetTimestamp();
                sleeper.Sleep(TimeSpan.FromTicks((long)(requestUs * 10)));
                overshoots.Add(microseconds(Stopwatch.GetTimestamp() - start) - requestUs);
            }

            TestContext.Out.WriteLine($"{requestUs,7:0}  {percentile(overshoots, 0.5),7:0}  {percentile(overshoots, 0.9),7:0}  {percentile(overshoots, 0.99),7:0}  {overshoots.Max(),7:0}");
        }

        using var waiter = new FrameWaiter("Report");
        var lateness = new List<double>();
        long period = ticks(1000);
        long next = Stopwatch.GetTimestamp();

        for (int i = 0; i < 3000; i++)
        {
            // Paced the way AppThread paces: a late frame resets the cadence rather than being caught up.
            next = Math.Max(next + period, Stopwatch.GetTimestamp());
            waiter.WaitUntil(next, precise: true);

            if (i >= 500)
                lateness.Add(microseconds(Stopwatch.GetTimestamp() - next));
        }

        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine($"1000 Hz FrameWaiter: guard {microseconds(waiter.GuardTicks):0}us, lateness p50 {percentile(lateness, 0.5):0}us, p99 {percentile(lateness, 0.99):0}us, max {lateness.Max():0}us");
    }

    private static double percentile(List<double> values, double quantile)
    {
        double[] sorted = values.Order().ToArray();
        return sorted[(int)Math.Min(sorted.Length - 1, quantile * sorted.Length)];
    }
}
