// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Timing;

/// <summary>
/// A handle for a task that has been scheduled to run.
/// </summary>
public class ScheduledTask
{
    /// <summary>
    /// The action to be executed.
    /// </summary>
    public Action Action { get; }

    /// <summary>
    /// The time at which this task will be executed.
    /// </summary>
    internal double ExecutionTime { get; set; }

    /// <summary>
    /// The interval at which this task will repeat. A value of 0 or less means no repeat.
    /// </summary>
    internal double RepeatInterval { get; }

    internal ScheduledTask(Action action, double executionTime, double repeatInterval = 0)
    {
        Action = action;
        ExecutionTime = executionTime;
        RepeatInterval = repeatInterval;
    }
}

