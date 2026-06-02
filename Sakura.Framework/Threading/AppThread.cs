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

        double accumulatedSleepError = 0.0;

        const double min_sleep_ms = 0.5;

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

                if (sleepMs >= min_sleep_ms)
                {
                    var sleepSpan = TimeSpan.FromMilliseconds(sleepMs);
                    double beforeMs = System.Diagnostics.Stopwatch.GetTimestamp() * msPerTick;

                    if (nativeSleep?.Sleep(sleepSpan) != true)
                        Thread.Sleep(sleepSpan);

                    double actualSleepMs = System.Diagnostics.Stopwatch.GetTimestamp() * msPerTick - beforeMs;

                    // Fold overshoot/undershoot back into the accumulator.
                    accumulatedSleepError += sleepMs - actualSleepMs;
                }
                else
                {
                    // Frame is late or remainder is below the sleep precision floor.
                    // Carry the deficit forward so the next frame compensates, rather than discarding it.
                    accumulatedSleepError += sleepMs;
                }

                // Clamp the accumulator: allow at most ~33 ms of catch-up debt (one missed 30fps frame),
                // and allow up to +2 ms of credit (so a frame that finished early can donate it forward).
                accumulatedSleepError = Math.Clamp(accumulatedSleepError, -1000.0 / 30.0, 2.0);

                // Advance the frame anchor by one frame quantum (keeps cadence locked).
                long targetTicks = (long)(targetFrameTimeMs / msPerTick);
                lastFrameTime += targetTicks;

                // If we've slipped more than 5 frames behind (e.g. after a GC pause or window drag),
                // snap the anchor forward to avoid a long catch-up avalanche of back-to-back frames.
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
                // Truly unlimited — don't sleep or yield. The frame runs back-to-back as fast as
                // the work allows. Users who want CPU relief in unlimited mode should enable
                // LimitUnlimitedUpdateRate in HostOptions, which caps at 1000 Hz with nanosleep.
                lastFrameTime = System.Diagnostics.Stopwatch.GetTimestamp();
                accumulatedSleepError = 0;
            }
        }

        nativeSleep?.Dispose();
    }
}
