// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Threading;

public class GameThread
{
    public string Name { get; }
    public Clock Clock { get; }
    public Action OnInitialize { get; set; }
    public Action FrameAction { get; }
    public Func<double> GetTargetHz { get; }

    private Thread? internalThread;
    private readonly ManualResetEventSlim pauseEvent = new ManualResetEventSlim(true);
    private bool isRunning;
    private bool isPaused;

    public GameThread(string name, Action frameAction, Func<double> getTargetHz)
    {
        Name = name;
        FrameAction = frameAction;
        GetTargetHz = getTargetHz;
        Clock = new Clock(true);
    }

    public void StartMultiThreaded()
    {
        if (isRunning) return;
        isRunning = true;
        isPaused = false;
        pauseEvent.Set();

        internalThread = new Thread(runLoop)
        {
            Name = Name,
            IsBackground = true
        };
        internalThread.Start();
    }

    public void StopMultiThreaded()
    {
        isRunning = false;
        pauseEvent.Set(); // unblock if paused
        internalThread?.Join(2000);
        internalThread = null;
    }

    public void PauseMultiThreaded()
    {
        isPaused = true;
        pauseEvent.Reset();
    }

    public void ResumeMultiThreaded()
    {
        isPaused = false;
        pauseEvent.Set();
    }

    public void RunSingleFrame()
    {
        Clock.Update();
        FrameAction.Invoke();
    }

    private void runLoop()
    {
        OnInitialize?.Invoke();

        var spinWait = new SpinWait();

        var threadTimer = System.Diagnostics.Stopwatch.StartNew();
        double lastFrameTime = threadTimer.Elapsed.TotalMilliseconds;

        while (isRunning)
        {
            pauseEvent.Wait();
            if (!isRunning) break;

            Clock.Update();
            double currentHz = GetTargetHz();

            FrameAction.Invoke();

            if (currentHz > 0)
            {
                double targetFrameTime = 1000.0 / currentHz;

                while (threadTimer.Elapsed.TotalMilliseconds - lastFrameTime < targetFrameTime)
                {
                    spinWait.SpinOnce();
                }

                lastFrameTime += targetFrameTime;

                if (threadTimer.Elapsed.TotalMilliseconds - lastFrameTime > targetFrameTime * 5)
                {
                    lastFrameTime = threadTimer.Elapsed.TotalMilliseconds;
                }
            }
            else
            {
                lastFrameTime = threadTimer.Elapsed.TotalMilliseconds;
            }
        }
    }
}
