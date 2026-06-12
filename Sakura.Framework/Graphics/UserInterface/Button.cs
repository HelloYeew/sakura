// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Input;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// Abstract base for all button-like controls.
/// </summary>
public abstract partial class Button : ClickableContainer
{
    public override void LoadComplete()
    {
        base.LoadComplete();

        Enabled.ValueChanged += e => OnEnabledChanged(e.NewValue);
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
}
