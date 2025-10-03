// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

public readonly struct MouseEvent : IMouseEvent
{
    public MouseState MouseState { get; }
    public Vector2 ScreenSpaceMousePosition => MouseState.Position;
    public Vector2 Delta { get; }

    public MouseEvent(MouseState mouseState, Vector2 delta = default)
    {
        MouseState = mouseState;
        Delta = delta;
    }

    public override string ToString()
    {
        return $"MouseEvent: Position={MouseState.Position}, MouseState=[{MouseState}, Delta={Delta}]";
    }
}
