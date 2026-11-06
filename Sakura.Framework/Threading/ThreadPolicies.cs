// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Threading;

/// <summary>
/// Picks the <see cref="IThreadPolicy"/> for the platform the app runs on.
/// </summary>
internal static class ThreadPolicies
{
    /// <summary>
    /// Creates the policy for one of the host's threads. Called on that thread.
    /// </summary>
    /// <param name="role">Which thread it's for.</param>
    /// <param name="threadName">The thread's name, for logging.</param>
    /// <param name="useWorkIntervals">Whether the frame threads may tell the system when each frame is due.</param>
    /// <returns>The policy, or null where the platform has nothing to apply.</returns>
    public static IThreadPolicy? Create(ThreadRole role, string threadName, bool useWorkIntervals)
    {
        if (DarwinThreadPolicy.IsAvailable)
            return new DarwinThreadPolicy(role, threadName, useWorkIntervals && role != ThreadRole.Audio);

        // The audio thread keeps Windows' choice: it moves the music's state on even with the app out of sight.
        if (OperatingSystem.IsWindows())
            return role == ThreadRole.Audio ? null : new WindowsThreadPolicy(threadName);

        // Likewise on Linux, where the audio thread never waits precisely and so has no use for less slack.
        if (OperatingSystem.IsLinux())
            return role == ThreadRole.Audio ? null : new LinuxThreadPolicy(threadName);

        return null;
    }
}
