// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Statistic;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Tests.Threading;

/// <summary>
/// Covers a thread whose frame waits for the next one itself, as a draw that blocks on the display does: it doesn't wait on
/// its own timer as well, and still records its target's budget.
/// </summary>
[TestFixture]
public class AppThreadPacedByFrameActionTest
{
    /// <summary>
    /// Half a second between frames by the timer, so a frame that waited for it would show.
    /// </summary>
    private const double slow_hz = 2;

    /// <summary>
    /// What the frame waits for itself, in place of the display.
    /// </summary>
    private const int frame_wait_ms = 10;

    [Test]
    [FlakyTest]
    public void AFramePacedByItselfSkipsTheTimer()
    {
        int frames = 0;

        var thread = new AppThread("PacedThread", () =>
        {
            Thread.Sleep(frame_wait_ms);
            Interlocked.Increment(ref frames);
        }, () => slow_hz)
        {
            PacedByFrameAction = static () => true
        };

        thread.StartMultiThreaded();
        Thread.Sleep(500);
        thread.StopMultiThreaded();

        var samples = new ThreadFrameSample[ThreadFrameStatistics.CAPACITY];
        long cursor = 0;
        int drained = thread.FrameStatistics.Drain(samples, ref cursor, out _);

        // By the timer, two frames at most; by the frame's own wait, dozens.
        Assert.That(Volatile.Read(ref frames), Is.GreaterThan(10));
        Assert.That(samples.Take(drained).Select(sample => sample.BudgetMilliseconds), Is.All.EqualTo(1000 / slow_hz), "the budget is the target's, not the frame's");
    }

    [Test]
    public void AFrameNotPacedByItselfWaitsForTheTimer()
    {
        int frames = 0;

        var thread = new AppThread("TimedThread", () =>
        {
            Thread.Sleep(frame_wait_ms);
            Interlocked.Increment(ref frames);
        }, () => slow_hz);

        var running = Stopwatch.StartNew();
        thread.StartMultiThreaded();
        Thread.Sleep(300);
        thread.StopMultiThreaded();

        Assert.That(Volatile.Read(ref frames), Is.LessThanOrEqualTo(1 + (int)Math.Ceiling(running.Elapsed.TotalSeconds * slow_hz)));
    }
}
