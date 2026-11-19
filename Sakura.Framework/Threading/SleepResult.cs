// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Threading;

/// <summary>
/// How an <see cref="INativeSleep"/> ended.
/// </summary>
internal enum SleepResult
{
    /// <summary>
    /// It slept for the whole duration.
    /// </summary>
    Slept,

    /// <summary>
    /// <see cref="INativeSleep.Wake"/> ended it early.
    /// </summary>
    Woken,

    /// <summary>
    /// The platform's call failed, so it didn't sleep.
    /// </summary>
    Failed
}
