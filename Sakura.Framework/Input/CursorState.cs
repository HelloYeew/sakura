// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input;

/// <summary>
/// The state of the mouse cursor.
/// </summary>
public enum CursorState
{
    /// <summary>
    /// The default cursor state
    /// </summary>
    Default,

    /// <summary>
    /// The cursor is a pointer, typically used for clickable elements.
    /// </summary>
    Pointer,

    /// <summary>
    /// The cursor is a text I-beam, typically used for text input fields.
    /// </summary>
    Text,

    /// <summary>
    /// The cursor is a wait indicator, typically used when an operation is in progress and the user should wait.
    /// </summary>
    Wait,

    /// <summary>
    /// The cursor is a crosshair, typically used for precision selection or drawing.
    /// </summary>
    Crosshair,

    /// <summary>
    /// The cursor is a "not allowed" symbol (circle with a slash), typically used to indicate that an action is not allowed or cannot be performed.
    /// </summary>
    NotAllowed
}
