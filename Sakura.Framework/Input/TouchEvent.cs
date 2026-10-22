// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Input;

/// <summary>
/// A finger going down, moving or lifting on a touchscreen.
/// </summary>
public readonly struct TouchEvent
{
    public TouchSource Source { get; }

    /// <summary>
    /// Where the finger is, in screen-space coordinates.
    /// </summary>
    public Vector2 ScreenSpacePosition { get; }

    /// <summary>
    /// How far the finger moved since its previous event, in screen space. Zero for a finger going down.
    /// </summary>
    public Vector2 Delta { get; }

    /// <summary>
    /// When the event physically happened, in milliseconds on the shared <see cref="TimeSource"/> timeline.
    /// <see cref="double.NaN"/> when there is no timestamp (synthetic events from tests).
    /// </summary>
    public double Timestamp { get; }

    /// <summary>
    /// For a finger lifting: the system took the finger rather than it lifting (a system gesture, such as
    /// swiping back from the edge, took over). Whatever the finger was doing is called off, not completed.
    /// </summary>
    public bool Cancelled { get; }

    public TouchEvent(TouchSource source, Vector2 screenSpacePosition, Vector2 delta = default, double timestamp = double.NaN, bool cancelled = false)
    {
        Source = source;
        ScreenSpacePosition = screenSpacePosition;
        Delta = delta;
        Timestamp = timestamp;
        Cancelled = cancelled;
    }

    public override string ToString() => $"TouchEvent: {Source} at {ScreenSpacePosition}, Delta={Delta}";
}
