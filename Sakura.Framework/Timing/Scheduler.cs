// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;

namespace Sakura.Framework.Timing;

/// <summary>
/// Manages scheduled, delayed, and repeating actions based on a clock.
/// </summary>
public class Scheduler
{
    private readonly List<ScheduledTask> tasks = new List<ScheduledTask>();
    private readonly List<ScheduledTask> tasksToAdd = new List<ScheduledTask>();
    private IClock? clock;

    public Scheduler(IClock? clock = null)
    {
        this.clock = clock;
    }

    public void SetClock(IClock newClock) => clock = newClock;

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

        bool removed = tasksToAdd.Remove(task);

        if (tasks.Remove(task))
            removed = true;

        return removed;
    }

    /// <summary>
    /// Removes all scheduled actions.
    /// </summary>
    public void Clear()
    {
        tasks.Clear();
        tasksToAdd.Clear();
    }


    /// <summary>
    /// Updates the scheduler, running all actions that are due.
    /// </summary>
    public void Update()
    {
        if (clock == null) return;

        double currentTime = clock.CurrentTime;

        if (tasksToAdd.Count > 0)
        {
            tasks.AddRange(tasksToAdd);
            tasksToAdd.Clear();
            tasks.Sort((a, b) => a.ExecutionTime.CompareTo(b.ExecutionTime));
        }

        List<ScheduledTask>? toRemove = null;
        List<ScheduledTask>? toReAdd = null;

        foreach (var task in tasks)
        {
            if (currentTime >= task.ExecutionTime)
            {
                task.Action();

                if (toRemove == null) toRemove = new List<ScheduledTask>();
                toRemove.Add(task);

                if (task.RepeatInterval > 0)
                {
                    task.ExecutionTime += task.RepeatInterval;
                    if (toReAdd == null) toReAdd = new List<ScheduledTask>();
                    toReAdd.Add(task);
                }
            }
            else
            {
                // Since the list is sorted by execution time, we can stop checking.
                break;
            }
        }

        if (toRemove != null)
        {
            foreach (var task in toRemove)
                tasks.Remove(task);
        }

        if (toReAdd != null)
        {
            // Re-add repeating tasks to the pending list to be sorted in next frame.
            tasksToAdd.AddRange(toReAdd);
        }
    }
}

