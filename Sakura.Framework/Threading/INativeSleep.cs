// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Threading;

/// <summary>
/// Provides a platform-specific high-precision sleep primitive.
/// </summary>
internal interface INativeSleep : IDisposable
{
    /// <summary>
    /// Sleeps for the given duration, or until <see cref="Wake"/> ends the sleep.
    /// </summary>
    /// <returns>How the sleep ended. <see cref="SleepResult.Failed"/> means it should fall back to <see cref="System.Threading.Thread.Sleep(int)"/>.</returns>
    SleepResult Sleep(TimeSpan duration);

    /// <summary>
    /// Ends the sleep under way early, or the next one if none is, which then returns <see cref="SleepResult.Woken"/> at once.
    /// Several wakes before a sleep end only that one. Safe to call from any thread. Does nothing where a sleep can't be woken.
    /// </summary>
    void Wake();
}
