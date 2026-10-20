// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Threading;
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

    private Thread? internalThread;
    private readonly ManualResetEventSlim pauseEvent = new ManualResetEventSlim(true);
    private volatile bool isRunning;
    private volatile bool isPaused;

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
        invokeFrameAction(budgetMilliseconds, deadlineMilliseconds > 0 ? deadlineMilliseconds : GetDeadlineMilliseconds?.Invoke() ?? 0);
    }

    private static readonly double ms_per_tick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>
    /// Invokes <see cref="FrameAction"/> and records how long its work took, separating out any GC
    /// pause that landed inside it.
    /// </summary>
    /// <returns>
    /// The timestamp taken immediately after the frame's work, so the pacing code can reuse it
    /// instead of reading the clock again.
    /// </returns>
    private long invokeFrameAction(double budgetMilliseconds, double deadlineMilliseconds)
    {
        long startTicks = Stopwatch.GetTimestamp();
        double pauseBefore = GC.GetTotalPauseDuration().TotalMilliseconds;

        FrameAction.Invoke();

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

        return endTicks;
    }

    private void runLoop()
    {
        OnInitialize?.Invoke();

        // Owned by this run of the loop rather than the thread object, because switching execution mode
        // stops and later restarts the loop. A waiter disposed by the previous run would keep a closed
        // timer handle on Windows, failing every sleep over to Thread.Sleep.
        using var waiter = new FrameWaiter(Name);

        // Absolute next-frame deadline, expressed in stopwatch ticks. We advance it by exactly one
        // frame quantum each iteration so cadence is locked to wall-clock time rather than drifting
        // with however long each sleep happens to overshoot.
        long nextFrameTime = Stopwatch.GetTimestamp();

        while (isRunning)
        {
            pauseEvent.Wait();
            if (!isRunning) break;

            Clock.Update();
            double currentHz = GetTargetHz();
            double targetFrameTimeMs = currentHz > 0 ? 1000.0 / currentHz : 0;

            long now = invokeFrameAction(targetFrameTimeMs, GetDeadlineMilliseconds?.Invoke() ?? 0);

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
