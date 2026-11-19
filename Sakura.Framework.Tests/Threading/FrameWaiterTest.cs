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

        public SleepResult Sleep(TimeSpan duration)
        {
            long until = Stopwatch.GetTimestamp() + (long)(duration.Ticks * Stopwatch.Frequency / (double)TimeSpan.TicksPerSecond) + lateTicks;

            while (Stopwatch.GetTimestamp() < until)
                Thread.SpinWait(1);

            return SleepResult.Slept;
        }

        public void Wake()
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Stands in for a platform sleep that returns at once, recording how long it was asked to sleep.
    /// </summary>
    private class RecordingSleep : INativeSleep
    {
        public readonly List<double> RequestedMicroseconds = new List<double>();
        private readonly bool succeeds;

        public RecordingSleep(bool succeeds = true)
        {
            this.succeeds = succeeds;
        }

        public SleepResult Sleep(TimeSpan duration)
        {
            RequestedMicroseconds.Add(duration.TotalMicroseconds);
            return succeeds ? SleepResult.Slept : SleepResult.Failed;
        }

        public void Wake()
        {
        }

        public void Dispose()
        {
        }
    }

    [Test]
    public void TestPlainWaitUsesThePlainSleep()
    {
        var plain = new RecordingSleep();
        var precise = new RecordingSleep();

        using (var waiter = new FrameWaiter(plain, precise))
            waiter.WaitUntil(Stopwatch.GetTimestamp() + ticks(2000), precise: false);

        Assert.That(plain.RequestedMicroseconds, Has.Count.EqualTo(1));
        Assert.That(precise.RequestedMicroseconds, Is.Empty);
    }

    [Test]
    public void TestPreciseWaitSleepsUpToTheGuardOnThePreciseSleep()
    {
        var plain = new RecordingSleep();
        var precise = new RecordingSleep();

        using (var waiter = new FrameWaiter(plain, precise))
            waiter.WaitUntil(Stopwatch.GetTimestamp() + ticks(2000), precise: true);

        Assert.That(plain.RequestedMicroseconds, Is.Empty);
        Assert.That(precise.RequestedMicroseconds.Single(), Is.EqualTo(2000 - 100).Within(50), "the wait less the starting guard");
    }

    /// <summary>
    /// A phone doesn't spin, but still gains from the more precise timer.
    /// </summary>
    [Test]
    public void TestPreciseWaitWithoutSpinningSleepsTheWholeWaitOnThePreciseSleep()
    {
        var plain = new RecordingSleep();
        var precise = new RecordingSleep();
        double guardBefore;
        double guardAfter;

        using (var waiter = new FrameWaiter(plain, precise, allowSpinning: false))
        {
            guardBefore = waiter.GuardTicks;
            waiter.WaitUntil(Stopwatch.GetTimestamp() + ticks(2000), precise: true);
            guardAfter = waiter.GuardTicks;
        }

        Assert.That(plain.RequestedMicroseconds, Is.Empty);
        Assert.That(precise.RequestedMicroseconds.Single(), Is.EqualTo(2000).Within(100));
        Assert.That(guardAfter, Is.EqualTo(guardBefore), "nothing spun, so there's nothing to learn");
    }

    [Test]
    public void TestAPreciseSleepThatFailsFallsBackToThePlainOne()
    {
        var plain = new RecordingSleep();
        var precise = new RecordingSleep(succeeds: false);

        using (var waiter = new FrameWaiter(plain, precise, allowSpinning: false))
            waiter.WaitUntil(Stopwatch.GetTimestamp() + ticks(2000), precise: true);

        Assert.That(plain.RequestedMicroseconds.Single(), Is.EqualTo(precise.RequestedMicroseconds.Single()));
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
    /// A wake from another thread ends a two-second wait at once, on this platform's own sleeps, and the shortened sleep
    /// teaches the guard nothing.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    [FlakyTest]
    public void AWakeEndsAWaitEarly(bool precise)
    {
        using var waiter = new FrameWaiter("TestWakeThread");
        double guardBefore = waiter.GuardTicks;
        long wokenAt = 0;

        var waker = new Thread(() =>
        {
            Thread.Sleep(50);
            Interlocked.Exchange(ref wokenAt, Stopwatch.GetTimestamp());
            waiter.Wake();
        });

        long start = Stopwatch.GetTimestamp();
        waker.Start();
        bool reachedDeadline = waiter.WaitUntil(start + ticks(2_000_000), precise);
        long returnedAt = Stopwatch.GetTimestamp();
        waker.Join();

        Assert.That(reachedDeadline, Is.False);
        Assert.That(microseconds(returnedAt - start), Is.GreaterThanOrEqualTo(50_000), "returned before the wake");
        Assert.That(microseconds(returnedAt - Interlocked.Read(ref wokenAt)), Is.LessThan(100_000), "the wait ended long after the wake");
        Assert.That(waiter.GuardTicks, Is.EqualTo(guardBefore));
    }

    /// <summary>
    /// A wake that comes while the thread is busy, before it waits, ends its next wait.
    /// </summary>
    [Test]
    public void AWakeBeforeTheWaitIsNotLost()
    {
        using var waiter = new FrameWaiter("TestWakeThread");

        waiter.Wake();

        long start = Stopwatch.GetTimestamp();
        Assert.That(waiter.WaitUntil(start + ticks(2_000_000), precise: false), Is.False);
        Assert.That(microseconds(Stopwatch.GetTimestamp() - start), Is.LessThan(1_000_000));
    }

    /// <summary>
    /// Two wakes before a wait end only that one. The wake both platform sleeps were given stays with the one that wasn't
    /// sleeping, and the next wait sleeps through it to its deadline.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void AWaitAfterAWokenOneLastsToItsDeadline(bool precise)
    {
        using var waiter = new FrameWaiter("TestWakeThread");

        waiter.Wake();
        waiter.Wake();
        Assert.That(waiter.WaitUntil(Stopwatch.GetTimestamp() + ticks(2_000_000), precise), Is.False);

        long deadline = Stopwatch.GetTimestamp() + ticks(50_000);
        Assert.That(waiter.WaitUntil(deadline, precise), Is.True);
        Assert.That(Stopwatch.GetTimestamp(), Is.GreaterThanOrEqualTo(deadline));
    }

    /// <summary>
    /// A precise sleep that returns at once leaves the whole wait to the spin, which a wake ends too.
    /// </summary>
    [Test]
    [FlakyTest]
    public void AWakeEndsTheSpin()
    {
        using var waiter = new FrameWaiter(new RecordingSleep(), new RecordingSleep());

        var waker = new Thread(() =>
        {
            Thread.Sleep(50);
            waiter.Wake();
        });

        long start = Stopwatch.GetTimestamp();
        waker.Start();
        bool reachedDeadline = waiter.WaitUntil(start + ticks(2_000_000), precise: true);
        double waitedMicroseconds = microseconds(Stopwatch.GetTimestamp() - start);
        waker.Join();

        Assert.That(reachedDeadline, Is.False);
        Assert.That(waitedMicroseconds, Is.LessThan(1_000_000));
    }

    /// <summary>
    /// The sleeps a frame thread gets on macOS, Linux and Windows can be woken: a wake ends a sleep of either kind.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void ThePlatformsSleepsCanBeWoken(bool precise)
    {
        Assume.That(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows(), "only these three are checked");

        using var sleep = precise ? FrameWaiter.CreatePreciseSleep() : FrameWaiter.CreateNativeSleep();
        Assume.That(sleep, Is.Not.Null, "the platform has no sleep of this kind");

        sleep!.Wake();

        long start = Stopwatch.GetTimestamp();
        Assert.That(sleep.Sleep(TimeSpan.FromSeconds(2)), Is.EqualTo(SleepResult.Woken));
        Assert.That(microseconds(Stopwatch.GetTimestamp() - start), Is.LessThan(1_000_000));
    }

    /// <summary>
    /// A woken sleep's timer would otherwise still fire at its old time, into whichever sleep comes next, and end that one
    /// early.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void AWokenSleepLeavesNoTimerForTheNext(bool precise)
    {
        Assume.That(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows(), "only these three are checked");

        using var sleep = precise ? FrameWaiter.CreatePreciseSleep() : FrameWaiter.CreateNativeSleep();
        Assume.That(sleep, Is.Not.Null, "the platform has no sleep of this kind");

        sleep!.Wake();
        Assert.That(sleep.Sleep(TimeSpan.FromMilliseconds(30)), Is.EqualTo(SleepResult.Woken));

        // Past the first sleep's deadline, so its timer, were it still set, would already have fired.
        Thread.Sleep(60);

        long start = Stopwatch.GetTimestamp();
        Assert.That(sleep.Sleep(TimeSpan.FromMilliseconds(100)), Is.EqualTo(SleepResult.Slept));
        Assert.That(microseconds(Stopwatch.GetTimestamp() - start), Is.GreaterThanOrEqualTo(95_000));
    }

    /// <summary>
    /// A wake can come from another thread as the waiting thread's loop ends and closes the waiter.
    /// </summary>
    [Test]
    public void AWakeAfterDisposeDoesNothing()
    {
        var waiter = new FrameWaiter("TestWakeThread");
        waiter.Dispose();

        Assert.DoesNotThrow(() => waiter.Wake());
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
