// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

/// <summary>
/// A base interface for all mouse-related events.
/// </summary>
public interface IMouseEvent
{
    /// <summary>
    /// The current state of the mouse.
    /// </summary>
    MouseState MouseState { get; }

    /// <summary>
    /// The position of the mouse in screen space coordinates.
    /// </summary>
    Vector2 ScreenSpaceMousePosition { get; }
}
