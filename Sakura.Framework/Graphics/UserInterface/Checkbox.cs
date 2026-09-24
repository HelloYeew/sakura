// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Input;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// Abstract base for checkbox controls.
/// </summary>
public abstract partial class Checkbox : ClickableContainer, ITabStop
{
    /// <summary>
    /// The current checked state.
    /// </summary>
    public ReactiveBool Current { get; } = new ReactiveBool(false);

    public override bool AcceptsFocus => Enabled.Value;

    public virtual bool CanBeTabbedTo => Enabled.Value;

    public virtual int TabOrder => 0;

    protected Checkbox()
    {
        Action = () => Current.Value = !Current.Value;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        OwnReactive(Current);
        OwnReactive(Enabled);

        Current.BindValueChanged(e => OnCheckChanged(e.NewValue), true);
        Enabled.BindValueChanged(e => OnEnabledChanged(e.NewValue), true);
    }

    public override bool OnHover(MouseEvent e)
    {
        if (!Enabled.Value) return false;
        OnHovered();
        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        if (!Enabled.Value) return false;
        OnHoverLost();
        return base.OnHoverLost(e);
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!HasFocus || !Enabled.Value)
            return false;

        if (e.Key != Key.Space)
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

    /// <summary>
    /// Called when the checked state changes. Override to animate visuals.
    /// </summary>
    protected virtual void OnCheckChanged(bool isChecked) { }

    /// <summary>
    /// Called when hover begins and checkbox is enabled.
    /// </summary>
    protected virtual void OnHovered() { }

    /// <summary>
    /// Called when hover ends and checkbox is enabled.
    /// </summary>
    protected new virtual void OnHoverLost() { }

    /// <summary>
    /// Called when <see cref="ClickableContainer.Enabled"/> changes.
    /// </summary>
    protected virtual void OnEnabledChanged(bool enabled) { }

    /// <summary>
    /// Called when the checkbox gains keyboard focus. Override to draw a focus indicator.
    /// </summary>
    protected virtual void OnFocusGained() { }

    /// <summary>
    /// Called when the checkbox loses keyboard focus. Override to remove the focus indicator.
    /// </summary>
    protected virtual void OnFocusLost() { }
}
