// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Platform;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Tests.Threading;

/// <summary>
/// Covers how a thread applies the host's level to itself: on the thread, before its first frame, and again only when
/// the level changes, with every frame bracketed for the platform.
/// </summary>
[TestFixture]
public class AppThreadActivityTest
{
    [Test]
    public void ThePolicyIsMadeUsedAndClosedOnTheThread()
    {
        int frameThread = -1;
        var policy = new RecordingPolicy();
        int createdOn = -1;

        var thread = new AppThread("PolicyThread", () => frameThread = Environment.CurrentManagedThreadId, () => 200)
        {
            CreatePolicy = () =>
            {
                createdOn = Environment.CurrentManagedThreadId;
                return policy;
            }
        };

        runUntil(thread, () => policy.Frames >= 10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(createdOn, Is.EqualTo(frameThread), "made on the thread it applies to");
            Assert.That(policy.AppliedOn.Distinct(), Is.EqualTo(new[] { frameThread }), "applied on that thread");
            Assert.That(policy.DisposedOn, Is.EqualTo(frameThread), "closed on that thread when it stopped");
        }
    }

    [Test]
    public void ALevelIsAppliedBeforeTheFirstFrameAndThenOnlyWhenItChanges()
    {
        var policy = new RecordingPolicy();
        int activity = (int)ThreadActivity.Tight;

        var thread = new AppThread("ChangingThread", () => { }, () => 200)
        {
            GetActivity = () => (ThreadActivity)Volatile.Read(ref activity),
            CreatePolicy = () => policy
        };

        thread.StartMultiThreaded();

        try
        {
            waitFor(() => policy.Frames >= 5);
            Assert.That(policy.Applied.First().BeforeFrame, Is.EqualTo(0), "applied before the first frame");

            Volatile.Write(ref activity, (int)ThreadActivity.Smooth);
            int changedAt = policy.Frames;
            waitFor(() => policy.Frames >= changedAt + 5);

            Volatile.Write(ref activity, (int)ThreadActivity.Tight);
            changedAt = policy.Frames;
            waitFor(() => policy.Frames >= changedAt + 5);
        }
        finally
        {
            thread.StopMultiThreaded();
        }

        Assert.That(policy.Applied.Select(a => a.Activity), Is.EqualTo(new[] { ThreadActivity.Tight, ThreadActivity.Smooth, ThreadActivity.Tight }));
    }

    [Test]
    public void EveryFrameIsBracketedWithItsPeriod()
    {
        var policy = new RecordingPolicy();
        var thread = new AppThread("BracketedThread", () => policy.FrameRan(), () => 200)
        {
            CreatePolicy = () => policy
        };

        runUntil(thread, () => policy.Frames >= 10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(policy.Periods, Is.All.EqualTo(5).Within(1e-9), "the period of a 200 Hz thread");
            Assert.That(policy.UnbracketedFrames, Is.Zero, "every frame ran between its begin and end");
            Assert.That(policy.Ends, Is.EqualTo(policy.Frames).Within(1), "every begun frame ended");
        }
    }

    /// <summary>
    /// Switching between single- and multi-threaded stops the loop and later starts a new thread, which has none of
    /// the old one's settings.
    /// </summary>
    [Test]
    public void ARestartedThreadAppliesItsLevelAgain()
    {
        var policies = new ConcurrentQueue<RecordingPolicy>();
        var thread = new AppThread("RestartedThread", () => { }, () => 200)
        {
            CreatePolicy = () =>
            {
                var policy = new RecordingPolicy();
                policies.Enqueue(policy);
                return policy;
            }
        };

        runUntil(thread, () => policies.Count == 1 && policies.Single().Frames >= 3);
        runUntil(thread, () => policies.Count == 2 && policies.Last().Frames >= 3);

        Assert.That(policies.Select(p => p.Applied.Count), Is.EqualTo(new[] { 1, 1 }));
    }

    /// <summary>
    /// A phone's app pauses its threads while it's away, and the system may drop what it was told meanwhile: on resuming,
    /// the thread tells its policy and its work hint the level again, though the level didn't change.
    /// </summary>
    [Test]
    public void AThreadResumedAfterAPauseAppliesItsLevelAgain()
    {
        var policy = new RecordingPolicy();
        var hint = new RecordingHint();
        var thread = new AppThread("PausedThread", () => { }, () => 200)
        {
            CreatePolicy = () => policy,
            CreateWorkHint = (_, _) => hint
        };

        thread.StartMultiThreaded();

        try
        {
            waitFor(() => policy.Frames >= 5);

            thread.PauseMultiThreaded();
            Assert.That(thread.WaitUntilPaused(TimeSpan.FromSeconds(5)), Is.True);
            thread.ResumeMultiThreaded();

            int resumedAt = policy.Frames;
            waitFor(() => policy.Frames >= resumedAt + 5);
        }
        finally
        {
            thread.StopMultiThreaded();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(policy.Applied.Select(a => a.Activity), Is.EqualTo(new[] { ThreadActivity.Tight, ThreadActivity.Tight }));
            Assert.That(hint.Activities, Is.EqualTo(new[] { ThreadActivity.Tight, ThreadActivity.Tight }), "the work hint is told too");
        }
    }

    private static void runUntil(AppThread thread, Func<bool> condition)
    {
        thread.StartMultiThreaded();

        try
        {
            waitFor(condition);
        }
        finally
        {
            thread.StopMultiThreaded();
        }
    }

    private static void waitFor(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();

        while (!condition() && timeout.ElapsedMilliseconds < 5000)
            Thread.Sleep(5);

        Assert.That(condition(), Is.True, "timed out");
    }

    private sealed class RecordingHint : IFrameWorkHint
    {
        public readonly ConcurrentQueue<ThreadActivity> Activities = new ConcurrentQueue<ThreadActivity>();

        public void ReportFrame(double targetMilliseconds, double workMilliseconds)
        {
        }

        public void SetActivity(ThreadActivity activity) => Activities.Enqueue(activity);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingPolicy : IThreadPolicy
    {
        public readonly ConcurrentQueue<(ThreadActivity Activity, int BeforeFrame)> Applied = new ConcurrentQueue<(ThreadActivity, int)>();
        public readonly ConcurrentQueue<int> AppliedOn = new ConcurrentQueue<int>();
        public readonly ConcurrentQueue<double> Periods = new ConcurrentQueue<double>();
        public int DisposedOn = -1;
        public int UnbracketedFrames;
        public int Ends;

        private int frames;
        private bool inFrame;

        public int Frames => Volatile.Read(ref frames);

        public string Apply(ThreadActivity activity)
        {
            Applied.Enqueue((activity, Frames));
            AppliedOn.Enqueue(Environment.CurrentManagedThreadId);
            return $"recorded {activity}";
        }

        public void BeginFrame(double frameMilliseconds)
        {
            Periods.Enqueue(frameMilliseconds);
            inFrame = true;
            Interlocked.Increment(ref frames);
        }

        public void EndFrame()
        {
            inFrame = false;
            Interlocked.Increment(ref Ends);
        }

        public void FrameRan()
        {
            if (!inFrame)
                Interlocked.Increment(ref UnbracketedFrames);
        }

        public void Dispose() => DisposedOn = Environment.CurrentManagedThreadId;
    }
}
