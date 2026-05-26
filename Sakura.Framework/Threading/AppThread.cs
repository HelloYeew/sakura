// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Threading;

public class AppThread
{
    public string Name { get; }
    public Clock Clock { get; }
    public ThreadPriority Priority { get; set; } = ThreadPriority.Normal;
    public Action? OnInitialize { get; set; }
    public Action FrameAction { get; }
    public Func<double> GetTargetHz { get; }

    private Thread? internalThread;
    private readonly ManualResetEventSlim pauseEvent = new ManualResetEventSlim(true);
    private bool isRunning;
    private bool isPaused;

    public AppThread(string name, Action frameAction, Func<double> getTargetHz)
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
            IsBackground = true,
            Priority = Priority
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

        long timestampFrequency = System.Diagnostics.Stopwatch.Frequency;
        double msPerTick = 1000.0 / timestampFrequency;

        long lastFrameTime = System.Diagnostics.Stopwatch.GetTimestamp();

        while (isRunning)
        {
            pauseEvent.Wait();
            if (!isRunning) break;

            Clock.Update();
            double currentHz = GetTargetHz();

            FrameAction.Invoke();

            if (currentHz > 0)
            {
                double targetFrameTimeMs = 1000.0 / currentHz;
                long targetTicks = (long)(targetFrameTimeMs / msPerTick);
                long targetFrameTimeTimestamp = lastFrameTime + targetTicks;

                while (true)
                {
                    long currentTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                    long remainingTicks = targetFrameTimeTimestamp - currentTimestamp;

                    if (remainingTicks <= 0)
                        break;

                    double timeRemainingMs = remainingTicks * msPerTick;

                    if (timeRemainingMs >= 1.0)
                    {
                        Thread.Sleep(TimeSpan.FromMilliseconds(timeRemainingMs - 0.2));
                    }
                    else if (timeRemainingMs > 0.1)
                    {
                        Thread.Sleep(0);
                    }
                    else
                    {
                        Thread.SpinWait(10);
                    }
                }

                lastFrameTime += targetTicks;

                double thresholdLimitMs = Math.Max(targetFrameTimeMs * 5, 50.0);

                long currentAfterWait = System.Diagnostics.Stopwatch.GetTimestamp();
                if ((currentAfterWait - lastFrameTime) * msPerTick > thresholdLimitMs)
                {
                    lastFrameTime = currentAfterWait;
                }
            }
            else
            {
                lastFrameTime = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }
    }
}
