// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Android;

/// <summary>
/// Writes the log's console output to logcat under the app's own tag, at the priority of each message's level.
/// </summary>
internal static class AndroidLog
{
    // Priorities from android/log.h.
    private const int debug_priority = 3;
    private const int info_priority = 4;
    private const int warn_priority = 5;
    private const int error_priority = 6;
    private const int fatal_priority = 7;

    /// <summary>
    /// The output for <see cref="Logger.ConsoleOutput"/> that writes under <paramref name="tag"/>.
    /// </summary>
    public static Action<LogLevel, string> For(string tag) => (level, message) => write(priorityOf(level), tag, message);

    private static int priorityOf(LogLevel level) => level switch
    {
        LogLevel.Debug => debug_priority,
        // Verbose is the level of most messages, which is what logcat calls info.
        LogLevel.Verbose => info_priority,
        LogLevel.Warning => warn_priority,
        LogLevel.Error => error_priority,
        _ => fatal_priority,
    };

    [DllImport("log", EntryPoint = "__android_log_write")]
    private static extern int write(int priority, [MarshalAs(UnmanagedType.LPUTF8Str)] string tag, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
}
