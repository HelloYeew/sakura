// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Reactive;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.UserInterface;

public partial class BasicDropdown<T> : Container, ITabStopScope
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

    /// <summary>
    /// Whether the menu is currently open. <see cref="Drawable.Show"/> / <see cref="Drawable.Hide"/>
    /// set alpha outright rather than fading, so this flips in the same frame the menu is toggled.
    /// </summary>
    public bool IsMenuOpen => !menu.IsHidden;

    /// <summary>
    /// While the menu is open, Tab is confined to this dropdown, so it cannot walk off onto the
    /// controls the menu is covering.
    /// </summary>
    public bool TrapsTabTraversal => IsMenuOpen;

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!IsMenuOpen)
            return false;

        switch (e.Key)
        {
            case Key.Escape:
                CloseMenu();
                return true;

            case Key.Up:
                return moveMenuFocus(-1);

            case Key.Down:
                return moveMenuFocus(1);

            default:
                return false;
        }
    }

    /// <summary>
    /// Opens the menu and moves focus onto the item matching <see cref="Current"/> (or the first
    /// item), so Up/Down start from the current selection rather than the top of the list.
    /// </summary>
    public void OpenMenu()
    {
        menu.Show();

        if (menuItems.Count == 0)
            return;

        int index = 0;
        int i = 0;

        foreach (var item in Items)
        {
            if (EqualityComparer<T>.Default.Equals(item, Current.Value))
            {
                index = i;
                break;
            }

            i++;
        }

        ScrollItemIntoView(index);
        GetContainingFocusManager()?.ChangeFocus(menuItems[index]);
    }

    /// <summary>
    /// Closes the menu and hands focus back to the header, so the dropdown stays a single stop in
    /// the tab order once it is shut.
    /// </summary>
    public void CloseMenu()
    {
        if (!IsMenuOpen)
            return;

        menu.Hide();
        GetContainingFocusManager()?.ChangeFocus(header);
    }

    private bool moveMenuFocus(int delta)
    {
        var focusManager = GetContainingFocusManager();

        if (focusManager == null || menuItems.Count == 0)
            return false;

        int current = focusManager.FocusedDrawable == null ? -1 : menuItems.IndexOf(focusManager.FocusedDrawable);

        // Entering from the header (or from nothing) starts at whichever end the direction implies.
        int next = current < 0
            ? (delta > 0 ? 0 : menuItems.Count - 1)
            : (current + delta + menuItems.Count) % menuItems.Count;

        ScrollItemIntoView(next);
        return focusManager.ChangeFocus(menuItems[next]);
    }

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

        OwnReactive(Current);

        Current.BindValueChanged(e => header.Text = e.NewValue?.ToString() ?? "Select...", true);
    }

    private void toggleMenu()
    {
        if (IsMenuOpen)
            CloseMenu();
        else
            OpenMenu();
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
                    CloseMenu();
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
