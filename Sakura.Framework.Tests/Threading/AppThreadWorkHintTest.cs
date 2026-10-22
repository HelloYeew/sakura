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
/// Covers what a platform's hint session is told: a hint session describes the thread that opens it, so it has to be
/// opened, fed and closed there, with every frame's target and work.
/// </summary>
[TestFixture]
public class AppThreadWorkHintTest
{
    [Test]
    public void TheThreadOpensFeedsAndClosesItsOwnSession()
    {
        int frameThread = -1;
        var hint = new RecordingHint();
        string? openedFor = null;
        double openedWith = 0;
        int openedOn = -1;

        var thread = new AppThread("HintedThread", () => frameThread = Environment.CurrentManagedThreadId, () => 200)
        {
            CreateWorkHint = (name, target) =>
            {
                openedFor = name;
                openedWith = target;
                openedOn = Environment.CurrentManagedThreadId;
                return hint;
            }
        };

        thread.StartMultiThreaded();

        try
        {
            var timeout = Stopwatch.StartNew();

            while (hint.Frames.Count < 10 && timeout.ElapsedMilliseconds < 5000)
                Thread.Sleep(5);
        }
        finally
        {
            thread.StopMultiThreaded();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(openedFor, Is.EqualTo("HintedThread"));
            Assert.That(openedWith, Is.EqualTo(5).Within(1e-9), "opened with the first frame's target");
            Assert.That(openedOn, Is.EqualTo(frameThread), "opened on the thread whose frames it reports");
            Assert.That(hint.Frames.Count, Is.GreaterThanOrEqualTo(10));
            Assert.That(hint.Frames.Select(f => f.Target), Is.All.EqualTo(5).Within(1e-9));
            Assert.That(hint.Frames.Select(f => f.Work), Is.All.GreaterThanOrEqualTo(0));
            Assert.That(hint.ReportedOn.Distinct(), Is.EqualTo(new[] { frameThread }), "fed on that thread");
            Assert.That(hint.DisposedOn, Is.EqualTo(frameThread), "closed on that thread when it stopped");
        }
    }

    [Test]
    public void ARunWithoutAHintFactoryStillRuns()
    {
        int frames = 0;
        var thread = new AppThread("UnhintedThread", () => Interlocked.Increment(ref frames), () => 200);

        thread.StartMultiThreaded();

        try
        {
            var timeout = Stopwatch.StartNew();

            while (Volatile.Read(ref frames) < 5 && timeout.ElapsedMilliseconds < 5000)
                Thread.Sleep(5);
        }
        finally
        {
            thread.StopMultiThreaded();
        }

        Assert.That(frames, Is.GreaterThanOrEqualTo(5));
    }

    private sealed class RecordingHint : IFrameWorkHint
    {
        public readonly ConcurrentQueue<(double Target, double Work)> Frames = new ConcurrentQueue<(double, double)>();
        public readonly ConcurrentQueue<int> ReportedOn = new ConcurrentQueue<int>();
        public int DisposedOn = -1;

        public void ReportFrame(double targetMilliseconds, double workMilliseconds)
        {
            Frames.Enqueue((targetMilliseconds, workMilliseconds));
            ReportedOn.Enqueue(Environment.CurrentManagedThreadId);
        }

        public void Dispose() => DisposedOn = Environment.CurrentManagedThreadId;
    }
}
