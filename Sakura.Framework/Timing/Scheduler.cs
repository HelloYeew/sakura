// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Threading;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Timing;

/// <summary>
/// Manages scheduled, delayed, and repeating actions based on a clock.
/// </summary>
public class Scheduler
{
    private static readonly GlobalStatistic<int> stat_pending_tasks = GlobalStatistics.Get<int>("Scheduler", "Pending Tasks");

    private readonly List<ScheduledTask> tasks = new List<ScheduledTask>();
    private readonly List<ScheduledTask> tasksToAdd = new List<ScheduledTask>();
    private IClock? clock;

    private readonly Lock locker = new Lock();
    
    private readonly List<ScheduledTask> dueBuffer = new List<ScheduledTask>();

    public Scheduler(IClock? clock = null)
    {
        this.clock = clock;
    }

    public void SetClock(IClock newClock) => clock = newClock;

    /// <summary>
    /// Shifts all pending task execution times by <paramref name="delta"/> milliseconds.
    /// Used when the owning drawable's clock is replaced (e.g. on being added to a container)
    /// so tasks scheduled on the old timeline keep their intended relative delays.
    /// </summary>
    internal void Rebase(double delta)
    {
        if (delta == 0)
            return;

        lock (locker)
        {
            foreach (var task in tasks)
                task.ExecutionTime += delta;

            foreach (var task in tasksToAdd)
                task.ExecutionTime += delta;
        }
    }

    /// <summary>
    /// Schedules a single action to be performed as soon as possible.
    /// </summary>
    /// <param name="action">The action to perform.</param>
    /// <returns>A handle to the scheduled task, which can be used to cancel it.</returns>
    public ScheduledTask Add(Action action) => AddDelayed(action, 0);

    /// <summary>
    /// Schedules a single action to be performed after a specified delay.
    /// </summary>
    /// <param name="action">The action to perform.</param>
    /// <param name="delay">The delay in milliseconds before execution.</param>
    /// <returns>A handle to the scheduled task, which can be used to cancel it.</returns>
    public ScheduledTask AddDelayed(Action action, double delay)
    {
        if (clock == null)
            throw new InvalidOperationException("Scheduler requires a clock to be set before adding delayed tasks.");

        var task = new ScheduledTask(action, clock.CurrentTime + delay);
        lock (locker)
            tasksToAdd.Add(task);
        return task;
    }

    /// <summary>
    /// Schedules an action to be performed repeatedly.
    /// </summary>
    /// <param name="action">The action to perform.</param>
    /// <param name="delay">The delay in milliseconds before the first execution.</param>
    /// <param name="repeatInterval">The interval in milliseconds between subsequent executions.</param>
    /// <returns>A handle to the scheduled task, which can be used to cancel it.</returns>
    public ScheduledTask AddRepeating(Action action, double delay, double repeatInterval)
    {
        if (clock == null)
            throw new InvalidOperationException("Scheduler requires a clock to be set before adding repeating tasks.");
        if (repeatInterval <= 0)
            throw new ArgumentException("Repeat interval must be greater than zero.", nameof(repeatInterval));

        var task = new ScheduledTask(action, clock.CurrentTime + delay, repeatInterval);
        lock (locker)
            tasksToAdd.Add(task);
        return task;
    }

    /// <summary>
    /// Cancels a previously scheduled task.
    /// </summary>
    /// <param name="task">The task to cancel.</param>
    /// <returns>True if the task was found and cancelled, false otherwise.</returns>
    public bool Cancel(ScheduledTask? task)
    {
        if (task == null)
            return false;

        lock (locker)
        {
            bool removed = tasksToAdd.Remove(task);

            if (tasks.Remove(task))
                removed = true;

            return removed;
        }
    }

    /// <summary>
    /// Removes all scheduled actions.
    /// </summary>
    public void Clear()
    {
        lock (locker)
        {
            tasks.Clear();
            tasksToAdd.Clear();
        }
    }


    private void insertSorted(ScheduledTask task)
    {
        // binary insertion keeps the list sorted without re-sorting everything on each add.
        int low = 0;
        int high = tasks.Count;

        while (low < high)
        {
            int mid = (low + high) / 2;
            if (tasks[mid].ExecutionTime <= task.ExecutionTime)
                low = mid + 1;
            else
                high = mid;
        }

        tasks.Insert(low, task);
    }

    /// <summary>
    /// Updates the scheduler, running all actions that are due.
    /// </summary>
    public void Update()
    {
        if (clock == null) return;

        double currentTime = clock.CurrentTime;
        bool anyDue;

        lock (locker)
        {
            stat_pending_tasks.Value = tasks.Count + tasksToAdd.Count;

            if (tasksToAdd.Count > 0)
            {
                for (int i = 0; i < tasksToAdd.Count; i++)
                    insertSorted(tasksToAdd[i]);
                tasksToAdd.Clear();
            }

            int executed = 0;
            while (executed < tasks.Count && currentTime >= tasks[executed].ExecutionTime)
                executed++;

            anyDue = executed > 0;

            if (anyDue)
            {
                dueBuffer.Clear();
                for (int i = 0; i < executed; i++)
                    dueBuffer.Add(tasks[i]);

                tasks.RemoveRange(0, executed);

                for (int i = 0; i < dueBuffer.Count; i++)
                {
                    var task = dueBuffer[i];
                    if (task.RepeatInterval > 0)
                    {
                        task.ExecutionTime += task.RepeatInterval;
                        tasksToAdd.Add(task);
                    }
                }
            }
        }

        if (!anyDue)
            return;

        for (int i = 0; i < dueBuffer.Count; i++)
            dueBuffer[i].Action();
    }
}

