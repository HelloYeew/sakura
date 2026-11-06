// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Tests.Threading;

/// <summary>
/// Covers what macOS and iOS give a thread at each level: the priority it asked for, a work interval only where it
/// helps, and a timer that never wakes early. Each runs on a thread of its own, so the test runner's threads keep
/// their priorities.
/// </summary>
[TestFixture]
public class DarwinThreadPolicyTest
{
    [SetUp]
    public void SetUp()
    {
        Assume.That(DarwinThreadPolicy.IsAvailable, Is.True, "macOS and iOS only");
    }

    [Test]
    public void EachLevelGetsItsPriority()
    {
        var applied = onOwnThread(() =>
        {
            using var policy = new DarwinThreadPolicy(ThreadRole.Update, "PriorityTestThread", useWorkInterval: false);

            return Enum.GetValues<ThreadActivity>().Select(policy.Apply).ToArray();
        });

        Assert.That(withoutNice(applied), Is.EqualTo(new[] { "priority 47", "priority 38", "priority 37", "priority 31" }));
    }

    /// <summary>
    /// The audio thread keeps a player's music state moving while they're in another app.
    /// </summary>
    [Test]
    public void TheAudioThreadStaysAheadOfOtherAppsInTheBackground()
    {
        Assert.That(DarwinThreadPolicy.PriorityFor(ThreadRole.Audio, ThreadActivity.Background), Is.EqualTo(38));
    }

    [Test]
    public void AFrameThreadJoinsAWorkIntervalOnlyWhileFocused()
    {
        var applied = onOwnThread(() =>
        {
            using var policy = new DarwinThreadPolicy(ThreadRole.Draw, "IntervalTestThread", useWorkInterval: true);
            var results = new List<string>();

            foreach (var activity in new[] { ThreadActivity.Tight, ThreadActivity.Smooth, ThreadActivity.Background, ThreadActivity.Hidden, ThreadActivity.Tight })
            {
                results.Add(policy.Apply(activity));

                // A few frames at each, as a thread would run them.
                for (int i = 0; i < 3; i++)
                {
                    policy.BeginFrame(8.33);
                    Thread.SpinWait(1000);
                    policy.EndFrame();
                }
            }

            return results.ToArray();
        });

        Assert.That(withoutNice(applied), Is.EqualTo(new[]
        {
            "priority 47, work interval",
            "priority 38, work interval",
            "priority 37",
            "priority 31",
            "priority 47, work interval"
        }));
    }

    [Test]
    public void TheCriticalTimerNeverWakesEarly()
    {
        var sleptMicroseconds = onOwnThread(() =>
        {
            DarwinThreadPolicy.SetOwnPriority(47);

            using var sleep = DarwinCriticalSleep.Create();
            Assert.That(sleep, Is.Not.Null);

            var slept = new List<double>();

            for (int i = 0; i < 50; i++)
            {
                long start = Stopwatch.GetTimestamp();
                Assert.That(sleep!.Sleep(TimeSpan.FromMilliseconds(1)), Is.True);
                slept.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
            }

            return slept;
        });

        Assert.That(sleptMicroseconds, Is.All.GreaterThanOrEqualTo(1000));
    }

    /// <summary>
    /// Not a check: prints how late a plain sleep and the critical timer wake on this machine for a frame thread at
    /// <see cref="ThreadActivity.Tight"/>, for the waits a frame thread makes.
    /// Run with <c>dotnet test --filter Name=TestReportCriticalTimerAccuracy</c>.
    /// </summary>
    [Test]
    [Explicit("Measurement report, takes about a minute")]
    public void TestReportCriticalTimerAccuracy()
    {
        string report = onOwnThread(() =>
        {
            DarwinThreadPolicy.SetOwnPriority(DarwinThreadPolicy.PriorityFor(ThreadRole.Draw, ThreadActivity.Tight));

            using var plain = FrameWaiter.CreateNativeSleep();
            using var critical = DarwinCriticalSleep.Create();
            var lines = new List<string>
            {
                "lateness in microseconds   p50     p90     p99     max",
            };

            foreach (double waitMs in new[] { 1.0, 1000 / 240.0, 1000 / 120.0 })
            {
                foreach ((string name, INativeSleep sleep) in new[] { ("plain", plain!), ("critical", (INativeSleep)critical!) })
                {
                    var late = new List<double>();

                    for (int i = 0; i < 400; i++)
                    {
                        long start = Stopwatch.GetTimestamp();
                        sleep.Sleep(TimeSpan.FromMilliseconds(waitMs));
                        late.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds - waitMs * 1000);
                    }

                    lines.Add($"{waitMs,5:0.00} ms {name,-9} {percentile(late, 0.5),7:0} {percentile(late, 0.9),7:0} {percentile(late, 0.99),7:0} {late.Max(),7:0}");
                }
            }

            // A draw thread's loop at 120 Hz with a millisecond of work: the guard it learns is what it spins each frame.
            lines.Add(string.Empty);
            lines.Add("120 Hz frame loop, 1 ms of work   guard (spin a frame)   late p50   late p99");

            foreach ((string name, bool useCritical) in new[] { ("plain sleep + spin", false), ("critical timer + spin", true) })
            {
                using var waiter = new FrameWaiter(FrameWaiter.CreateNativeSleep(), useCritical ? DarwinCriticalSleep.Create() : null);
                var late = new List<double>();
                long period = Stopwatch.Frequency / 120;
                long next = Stopwatch.GetTimestamp();

                for (int frame = 0; frame < 2400; frame++)
                {
                    long workUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000;

                    while (Stopwatch.GetTimestamp() < workUntil)
                        Thread.SpinWait(1);

                    next = Math.Max(next + period, Stopwatch.GetTimestamp());
                    waiter.WaitUntil(next, precise: true);

                    // After the guard has had ten seconds to settle.
                    if (frame >= 1200)
                        late.Add(Stopwatch.GetElapsedTime(next).TotalMicroseconds);
                }

                lines.Add($"{name,-34} {waiter.GuardTicks * 1_000_000.0 / Stopwatch.Frequency,12:0} us {percentile(late, 0.5),10:0} {percentile(late, 0.99),10:0}");
            }

            return string.Join(Environment.NewLine, lines);
        }, 180_000);

        TestContext.Out.WriteLine(report);
    }

    /// <summary>
    /// Takes out what a nice value does to a fixed priority: the system lowers every one by it, so a test run with
    /// <c>nice</c> reads each back that much lower.
    /// </summary>
    private static string[] withoutNice(string[] applied)
    {
        int nice = getpriority(prio_process, 0);

        return applied.Select(a => Regex.Replace(a, @"priority (\d+)", m => $"priority {int.Parse(m.Groups[1].Value) + nice}")).ToArray();
    }

    private const int prio_process = 0;

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int getpriority(int which, int who);

    private static double percentile(List<double> values, double quantile)
    {
        double[] sorted = values.Order().ToArray();
        return sorted[(int)Math.Min(sorted.Length - 1, quantile * sorted.Length)];
    }

    /// <summary>
    /// Runs on a new thread and waits by polling rather than joining: joining would lend the waiting thread's priority
    /// to the one it waits on.
    /// </summary>
    private static T onOwnThread<T>(Func<T> work, int timeoutMilliseconds = 30_000)
    {
        T result = default!;
        Exception? failure = null;
        int done = 0;

        new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception e)
            {
                failure = e;
            }

            Volatile.Write(ref done, 1);
        }).Start();

        var timeout = Stopwatch.StartNew();

        while (Volatile.Read(ref done) == 0 && timeout.ElapsedMilliseconds < timeoutMilliseconds)
            Thread.Sleep(5);

        if (failure != null)
            throw failure;

        Assert.That(done, Is.EqualTo(1), "timed out");
        return result;
    }
}
