// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Threading;

public class ThreadRunner : IDisposable
{
    private readonly AppThread updateThread;
    private readonly AppThread drawThread;
    private readonly AppThread audioThread;

    public ExecutionMode CurrentMode { get; private set; }

    /// <summary>
    /// Held for the length of each single-threaded frame, so <see cref="Pause"/> can wait for one in progress.
    /// </summary>
    private readonly object singleThreadedFrameLock = new object();

    private volatile bool paused;

    /// <summary>
    /// How long <see cref="Pause"/> waits for a thread to finish its frame. The system asks for a pause on its
    /// own thread and stops waiting for the app not long after (an Android activity going to the background).
    /// </summary>
    private static readonly TimeSpan pause_timeout = TimeSpan.FromSeconds(2);

    public ThreadRunner(AppThread updateThread, AppThread drawThread, AppThread audioThread)
    {
        this.updateThread = updateThread;
        this.drawThread = drawThread;
        this.audioThread = audioThread;
        CurrentMode = ExecutionMode.SingleThread;
    }

    public void SetExecutionMode(ExecutionMode mode)
    {
        if (CurrentMode == mode) return;

        if (mode == ExecutionMode.MultiThread)
        {
            // Started paused when the runner is, so none of them gets a frame in before it should.
            if (paused)
            {
                audioThread.PauseMultiThreaded();
                updateThread.PauseMultiThreaded();
                drawThread.PauseMultiThreaded();
            }

            audioThread.StartMultiThreaded();
            updateThread.StartMultiThreaded();
            drawThread.StartMultiThreaded();
        }
        else
        {
            audioThread.StopMultiThreaded();
            updateThread.StopMultiThreaded();
            drawThread.StopMultiThreaded();
        }

        CurrentMode = mode;
    }

    /// <param name="budgetMilliseconds">
    /// The main loop's frame budget, or 0 if unbounded. All three threads run once per
    /// iteration here, so they share it rather than each is having their own.
    /// </param>
    /// <param name="deadlineMilliseconds">
    /// The presentation deadline in effect, or 0 if there is none. Shared for the same reason, and
    /// the same figure the threads use in multi-threaded mode.
    /// </param>
    public void RunSingleThreadedFrame(double budgetMilliseconds = 0, double deadlineMilliseconds = 0)
    {
        lock (singleThreadedFrameLock)
        {
            if (CurrentMode != ExecutionMode.SingleThread || paused) return;

            audioThread.RunSingleFrame(budgetMilliseconds, deadlineMilliseconds);
            updateThread.RunSingleFrame(budgetMilliseconds, deadlineMilliseconds);
            drawThread.RunSingleFrame(budgetMilliseconds, deadlineMilliseconds);
        }
    }

    /// <summary>
    /// Stops audio, update and draw at the end of their current frames, and waits until they have, so the
    /// caller can take away what they use (the window's surface) without racing them. Nothing runs again
    /// until <see cref="Resume"/>.
    /// </summary>
    public void Pause()
    {
        // Taking the lock waits out a single-threaded frame in progress; none starts once the flag is set.
        lock (singleThreadedFrameLock)
            paused = true;

        if (CurrentMode != ExecutionMode.MultiThread)
            return;

        audioThread.PauseMultiThreaded();
        updateThread.PauseMultiThreaded();
        drawThread.PauseMultiThreaded();

        foreach (var thread in new[] { audioThread, updateThread, drawThread })
        {
            if (!thread.WaitUntilPaused(pause_timeout))
                Logger.Warning($"{thread.Name} didn't stop within {pause_timeout.TotalSeconds:0} s of being paused.");
        }
    }

    public void Resume()
    {
        paused = false;

        audioThread.ResumeMultiThreaded();
        updateThread.ResumeMultiThreaded();
        drawThread.ResumeMultiThreaded();
    }

    public void Stop()
    {
        if (CurrentMode == ExecutionMode.MultiThread)
        {
            audioThread.StopMultiThreaded();
            updateThread.StopMultiThreaded();
            drawThread.StopMultiThreaded();
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
