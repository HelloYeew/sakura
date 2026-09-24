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
}
