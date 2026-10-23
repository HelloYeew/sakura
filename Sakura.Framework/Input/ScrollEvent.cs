// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

public readonly struct ScrollEvent : IMouseEvent
{
    public MouseState MouseState { get; }

    /// <summary>
    /// How far the wheel turned, in notches. A trackpad gives fractions of one, which mean different distances on
    /// different platforms.
    /// </summary>
    public Vector2 ScrollDelta { get; }

    /// <summary>
    /// How far a trackpad's fingers moved, in screen space, where the platform says (macOS): the distance content
    /// moves to follow them, as it would under a drag. The same direction as <see cref="ScrollDelta"/>. Null for a wheel,
    /// and where the platform only gives notches.
    /// </summary>
    public Vector2? AbsoluteDelta { get; }

    public Vector2 ScreenSpaceMousePosition => MouseState.Position;

    public ScrollEvent(MouseState mouseState, Vector2 scrollDelta, Vector2? absoluteDelta = null)
    {
        MouseState = mouseState;
        ScrollDelta = scrollDelta;
        AbsoluteDelta = absoluteDelta;
    }
}
