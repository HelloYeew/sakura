// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Input;

public interface IFocusManager
{
    /// <summary>
    /// The drawable that currently holds focus, or null if nothing is focused.
    /// </summary>
    Drawable? FocusedDrawable { get; }

    /// <summary>
    /// Changes the currently focused drawable.
    /// </summary>
    bool ChangeFocus(Drawable? potentialFocusTarget);

    /// <summary>
    /// Evaluates focus state, usually triggered when a drawable requests focus.
    /// </summary>
    void TriggerFocusContention(Drawable? triggerSource);

    /// <summary>
    /// Called by input dispatchers before processing a mouse-down event.
    /// Resets the internal "focus was claimed by this click" tracker.
    /// </summary>
    void BeginMouseDownFocusTracking();

    /// <summary>
    /// Returns true if a focusable drawable claimed focus during the most recent
    /// mouse-down dispatch (i.e., between the last <see cref="BeginMouseDownFocusTracking"/> call
    /// and now).
    /// </summary>
    bool WasFocusClaimedByLastClick { get; }
}
