// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// One entry in a <see cref="Menu"/>: something to do, a sub-menu to open, or both labels for a
/// <see cref="ToggleMenuItem"/>. Plain data; the menu builds a <see cref="DrawableMenuItem"/> for it.
/// </summary>
public class MenuItem
{
    /// <summary>
    /// The label.
    /// </summary>
    public Reactive<string> Text { get; } = new Reactive<string>(string.Empty);

    /// <summary>
    /// What clicking the item does. Ignored when the item has <see cref="Items"/>, which open instead.
    /// </summary>
    public Action? Action { get; set; }

    /// <summary>
    /// A sub-menu, opened from this item. Empty for an item that just does something.
    /// </summary>
    public IReadOnlyList<MenuItem> Items { get; set; } = Array.Empty<MenuItem>();

    /// <summary>
    /// Whether the item can be used. A disabled item is shown dimmed and does nothing.
    /// </summary>
    public ReactiveBool Enabled { get; } = new ReactiveBool(true);

    /// <summary>
    /// A hint shown at the far side of the item, such as "Ctrl+Z". Display only: the key itself has
    /// to be handled wherever the action lives, so it also works with the menu closed.
    /// </summary>
    public string? ShortcutText { get; set; }

    public MenuItem(string text, Action? action = null)
    {
        Text.Value = text;
        Action = action;
    }

    public MenuItem(string text, IReadOnlyList<MenuItem> items)
    {
        Text.Value = text;
        Items = items;
    }
}

/// <summary>
/// A divider between groups of items. Never actionable.
/// </summary>
public class MenuSeparator : MenuItem
{
    public MenuSeparator()
        : base(string.Empty)
    {
        Enabled.Value = false;
    }
}

/// <summary>
/// An item with an on/off state, shown with a check mark while on. Clicking flips
/// <see cref="State"/>, which is what to bind to rather than <see cref="MenuItem.Action"/>.
/// </summary>
public class ToggleMenuItem : MenuItem
{
    public ReactiveBool State { get; } = new ReactiveBool();

    public ToggleMenuItem(string text, bool initialState = false)
        : base(text)
    {
        State.Value = initialState;
        Action = () => State.Value = !State.Value;
    }
}
