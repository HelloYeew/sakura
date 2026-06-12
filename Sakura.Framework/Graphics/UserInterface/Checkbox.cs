// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Input;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// Abstract base for checkbox controls.
/// </summary>
public abstract partial class Checkbox : ClickableContainer
{
    /// <summary>
    /// The current checked state.
    /// </summary>
    public ReactiveBool Current { get; } = new ReactiveBool(false);

    protected Checkbox()
    {
        Action = () => Current.Value = !Current.Value;
    }

    public override void LoadComplete()
    {
        base.LoadComplete();

        Current.ValueChanged += e => OnCheckChanged(e.NewValue);
        Enabled.ValueChanged += e => OnEnabledChanged(e.NewValue);
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
}
