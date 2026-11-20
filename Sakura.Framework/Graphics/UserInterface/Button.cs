// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Input;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// Abstract base for all button-like controls.
/// </summary>
public abstract partial class Button : ClickableContainer, ITabStop
{
    public override bool AcceptsFocus => Enabled.Value;

    public virtual bool CanBeTabbedTo => Enabled.Value;

    public virtual int TabOrder => 0;

    /// <summary>
    /// Whether <see cref="OnHovered"/> has run with no <see cref="OnHoverLost()"/> after it, so the hover look is on.
    /// </summary>
    private bool hoverShown;

    protected override void LoadComplete()
    {
        base.LoadComplete();

        Enabled.ValueChanged += e => updateEnabled(e.NewValue);
    }

    /// <summary>
    /// Applies an enabled state, with the hover look going with it: a button turned off under the pointer drops its hover
    /// look before its disabled look goes on, and one turned back on under the pointer shows it again, since the pointer
    /// won't hover it anew.
    /// </summary>
    private void updateEnabled(bool enabled)
    {
        if (!enabled)
            hideHover();

        OnEnabledChanged(enabled);

        if (enabled && IsHovered)
            showHover();
    }

    /// <summary>
    /// Puts the hover look on, unless it's on already.
    /// </summary>
    private void showHover()
    {
        if (hoverShown)
            return;

        hoverShown = true;
        OnHovered();
    }

    /// <summary>
    /// Takes the hover look off, unless it's off already.
    /// </summary>
    private void hideHover()
    {
        if (!hoverShown)
            return;

        hoverShown = false;
        OnHoverLost();
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!HasFocus || !Enabled.Value)
            return false;

        if (e.Key != Key.Space && e.Key != Key.Enter && e.Key != Key.KeypadEnter)
            return false;

        Action?.Invoke();
        return true;
    }

    public override void OnFocus(FocusEvent e)
    {
        base.OnFocus(e);
        OnFocusGained();
    }

    public override void OnFocusLost(FocusLostEvent e)
    {
        base.OnFocusLost(e);
        OnFocusLost();
    }

    public override bool OnHover(MouseEvent e)
    {
        if (!Enabled.Value)
            return false;

        showHover();
        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        hideHover();

        if (!Enabled.Value)
            return false;

        return base.OnHoverLost(e);
    }

    /// <summary>
    /// Called when the button is hovered and enabled, or turned on while the pointer is over it. Override to apply hover
    /// visuals.
    /// </summary>
    protected virtual void OnHovered() { }

    /// <summary>
    /// Called when hover ends, or when the button is turned off while hovered, so a disabled button never keeps its hover
    /// look. Runs once for each <see cref="OnHovered"/>. Override to revert hover visuals.
    /// </summary>
    protected new virtual void OnHoverLost() { }

    /// <summary>
    /// Called when <see cref="ClickableContainer.Enabled"/> changes. Override to apply disabled visuals.
    /// </summary>
    protected virtual void OnEnabledChanged(bool enabled) { }

    /// <summary>
    /// Called when the button gains keyboard focus. Override to draw a focus indicator.
    /// </summary>
    protected virtual void OnFocusGained() { }

    /// <summary>
    /// Called when the button loses keyboard focus. Override to remove the focus indicator.
    /// </summary>
    protected new virtual void OnFocusLost() { }
}
