// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

public readonly struct MouseEvent : IMouseEvent
{
    public MouseState MouseState { get; }
    public Vector2 ScreenSpaceMousePosition => MouseState.Position;
    public Vector2 Delta { get; }

    /// <summary>
    /// The time the motion physically happened, on the shared <see cref="Sakura.Framework.Timing.TimeSource"/>
    /// timeline in milliseconds. <see cref="double.NaN"/> when the source provides no timestamp
    /// (e.g. synthetic events from tests).
    /// </summary>
    public double Timestamp { get; }

    public MouseEvent(MouseState mouseState, Vector2 delta = default, double timestamp = double.NaN)
    {
        MouseState = mouseState;
        Delta = delta;
        Timestamp = timestamp;
    }

    public override string ToString()
    {
        return $"MouseEvent: Position={MouseState.Position}, MouseState=[{MouseState}, Delta={Delta}]";
    }
}
