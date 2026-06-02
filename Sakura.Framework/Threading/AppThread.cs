// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Sakura.Framework.Timing;
using Logger = Sakura.Framework.Logging.Logger;
using OSPlatform = System.Runtime.InteropServices.OSPlatform;

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
    private volatile bool isRunning;
    private volatile bool isPaused;

    /// <summary>
    /// Platform-specific high-precision sleep. Null if unavailable (falls back to Thread.Sleep)
    /// </summary>
    private readonly INativeSleep? nativeSleep;

    public AppThread(string name, Action frameAction, Func<double> getTargetHz)
    {
        Name = name;
        FrameAction = frameAction;
        GetTargetHz = getTargetHz;
        Clock = new Clock(true);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            // Use Sakura's RuntimeInfo here make CA1416 warning
            // CA1416: This call site is reachable on all platforms. 'WindowsNativeSleep' is only supported on: 'windows'.
            nativeSleep = new WindowsNativeSleep();
        else if (UnixNativeSleep.IsAvailable)
            nativeSleep = new UnixNativeSleep();

        Logger.Debug($"AppThread '{Name}' initialized with native sleep: {(nativeSleep != null ? nativeSleep.GetType().Name : "none")}");
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

        // Accumulated sleep error for drift correction (same approach as osu!framework's ThrottledFrameClock).
        double accumulatedSleepError = 0.0;

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

                // How much time has elapsed since the last frame started?
                long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                double elapsedMs = (nowTicks - lastFrameTime) * msPerTick;

                // Excess = how long we should sleep (with drift correction).
                double sleepMs = targetFrameTimeMs - elapsedMs + accumulatedSleepError;

                if (sleepMs > 0)
                {
                    var sleepSpan = TimeSpan.FromMilliseconds(sleepMs);
                    double beforeMs = System.Diagnostics.Stopwatch.GetTimestamp() * msPerTick;

                    if (nativeSleep?.Sleep(sleepSpan) != true)
                        Thread.Sleep(sleepSpan);

                    double actualSleepMs = System.Diagnostics.Stopwatch.GetTimestamp() * msPerTick - beforeMs;

                    // Correct for overshoot/undershoot; clamp to avoid runaway catch-up.
                    accumulatedSleepError += sleepMs - actualSleepMs;
                    accumulatedSleepError = Math.Max(-1000.0 / 30.0, accumulatedSleepError);
                }
                else
                {
                    // Already late — reset error so we don't double-compensate.
                    accumulatedSleepError = 0;
                }

                // Advance the frame anchor by one frame quantum (keeps cadence locked).
                long targetTicks = (long)(targetFrameTimeMs / msPerTick);
                lastFrameTime += targetTicks;

                // If we've slipped more than 5 frames behind, reset to avoid a catch-up avalanche.
                long currentAfterWait = System.Diagnostics.Stopwatch.GetTimestamp();
                double thresholdMs = Math.Max(targetFrameTimeMs * 5, 50.0);
                if ((currentAfterWait - lastFrameTime) * msPerTick > thresholdMs)
                {
                    lastFrameTime = currentAfterWait;
                    accumulatedSleepError = 0;
                }
            }
            else
            {
                // Unlimited rate — yield once so we don't monopolise the CPU.
                lastFrameTime = System.Diagnostics.Stopwatch.GetTimestamp();
                accumulatedSleepError = 0;
                Thread.Sleep(0);
            }
        }

        nativeSleep?.Dispose();
    }
}
