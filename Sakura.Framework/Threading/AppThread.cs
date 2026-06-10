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

        // Absolute next-frame deadline, expressed in stopwatch ticks. We advance it by exactly one
        // frame quantum each iteration so cadence is locked to wall-clock time rather than drifting
        // with however long each sleep happens to overshoot.
        long nextFrameTime = System.Diagnostics.Stopwatch.GetTimestamp();

        // Leave a small slice of the wait for a busy spin so we land on the deadline precisely.
        // The OS sleep is only accurate to ~0.5-1ms even with a high-resolution timer, so we sleep
        // for (remaining - guard) and spin the rest. This is what keeps a 1000 Hz thread sitting on
        // 1.00 ms instead of overshooting and then "catching up" by running flat-out.
        const double spin_guard_ms = 0.5;

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

                // Advance the deadline by one quantum from the previous deadline (not from "now"),
                // so transient overshoot on one frame is absorbed instead of compounding.
                nextFrameTime += targetTicks;

                long now = System.Diagnostics.Stopwatch.GetTimestamp();

                // If we've fallen more than ~5 frames behind (GC pause, window drag, scheduler
                // starvation) snap the deadline to now so we don't fire an avalanche of catch-up
                // frames. This is the only place the cadence is allowed to "reset".
                double behindMs = (now - nextFrameTime) * msPerTick;
                double maxBehindMs = Math.Max(targetFrameTimeMs * 5, 50.0);
                if (behindMs > maxBehindMs)
                {
                    nextFrameTime = now + targetTicks;
                    now = System.Diagnostics.Stopwatch.GetTimestamp();
                }

                double remainingMs = (nextFrameTime - now) * msPerTick;

                // Coarse phase: hand the CPU back to the OS for the bulk of the wait.
                double sleepMs = remainingMs - spin_guard_ms;
                if (sleepMs > 0)
                {
                    var sleepSpan = TimeSpan.FromMilliseconds(sleepMs);
                    if (nativeSleep?.Sleep(sleepSpan) != true)
                        Thread.Sleep(sleepSpan);
                }

                // Fine phase: tight busy-wait until the deadline. This spin is bounded by
                // spin_guard_ms (~0.5 ms), so it costs very little CPU while landing the frame on
                // the exact deadline. We deliberately do NOT use SpinWait here because SpinWait
                // escalates to Thread.Sleep(0/1) after a few iterations, which would overshoot a
                // sub-millisecond deadline. Thread.SpinWait(0) just issues a pause hint to the CPU.
                while (System.Diagnostics.Stopwatch.GetTimestamp() < nextFrameTime)
                    Thread.SpinWait(1);
            }
            else
            {
                // Truly unlimited — don't sleep or yield. The frame runs back-to-back as fast as
                // the work allows. Users who want CPU relief in unlimited mode should enable
                // LimitUnlimitedUpdateRate in HostOptions, which caps at 1000 Hz.
                nextFrameTime = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }

        nativeSleep?.Dispose();
    }
}
