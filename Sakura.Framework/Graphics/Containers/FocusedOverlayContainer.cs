// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Input;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// An overlay container that eagerly holds keyboard focus and blocks key events from passing through.
/// </summary>
public abstract class FocusedOverlayContainer : OverlayContainer
{
    /// <summary>
    /// Whether we should block keyboard input from reaching underlying drawables.
    /// </summary>
    protected virtual bool BlockNonPositionalInput => true;

    public override bool OnKeyDown(KeyEvent e)
    {
        if (State == Visibility.Visible && BlockNonPositionalInput)
        {
            base.OnKeyDown(e);
            return true;
        }
        return base.OnKeyDown(e);
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        if (State == Visibility.Visible && BlockNonPositionalInput)
        {
            base.OnKeyUp(e);
            return true;
        }
        return base.OnKeyUp(e);
    }
}
