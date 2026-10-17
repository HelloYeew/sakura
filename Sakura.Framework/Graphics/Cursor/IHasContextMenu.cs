// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Graphics.UserInterface;

namespace Sakura.Framework.Graphics.Cursor;

/// <summary>
/// A drawable that shows a menu when right-clicked inside a <see cref="ContextMenuContainer"/>.
/// </summary>
public interface IHasContextMenu
{
    /// <summary>
    /// The items to show, read at the moment of the right-click so they can depend on state.
    /// Null to have no say, which leaves it to whatever is behind this drawable; empty to show
    /// nothing and stop there.
    /// </summary>
    IReadOnlyList<MenuItem>? ContextMenuItems { get; }
}
