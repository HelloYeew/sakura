// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// Interface for a clock that provides high-precision timing.
/// </summary>
public interface IClock
{
    /// <summary>
    /// The current time in seconds since the clock was started.
    /// </summary>
    double CurrentTime { get; }

    /// <summary>
    /// The time in seconds that has elapsed since the last frame.
    /// </summary>
    double ElapsedFrameTime { get; }

    /// <summary>
    /// The number of frames that have been processed since the clock was started.
    /// </summary>
    double FramesPerSecond { get; }

    /// <summary>
    /// Whether the clock is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Start the clock.
    /// </summary>
    void Start();

    /// <summary>
    /// Stop the clock.
    /// </summary>
    void Stop();

    /// <summary>
    /// Update the clock's state. This should be called once per frame.
    /// </summary>
    void Update();
}
