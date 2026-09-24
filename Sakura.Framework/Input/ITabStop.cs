// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Input;

/// <summary>
/// Implemented by a control that keyboard focus can be moved to with Tab / Shift+Tab.
/// </summary>
/// <remarks>
/// This is an interface rather than a base class so it composes with an existing hierarchy: a
/// control opts in wherever it already sits, instead of having to be re-parented under a dedicated
/// container. A tab stop must also return true from <see cref="Drawable.AcceptsFocus"/> — the
/// traversal in <see cref="InputManager.MoveFocusToNextTabStop"/> checks both, since
/// <see cref="IFocusManager.ChangeFocus"/> would reject it otherwise.
/// </remarks>
public interface ITabStop
{
    /// <summary>
    /// Whether this control should currently be offered as a tab stop. Typically tracks the
    /// control's enabled state; a disabled control stays skippable without leaving the tab order
    /// permanently.
    /// </summary>
    bool CanBeTabbedTo { get; }

    /// <summary>
    /// Orders this stop relative to its peers. Stops are visited in ascending order, and ties
    /// (including the default of <c>0</c>, which every control uses unless it says otherwise) fall
    /// back to document order — the order the drawables appear in the tree.
    /// </summary>
    /// <remarks>
    /// Only set this where tree order genuinely disagrees with visual order, e.g. an overlay added
    /// last so it draws on top but which reads first on screen. Reordering the tree is usually the
    /// better fix.
    /// </remarks>
    int TabOrder => 0;
}

/// <summary>
/// Implemented by a container that bounds Tab traversal to its own subtree, so focus cannot escape
/// it while it is active — a modal dialog, or a popup menu.
/// </summary>
public interface ITabStopScope
{
    /// <summary>
    /// Whether Tab traversal is currently confined to this subtree. Usually tracks visibility, so a
    /// hidden overlay stops trapping focus without having to be removed from the tree.
    /// </summary>
    bool TrapsTabTraversal { get; }
}
