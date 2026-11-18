// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A container that can set action on click events.
/// </summary>
public partial class ClickableContainer : Container
{
    private Action action;

    public Action Action
    {
        get => action;
        set
        {
            action = value;
            Enabled.Value = action != null;
        }
    }

    /// <summary>
    /// Whether clicking does anything. Setting <see cref="Action"/> sets it. While it is false, a press is not taken
    /// and reaches whatever is behind, so a drag that starts on a disabled control still scrolls the list it sits in.
    /// </summary>
    public readonly ReactiveBool Enabled = new ReactiveBool();

    public override bool OnClick(MouseButtonEvent e) => TriggerClick();

    public bool TriggerClick()
    {
        if (!Enabled.Value)
            return false;

        Action?.Invoke();
        return true;
    }

    public override bool OnHover(MouseEvent e)
    {
        return true;
    }
}
