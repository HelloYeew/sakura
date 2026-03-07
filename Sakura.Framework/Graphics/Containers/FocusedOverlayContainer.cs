// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Input;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// An overlay container that eagerly holds keyboard focus and blocks key events from passing through.
/// </summary>
public abstract class FocusedOverlayContainer : OverlayContainer
{
    public override bool RequestsFocus => State == Visibility.Visible;
    public override bool AcceptsFocus => State == Visibility.Visible;

    /// <summary>
    /// Whether we should block keyboard input from reaching underlying drawables.
    /// </summary>
    protected virtual bool BlockNonPositionalInput => true;

    protected override void UpdateState(Visibility newState)
    {
        base.UpdateState(newState);

        switch (newState)
        {
            case Visibility.Hidden:
                if (HasFocus)
                    GetContainingFocusManager()?.ChangeFocus(null);
                break;

            case Visibility.Visible:
                GetContainingFocusManager()?.TriggerFocusContention(this);
                break;
        }
    }

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
