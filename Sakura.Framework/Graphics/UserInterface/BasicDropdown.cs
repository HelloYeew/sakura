// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Reactive;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.UserInterface;

public partial class BasicDropdown<T> : Container
{
    private const float item_height = 30;

    public Reactive<T> Current { get; } = new Reactive<T>(default!);

    private readonly BasicButton header;
    private readonly ScrollableContainer menu;

    private readonly List<Drawable> menuItems = new List<Drawable>();

    /// <summary>
    /// The generated menu item drawables, in the same order as <see cref="Items"/>.
    /// </summary>
    public IReadOnlyList<Drawable> MenuItems => menuItems;

    /// <summary>
    /// The clickable header drawable that toggles the menu.
    /// </summary>
    public Drawable Header => header;

    private float maxHeight = item_height * 5;

    /// <summary>
    /// The maximum height the open menu can grow to. When the total height of the items
    /// exceeds this value, the menu is clamped to this height and becomes scrollable.
    /// </summary>
    public float MaxHeight
    {
        get => maxHeight;
        set
        {
            if (Precision.AlmostEquals(maxHeight, value))
                return;
            maxHeight = value;
            updateMenuHeight();
        }
    }

    private float contentHeight;

    private IEnumerable<T> items = Enumerable.Empty<T>();
    public IEnumerable<T> Items
    {
        get => items;
        set
        {
            items = value;
            generateMenuItems();
        }
    }

    public BasicDropdown()
    {
        AutoSizeAxes = Axes.Y;
        Width = 200;

        Children = new Drawable[]
        {
            header = new BasicButton
            {
                RelativeSizeAxes = Axes.X,
                Width = 1,
                Height = item_height,
                Text = "Select...",
                Action = toggleMenu,
                TextAnchor = Anchor.CentreLeft
            },
            menu = new ScrollableContainer
            {
                RelativeSizeAxes = Axes.X,
                Width = 1,
                Y = item_height,
                Alpha = 0
            }
        };

        header.Text = Current.Value?.ToString() ?? "Select...";
    }

    public override void LoadComplete()
    {
        base.LoadComplete();

        Current.ValueChanged += e =>
        {
            header.Text = e.NewValue?.ToString() ?? "Select...";
        };
    }

    private void toggleMenu()
    {
        if (menu.IsHidden)
            menu.Show();
        else
            menu.Hide();
    }

    private void generateMenuItems()
    {
        menu.Clear();
        menuItems.Clear();
        float currentY = 0;

        foreach (var item in Items)
        {
            var capturedItem = item;
            var dropdownItem = new BasicButton
            {
                RelativeSizeAxes = Axes.X,
                Width = 1,
                Height = item_height,
                Y = currentY,
                Text = capturedItem?.ToString() ?? "",
                Action = () =>
                {
                    Current.Value = capturedItem;
                    menu.Hide();
                },
                TextAnchor = Anchor.CentreLeft
            };

            menu.Add(dropdownItem);
            menuItems.Add(dropdownItem);
            currentY += item_height;
        }

        contentHeight = currentY;
        updateMenuHeight();
    }

    /// <summary>
    /// Scrolls the menu so the item at <paramref name="index"/> is fully visible. Useful when the
    /// menu is taller than <see cref="MaxHeight"/> and the target item would otherwise be clipped.
    /// </summary>
    public void ScrollItemIntoView(int index)
    {
        if (index < 0 || index >= menuItems.Count)
            return;
        menu.ScrollIntoView(menuItems[index]);
    }

    /// <summary>
    /// Clamps the menu's viewport height to <see cref="MaxHeight"/>. When the items don't fill
    /// the max height the menu shrinks to fit; when they exceed it the menu stays at the max
    /// height and the <see cref="ScrollableContainer"/> takes over with a scrollbar.
    /// </summary>
    private void updateMenuHeight() => menu.Height = Math.Min(contentHeight, MaxHeight);
}
