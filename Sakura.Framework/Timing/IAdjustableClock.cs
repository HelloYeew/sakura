// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// A clock whose playback can be controlled: started, stopped, sought, and rate-adjusted.
/// </summary>
public interface IAdjustableClock : IClock
{
    /// <summary>
    /// Starts (or resumes) the clock.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops (pauses) the clock at its current time.
    /// </summary>
    void Stop();

    /// <summary>
    /// Seeks the clock to the given time in milliseconds.
    /// </summary>
    /// <param name="position">The target time in milliseconds.</param>
    /// <returns>Whether the seek was successful.</returns>
    bool Seek(double position);

    /// <summary>
    /// Stops the clock and resets its time to zero.
    /// </summary>
    void Reset();

    /// <summary>
    /// The rate this clock runs at, relative to real time. Settable.
    /// </summary>
    new double Rate { get; set; }
}
