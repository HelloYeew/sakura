// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Input;

namespace Sakura.Framework.Graphics.Cursor;

/// <summary>
/// Implemented by the drawable a <see cref="CursorContainer"/> draws, so that it can be told when
/// the cursor's state changes.
/// </summary>
public interface ICursorDrawable
{
    /// <summary>
    /// Called whenever <see cref="Platform.IWindow.CursorState"/> changes, and once on load.
    /// </summary>
    void ChangeCursor(CursorState state);

    /// <summary>
    /// Called when a mouse button goes down, wherever the pointer is and whatever is under it. Does
    /// nothing unless implemented.
    /// </summary>
    /// <remarks>
    /// The cursor drawable cannot find this out for itself. It is drawn at the pointer but moved there
    /// a frame late, and its box starts exactly at the tip, so a press made on the move usually lands
    /// outside it. The container fills the window and is told about every press.
    /// </remarks>
    void ButtonPressed(MouseButton button)
    {
    }
}
