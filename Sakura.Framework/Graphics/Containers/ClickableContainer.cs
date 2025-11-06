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
public class ClickableContainer : Container
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

    public readonly ReactiveBool Enabled = new ReactiveBool();

    public override bool OnClick(MouseButtonEvent e)
    {
        if (!Enabled.Value)
            return false;

        Action?.Invoke();
        return true;
    }
}
