// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Threading;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
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

    /// <summary>
    /// Per-frame timings, recorded by this thread as each frame completes.
    /// </summary>
    public ThreadFrameStatistics FrameStatistics { get; } = new ThreadFrameStatistics();

    /// <summary>
    /// Optional. Asked, after each frame, how much of it the <see cref="FrameAction"/> spent blocked
    /// on an external device rather than doing work. Reported as
    /// <see cref="ThreadFrameSample.BlockedMilliseconds"/> and excluded from the busy figure.
    /// </summary>
    /// <remarks>
    /// Called on this thread immediately after the frame action returns, so an implementation can
    /// simply hand back a value the action just stored.
    /// </remarks>
    public Func<double>? GetBlockedMilliseconds { get; set; }

    /// <summary>
    /// Optional. Asked, at the top of each frame, how long that frame's work may take before something
    /// visible is lost. Recorded as <see cref="ThreadFrameSample.DeadlineMilliseconds"/> and used for
    /// <see cref="ThreadFrameSample.MissedDeadline"/>. Frames record no deadline when this is unset.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GetTargetHz"/> because a thread's target rate is a pacing choice rather
    /// than a due date - see <see cref="ThreadFrameSample.DeadlineMilliseconds"/>.
    /// </remarks>
    public Func<double>? GetDeadlineMilliseconds { get; set; }

    /// <summary>
    /// Whether the last stretch of each frame wait may busy-spin for precise pacing (see <see cref="FrameWaiter"/>).
    /// When it returns false the thread sleeps for the full remaining time instead,
    /// trading sub-millisecond timing jitter for CPU/battery savings.
    /// Defaults to always spinning.
    /// </summary>
    public Func<bool> UsePreciseTiming { get; set; } = static () => true;

    /// <summary>
    /// Whether to register this thread with the Windows Multimedia Class Scheduler Service while it runs
    /// multi-threaded. Ignored on other platforms. See <see cref="Platform.HostOptions.UseMultimediaClassScheduler"/>.
    /// </summary>
    public bool UseMultimediaClassScheduler { get; set; }

    /// <summary>
    /// Optional. Opens, on this thread as it starts running on its own, a session that reports each frame's work
    /// against its target to the platform (<see cref="IPlatformPerformance.CreateFrameWorkHint"/>). Passed the
    /// thread's name and the first frame's target in milliseconds.
    /// </summary>
    public Func<string, double, IFrameWorkHint?>? CreateWorkHint { get; set; }

    private Thread? internalThread;
    private volatile bool isRunning;

    /// <summary>
    /// Guards <see cref="pauseRequested"/> and <see cref="parked"/>, and is what a paused thread waits on.
    /// </summary>
    private readonly object pauseLock = new object();

    private bool pauseRequested;

    /// <summary>
    /// Whether the thread is waiting between frames because a pause was asked for.
    /// </summary>
    private bool parked;

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

        // Wake it if it's paused, so it can see it should stop.
        lock (pauseLock)
            Monitor.PulseAll(pauseLock);

        internalThread?.Join(2000);
        internalThread = null;
    }

    /// <summary>
    /// Asks the thread to stop between frames, which it does once the frame it is on finishes. Use
    /// <see cref="WaitUntilPaused"/> to know when it has.
    /// </summary>
    public void PauseMultiThreaded()
    {
        lock (pauseLock)
            pauseRequested = true;
    }

    public void ResumeMultiThreaded()
    {
        lock (pauseLock)
        {
            pauseRequested = false;
            Monitor.PulseAll(pauseLock);
        }
    }

    /// <summary>
    /// Waits until a paused thread has finished its frame and stopped, so nothing it does can overlap with
    /// what the caller does next. True straight away for a thread that isn't running on its own.
    /// </summary>
    /// <returns>False if it hadn't stopped within <paramref name="timeout"/>.</returns>
    public bool WaitUntilPaused(TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        lock (pauseLock)
        {
            while (pauseRequested && !parked && isRunning && internalThread != null)
            {
                long remaining = deadline - Environment.TickCount64;

                if (remaining <= 0 || !Monitor.Wait(pauseLock, (int)remaining))
                    return parked;
            }

            return true;
        }
    }

    /// <summary>
    /// Runs a single frame on the calling thread.
    /// </summary>
    /// <param name="budgetMilliseconds">
    /// The frame budget to record against this frame, or 0 if unbounded. Passed in rather than
    /// derived from <see cref="GetTargetHz"/> because in single-threaded execution every thread runs
    /// once per main-loop iteration and so shares that loop's budget, not its own target rate.
    /// </param>
    /// <param name="deadlineMilliseconds">
    /// The deadline to record against this frame, or 0 if there is none. Falls back to
    /// <see cref="GetDeadlineMilliseconds"/> when not given.
    /// </param>
    public void RunSingleFrame(double budgetMilliseconds = 0, double deadlineMilliseconds = 0)
    {
        Clock.Update();
        invokeFrameAction(budgetMilliseconds, deadlineMilliseconds > 0 ? deadlineMilliseconds : GetDeadlineMilliseconds?.Invoke() ?? 0, out _);
    }

    private static readonly double ms_per_tick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>
    /// Invokes <see cref="FrameAction"/> and records how long its work took, separating out any GC
    /// pause that landed inside it.
    /// </summary>
    /// <param name="workMilliseconds">How long the frame took, less the time it was blocked on a device.</param>
    /// <returns>
    /// The timestamp taken immediately after the frame's work, so the pacing code can reuse it
    /// instead of reading the clock again.
    /// </returns>
    private long invokeFrameAction(double budgetMilliseconds, double deadlineMilliseconds, out double workMilliseconds)
    {
        long startTicks = Stopwatch.GetTimestamp();
        double pauseBefore = GC.GetTotalPauseDuration().TotalMilliseconds;

        // Drained inside the timing: freeing what the frame left is the frame's work too.
        IntPtr pool = AutoreleasePool.Push();
        FrameAction.Invoke();
        AutoreleasePool.Pop(pool);

        long endTicks = Stopwatch.GetTimestamp();

        // A GC pause suspends every managed thread, so a collection triggered elsewhere still stalled
        // this frame. Attributing it here and subtracting it keeps the busy figure to work we chose
        // to do, while leaving the stall visible in its own field.
        double gcMilliseconds = GC.GetTotalPauseDuration().TotalMilliseconds - pauseBefore;
        double blockedMilliseconds = GetBlockedMilliseconds?.Invoke() ?? 0;
        double frameMilliseconds = (endTicks - startTicks) * ms_per_tick;

        FrameStatistics.Record(new ThreadFrameSample
        {
            // Clamped because neither the GC pause nor the blocked span is measured against exactly
            // this window since a pause can straddle either boundary, and one landing inside the blocked
            // span would otherwise be subtracted twice.
            BusyMilliseconds = Math.Max(0, frameMilliseconds - gcMilliseconds - blockedMilliseconds),
            GCMilliseconds = gcMilliseconds,
            BlockedMilliseconds = blockedMilliseconds,
            ElapsedMilliseconds = Clock.ElapsedFrameTime,
            BudgetMilliseconds = budgetMilliseconds,
            DeadlineMilliseconds = deadlineMilliseconds
        });

        // A GC pause stays in: the CPU has to get through it too.
        workMilliseconds = Math.Max(0, frameMilliseconds - blockedMilliseconds);

        return endTicks;
    }

    private void runLoop()
    {
        OnInitialize?.Invoke();

        // Owned by this run of the loop rather than the thread object, because switching execution mode
        // stops and later restarts the loop. A waiter disposed by the previous run would keep a closed
        // timer handle on Windows, failing every sleep over to Thread.Sleep.
        using var waiter = new FrameWaiter(Name);

        // Registration applies to the calling thread, so it can only happen here, on the thread itself.
        using var multimediaScheduler = UseMultimediaClassScheduler && OperatingSystem.IsWindows()
            ? WindowsMultimediaScheduler.Register(Name)
            : null;

        // Likewise: the session describes the thread that opens it.
        double firstHz = GetTargetHz();
        using var workHint = CreateWorkHint?.Invoke(Name, firstHz > 0 ? 1000.0 / firstHz : 1000.0 / 60);

        // Absolute next-frame deadline, expressed in stopwatch ticks. We advance it by exactly one
        // frame quantum each iteration so cadence is locked to wall-clock time rather than drifting
        // with however long each sleep happens to overshoot.
        long nextFrameTime = Stopwatch.GetTimestamp();

        while (isRunning)
        {
            lock (pauseLock)
            {
                while (pauseRequested && isRunning)
                {
                    parked = true;
                    Monitor.PulseAll(pauseLock);
                    Monitor.Wait(pauseLock);
                }

                parked = false;
            }

            if (!isRunning) break;

            Clock.Update();
            double currentHz = GetTargetHz();
            double targetFrameTimeMs = currentHz > 0 ? 1000.0 / currentHz : 0;

            long now = invokeFrameAction(targetFrameTimeMs, GetDeadlineMilliseconds?.Invoke() ?? 0, out double workMilliseconds);

            workHint?.ReportFrame(targetFrameTimeMs, workMilliseconds);

            if (currentHz > 0)
            {
                long targetTicks = (long)(targetFrameTimeMs / ms_per_tick);

                nextFrameTime += targetTicks;

                if (now > nextFrameTime)
                    nextFrameTime = now;

                // Precision spinning may be disabled (battery-constrained targets, inactive
                // window): sleep the entire remaining time and accept sub-ms pacing jitter.
                waiter.WaitUntil(nextFrameTime, UsePreciseTiming());
            }
            else
            {
                nextFrameTime = now;
            }
        }
    }
}
