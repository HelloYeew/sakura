// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Threading;

public class ThreadRunner : IDisposable
{
    private readonly AppThread updateThread;
    private readonly AppThread drawThread;
    private readonly AppThread audioThread;

    public ExecutionMode CurrentMode { get; private set; }

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
    public void RunSingleThreadedFrame(double budgetMilliseconds = 0)
    {
        if (CurrentMode != ExecutionMode.SingleThread) return;

        audioThread.RunSingleFrame(budgetMilliseconds);
        updateThread.RunSingleFrame(budgetMilliseconds);
        drawThread.RunSingleFrame(budgetMilliseconds);
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
