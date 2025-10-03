// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

public readonly struct MouseButtonEvent : IMouseEvent
{
    public MouseState MouseState { get; }
    public MouseButton Button { get; }
    public int Clicks { get; }
    public Vector2 ScreenSpaceMousePosition => MouseState.Position;

    public MouseButtonEvent(MouseState mouseState, MouseButton button, int clicks)
    {
        MouseState = mouseState;
        Button = button;
        Clicks = clicks;
    }
}
