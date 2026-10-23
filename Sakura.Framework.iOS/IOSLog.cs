// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using CoreFoundation;
using Foundation;
using Sakura.Framework.Logging;

namespace Sakura.Framework.iOS;

/// <summary>
/// Writes the log's console output to the unified system log, under the app's bundle ID, at the level of each message.
/// Standard output goes nowhere for an app the system starts; the system log is what Console, Xcode and
/// <c>log stream</c> show, from a device or the simulator.
/// </summary>
internal static class IOSLog
{
    /// <summary>
    /// The output for <see cref="Logger.ConsoleOutput"/> that writes under <paramref name="category"/>.
    /// </summary>
    public static Action<LogLevel, string> For(string category)
    {
        var log = new CoreFoundation.OSLog(NSBundle.MainBundle.BundleIdentifier ?? category, category);
        return (level, message) => log.Log(levelOf(level), message);
    }

    private static OSLogLevel levelOf(LogLevel level) => level switch
    {
        LogLevel.Debug => OSLogLevel.Debug,
        // The system log keeps Default and above, and shows them unasked; Info and Debug are only seen while
        // something streams them. Verbose is the level of most messages, and warnings have no level of their own.
        LogLevel.Verbose or LogLevel.Warning => OSLogLevel.Default,
        LogLevel.Error => OSLogLevel.Error,
        _ => OSLogLevel.Fault,
    };
}
