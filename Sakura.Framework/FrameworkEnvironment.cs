// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Threading;

namespace Sakura.Framework;

public static class FrameworkEnvironment
{
    /// <summary>
    /// The execution mode every host in this process runs in, regardless of <c>framework.ini</c>, when
    /// <c>SAKURA_EXECUTION_MODE</c> is <c>SingleThread</c> or <c>MultiThread</c>.
    /// </summary>
    /// <remarks>
    /// Headless hosts start from a fresh configuration, so without this every headless test runs in the
    /// default mode only. CI runs the suite once in each.
    /// </remarks>
    public static ExecutionMode? StartupExecutionMode { get; } = parse<ExecutionMode>("SAKURA_EXECUTION_MODE");

    private static T? parse<T>(string variable) where T : struct, Enum
        => Enum.TryParse(Environment.GetEnvironmentVariable(variable), true, out T value) && Enum.IsDefined(value) ? value : null;
}
