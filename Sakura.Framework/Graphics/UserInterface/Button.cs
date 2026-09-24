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

    protected override void LoadComplete()
    {
        base.LoadComplete();

        Enabled.ValueChanged += e => OnEnabledChanged(e.NewValue);
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

        OnHovered();
        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        if (!Enabled.Value)
            return false;

        OnHoverLost();
        return base.OnHoverLost(e);
    }

    /// <summary>
    /// Called when the button is hovered and enabled. Override to apply hover visuals.
    /// </summary>
    protected virtual void OnHovered() { }

    /// <summary>
    /// Called when hover ends and button is enabled. Override to revert hover visuals.
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
