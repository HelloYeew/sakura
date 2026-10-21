// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Tests.Threading;

/// <summary>
/// Pausing has to leave nothing running, not just stop new frames from starting: what comes after it (taking the
/// window's surface away) races any frame still in progress.
/// </summary>
[TestFixture]
public class ThreadRunnerPauseTest
{
    private const int frame_milliseconds = 30;

    private sealed class CountingThread
    {
        public readonly AppThread Thread;

        private int frames;
        private int inFrame;

        public int Frames => Volatile.Read(ref frames);
        public bool InFrame => Volatile.Read(ref inFrame) != 0;

        public CountingThread(string name)
        {
            Thread = new AppThread(name, frame, () => 200);
        }

        private void frame()
        {
            Volatile.Write(ref inFrame, 1);
            System.Threading.Thread.Sleep(frame_milliseconds);
            Interlocked.Increment(ref frames);
            Volatile.Write(ref inFrame, 0);
        }
    }

    private CountingThread update = null!;
    private CountingThread draw = null!;
    private CountingThread audio = null!;
    private ThreadRunner runner = null!;

    [SetUp]
    public void SetUp()
    {
        update = new CountingThread("Update");
        draw = new CountingThread("Draw");
        audio = new CountingThread("Audio");
        runner = new ThreadRunner(update.Thread, draw.Thread, audio.Thread);
    }

    [TearDown]
    public void TearDown() => runner.Dispose();

    private static void waitFor(Func<bool> condition, string what)
    {
        var timeout = Stopwatch.StartNew();

        while (!condition())
        {
            if (timeout.ElapsedMilliseconds > 5000)
                Assert.Fail($"Timed out waiting for {what}.");

            Thread.Sleep(1);
        }
    }

    private int totalFrames => update.Frames + draw.Frames + audio.Frames;

    private bool anyInFrame => update.InFrame || draw.InFrame || audio.InFrame;

    [Test]
    public void MultiThreadedPauseWaitsForTheFramesInProgress()
    {
        runner.SetExecutionMode(ExecutionMode.MultiThread);
        waitFor(() => draw.InFrame, "a draw frame to start");

        runner.Pause();

        Assert.That(anyInFrame, Is.False, "a frame was still running when the pause returned");

        int frames = totalFrames;
        Thread.Sleep(frame_milliseconds * 4);
        Assert.That(totalFrames, Is.EqualTo(frames), "frames ran while paused");

        runner.Resume();
        waitFor(() => totalFrames > frames, "frames to run again after resuming");
    }

    [Test]
    public void SingleThreadedPauseWaitsForTheFrameInProgress()
    {
        var frameTask = Task.Run(() => runner.RunSingleThreadedFrame());
        waitFor(() => anyInFrame, "the single-threaded frame to start");

        runner.Pause();

        Assert.That(anyInFrame, Is.False, "the frame was still running when the pause returned");
        frameTask.Wait();

        int frames = totalFrames;
        runner.RunSingleThreadedFrame();
        Assert.That(totalFrames, Is.EqualTo(frames), "a frame ran while paused");

        runner.Resume();
        runner.RunSingleThreadedFrame();
        Assert.That(totalFrames, Is.EqualTo(frames + 3), "the frame didn't run after resuming");
    }

    [Test]
    public void ThreadsStartedWhilePausedWaitForResume()
    {
        runner.Pause();
        runner.SetExecutionMode(ExecutionMode.MultiThread);

        Thread.Sleep(frame_milliseconds * 4);
        Assert.That(totalFrames, Is.Zero, "threads started while paused ran frames");

        runner.Resume();
        waitFor(() => totalFrames > 0, "frames to run after resuming");
    }

    [Test]
    public void WaitingOnAThreadThatIsNotRunningReturnsAtOnce()
    {
        var thread = new AppThread("Idle", () => { }, () => 60);
        thread.PauseMultiThreaded();

        Assert.That(thread.WaitUntilPaused(TimeSpan.FromMilliseconds(10)), Is.True);
    }
}
