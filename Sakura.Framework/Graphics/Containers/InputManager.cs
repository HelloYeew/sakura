// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A root-level container that handles input routing and implements focus management.
/// </summary>
public class InputManager : Container, IFocusManager
{
    public Drawable? FocusedDrawable { get; private set; }

    public virtual bool ChangeFocus(Drawable? potentialFocusTarget)
    {
        if (FocusedDrawable == potentialFocusTarget)
            return true;

        if (potentialFocusTarget != null && !potentialFocusTarget.AcceptsFocus)
            return false;

        if (FocusedDrawable != null)
        {
            FocusedDrawable.HasFocus = false;
            FocusedDrawable.OnFocusLost(new FocusLostEvent());
        }

        FocusedDrawable = potentialFocusTarget;

        if (FocusedDrawable != null)
        {
            FocusedDrawable.HasFocus = true;
            FocusedDrawable.OnFocus(new FocusEvent());
        }

        return true;
    }

    public virtual void TriggerFocusContention(Drawable? triggerSource)
    {
        // For now, simply grant focus to the requester if it wants it.
        // (In a highly advanced setup, you might search the tree for the highest-depth requester)
        if (triggerSource != null && triggerSource.RequestsFocus)
        {
            ChangeFocus(triggerSource);
        }
    }

    #region Input Routing

    public override bool OnKeyDown(KeyEvent e)
    {
        // 1. Route to the focused drawable first!
        if (FocusedDrawable != null && FocusedDrawable.IsLoaded && FocusedDrawable.IsAlive)
        {
            if (FocusedDrawable.OnKeyDown(e))
                return true;
        }

        // 2. If not handled by focus, let normal propagation happen
        return base.OnKeyDown(e);
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        if (FocusedDrawable != null && FocusedDrawable.IsLoaded && FocusedDrawable.IsAlive)
        {
            if (FocusedDrawable.OnKeyUp(e))
                return true;
        }

        return base.OnKeyUp(e);
    }

    #endregion
}
