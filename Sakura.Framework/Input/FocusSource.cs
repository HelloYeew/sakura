// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input;

/// <summary>
/// What moved focus to a drawable.
/// </summary>
public enum FocusSource
{
    /// <summary>
    /// Code, outside any press: a call made after the press that led to it has been dispatched, such as an overlay focusing its
    /// first control once it has loaded, or focus set as the app starts.
    /// </summary>
    Code,

    /// <summary>
    /// A press of a mouse button, a finger or a pen. This includes focus that goes back to its previous holder because the press
    /// landed on nothing that takes focus.
    /// </summary>
    Pointer,

    /// <summary>
    /// A key press: Tab and Shift+Tab, an arrow key in a menu, or Escape closing an overlay and handing focus back.
    /// </summary>
    Keyboard,

    /// <summary>
    /// A gamepad button press.
    /// </summary>
    Gamepad
}
