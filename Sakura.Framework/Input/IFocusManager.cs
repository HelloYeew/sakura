// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Input;

public interface IFocusManager
{
    /// <summary>
    /// The drawable that currently holds focus, or null if nothing is focused.
    /// </summary>
    Drawable? FocusedDrawable { get; }

    /// <summary>
    /// The current focus stack (back-most first): drawables whose focus is suspended and will be
    /// restored when the current focus is released. Exposed for inspection / debug overlays.
    /// </summary>
    IReadOnlyList<Drawable> FocusStack { get; }

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

/// <summary>
/// Implemented by a drawable (typically the app root) that owns an <see cref="InputManager"/> and
/// its authoritative <see cref="InputState"/>. Lets descendants read shared input state e.g. the
/// current mouse position — without each reconstructing it locally.
/// </summary>
public interface IInputManagerProvider
{
    /// <summary>
    /// The input manager owning the authoritative <see cref="InputState"/> for this subtree.
    /// </summary>
    InputManager InputManager { get; }
}
