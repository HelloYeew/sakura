// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Threading;

public class ThreadRunner : IDisposable
{
    private readonly GameThread updateThread;
    private readonly GameThread drawThread;
    private readonly GameThread audioThread;

    public ExecutionMode CurrentMode { get; private set; }

    public ThreadRunner(GameThread updateThread, GameThread drawThread, GameThread audioThread)
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

    public void RunSingleThreadedFrame()
    {
        if (CurrentMode != ExecutionMode.SingleThread) return;

        audioThread.RunSingleFrame();
        updateThread.RunSingleFrame();
        drawThread.RunSingleFrame();
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
