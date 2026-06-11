// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// The minimal read-only contract for a clock.
/// All times in the framework are expressed in <b>milliseconds</b>.
/// </summary>
public interface IClock
{
    /// <summary>
    /// The current time of this clock in milliseconds.
    /// </summary>
    double CurrentTime { get; }

    /// <summary>
    /// The rate this clock is running at, relative to real time. 1.0 is real time.
    /// </summary>
    double Rate { get; }

    /// <summary>
    /// Whether the clock is currently running.
    /// </summary>
    bool IsRunning { get; }
}
