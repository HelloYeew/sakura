// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

public readonly struct MouseEvent : IMouseEvent
{
    public MouseState MouseState { get; }
    public Vector2 ScreenSpaceMousePosition => MouseState.Position;

    public MouseEvent(MouseState mouseState)
    {
        MouseState = mouseState;
    }
}
