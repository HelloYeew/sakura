// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Input.Bindings;

namespace Sakura.Framework.Graphics.UserInterface.KeyBinding;

/// <summary>
/// Default implementation of <see cref="KeyBindingPanel{T}"/> that builds rows using
/// <see cref="BasicKeyBindingRow{T}"/>.
/// </summary>
/// <typeparam name="T">The action enum type.</typeparam>
public partial class BasicKeyBindingPanel<T> : KeyBindingPanel<T> where T : struct, Enum
{
    public BasicKeyBindingPanel(KeyBindingStore<T> store)
        : base(store)
    {
    }

    protected override KeyBindingRow<T> CreateRow(T action, IReadOnlyList<KeyCombination> current)
        => new BasicKeyBindingRow<T>(action, current, SlotsPerAction);
}
