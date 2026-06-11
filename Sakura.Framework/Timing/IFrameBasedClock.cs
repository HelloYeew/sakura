// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Timing;

/// <summary>
/// A clock that takes a consistent snapshot of time once per frame.
/// <para>
/// Between calls to <see cref="ProcessFrame"/>, <see cref="IClock.CurrentTime"/> does not change.
/// This guarantees every consumer within a single frame observes the same time, which is
/// essential for coherent layout, animation and judgement logic.
/// </para>
/// </summary>
public interface IFrameBasedClock : IClock
{
    /// <summary>
    /// The time in milliseconds that elapsed between the last two <see cref="ProcessFrame"/> calls.
    /// </summary>
    double ElapsedFrameTime { get; }

    /// <summary>
    /// The number of frames processed per second, averaged over a recent window.
    /// </summary>
    double FramesPerSecond { get; }

    /// <summary>
    /// Advances the clock to the current time of its underlying source.
    /// Call exactly once per frame, before any consumer reads the clock.
    /// </summary>
    void ProcessFrame();
}
