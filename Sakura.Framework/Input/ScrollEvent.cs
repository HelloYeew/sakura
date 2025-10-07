// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

public readonly struct ScrollEvent : IMouseEvent
{
    public MouseState MouseState { get; }
    public Vector2 ScrollDelta { get; }
    public Vector2 ScreenSpaceMousePosition => MouseState.Position;

    public ScrollEvent(MouseState mouseState, Vector2 scrollDelta)
    {
        MouseState = mouseState;
        ScrollDelta = scrollDelta;
    }
}
