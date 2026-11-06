// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

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

        return null;
    }
}
