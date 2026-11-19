// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Tests.Threading;

/// <summary>
/// Covers a thread woken between frames: it runs what it was woken for at once, on itself, and goes back to waiting for the
/// same frame rather than starting one early.
/// </summary>
[TestFixture]
public class AppThreadWakeTest
{
    /// <summary>
    /// Half a second between frames, so a wake that waited for the next frame would show.
    /// </summary>
    private const double slow_hz = 2;

    [Test]
    [FlakyTest]
    public void AWakeRunsOnWakeAtOnceOnTheThreadAndStartsNoFrame()
    {
        int frames = 0;
        int wakes = 0;
        int frameThread = -1;
        int wakeThread = -1;

        var thread = new AppThread("WakeThread", () =>
        {
            frameThread = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref frames);
        }, () => slow_hz)
        {
            OnWake = () =>
            {
                wakeThread = Environment.CurrentManagedThreadId;
                Interlocked.Increment(ref wakes);
            }
        };

        thread.StartMultiThreaded();

        try
        {
            waitFor(() => Volatile.Read(ref frames) >= 1);

            // Into its wait for the second frame, which is still most of half a second away.
            Thread.Sleep(50);
            int framesBefore = Volatile.Read(ref frames);

            var sinceWake = Stopwatch.StartNew();
            thread.Wake();
            waitFor(() => Volatile.Read(ref wakes) >= 1);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sinceWake.Elapsed.TotalMilliseconds, Is.LessThan(200), "ran at the next frame, not at the wake");
                Assert.That(Volatile.Read(ref frames), Is.EqualTo(framesBefore), "a frame started early");
                Assert.That(wakeThread, Is.EqualTo(frameThread), "ran on another thread");
            }
        }
        finally
        {
            thread.StopMultiThreaded();
        }
    }

    /// <summary>
    /// A press that arrives while the frame is running, after the frame took its input, is dispatched as the frame ends.
    /// </summary>
    [Test]
    [FlakyTest]
    public void AWakeDuringAFrameRunsOnWakeAsTheFrameEnds()
    {
        int frames = 0;
        int wakes = 0;
        int framesAtWake = -1;
        using var inFrame = new ManualResetEventSlim();
        using var finishFrame = new ManualResetEventSlim();

        var thread = new AppThread("BusyWakeThread", () =>
        {
            inFrame.Set();
            finishFrame.Wait(TimeSpan.FromSeconds(5));
            Interlocked.Increment(ref frames);
        }, () => slow_hz)
        {
            OnWake = () =>
            {
                framesAtWake = Volatile.Read(ref frames);
                Interlocked.Increment(ref wakes);
            }
        };

        thread.StartMultiThreaded();

        try
        {
            Assert.That(inFrame.Wait(TimeSpan.FromSeconds(5)), Is.True, "the first frame never started");

            thread.Wake();
            var sinceFrameEnd = Stopwatch.StartNew();
            finishFrame.Set();
            waitFor(() => Volatile.Read(ref wakes) >= 1);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sinceFrameEnd.Elapsed.TotalMilliseconds, Is.LessThan(200), "waited for the next frame");
                Assert.That(framesAtWake, Is.EqualTo(1), "ran after another frame rather than straight after the first");
            }
        }
        finally
        {
            finishFrame.Set();
            thread.StopMultiThreaded();
        }
    }

    /// <summary>
    /// Single-threaded, or before it starts, there's no wait to cut short, and the wake is dropped.
    /// </summary>
    [Test]
    public void AWakeOnAThreadNotRunningOnItsOwnDoesNothing()
    {
        int wakes = 0;

        var thread = new AppThread("IdleThread", () => { }, () => 60)
        {
            OnWake = () => Interlocked.Increment(ref wakes)
        };

        thread.Wake();
        thread.RunSingleFrame();

        Assert.That(wakes, Is.Zero);
    }

    /// <summary>
    /// A thread asked to stop while it waits for a slow frame stops then, rather than at the frame.
    /// </summary>
    [Test]
    [FlakyTest]
    public void StoppingAWaitingThreadEndsItsWait()
    {
        int frames = 0;
        var thread = new AppThread("StoppingThread", () => Interlocked.Increment(ref frames), () => 0.5);

        thread.StartMultiThreaded();
        waitFor(() => Volatile.Read(ref frames) >= 1);
        Thread.Sleep(50);

        var stopping = Stopwatch.StartNew();
        thread.StopMultiThreaded();

        Assert.That(stopping.Elapsed.TotalMilliseconds, Is.LessThan(1000), "waited out the frame, two seconds long");
    }

    private static void waitFor(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();

        while (!condition() && timeout.ElapsedMilliseconds < 5000)
        {
            Thread.Sleep(1);
        }

        Assert.That(condition(), Is.True, "timed out");
    }
}
