// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Input;

public readonly struct MouseButtonEvent : IMouseEvent
{
    public MouseState MouseState { get; }
    public MouseButton Button { get; }
    public int Clicks { get; }
    public Vector2 ScreenSpaceMousePosition => MouseState.Position;

    /// <summary>
    /// The time at which this event physically occurred, in milliseconds on the shared
    /// <see cref="TimeSource"/> timeline. Captured from the OS event
    /// timestamp where available. <see cref="double.NaN"/> when no timestamp was available.
    /// </summary>
    public double Timestamp { get; }

    public MouseButtonEvent(MouseState mouseState, MouseButton button, int clicks, double timestamp = double.NaN)
    {
        MouseState = mouseState;
        Button = button;
        Clicks = clicks;
        Timestamp = timestamp;
    }

    public override string ToString()
    {
        return $"MouseButtonEvent: Button={Button}, Clicks={Clicks}, Position={MouseState.Position}, MouseState=[{MouseState}]";
    }
}
