// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.UserInterface;

public enum MenuState
{
    Closed,
    Open
}

/// <summary>
/// A list of <see cref="MenuItem"/>s, opening sub-menus from the items that have them. The base of
/// both a menu bar (a horizontal <see cref="TopLevelMenu"/>, always shown) and the pop-up menus that
/// open from it or from a <see cref="Cursor.ContextMenuContainer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on osu!framework's menu. An open pop-up menu holds keyboard focus, and losing it is
/// what closes it: a click anywhere that isn't on the menu either focuses something else or, since
/// nothing claimed it, clears focus, and either way the whole chain of open menus closes. Opening a
/// sub-menu moves focus into it, which its parent ignores.
/// </para>
/// <para>
/// Sub-menus are children of the menu that opened them and are drawn outside its bounds, so nothing
/// between a menu and the screen should mask. They stay on screen by opening the other way, or
/// sliding back in, when there is no room.
/// </para>
/// <para>
/// Keyboard, while a pop-up menu has focus: Up/Down (Left/Right in a horizontal one) move the
/// highlight, Enter uses the highlighted item, Right opens its sub-menu, Left or Escape closes the
/// current one.
/// </para>
/// </remarks>
public abstract partial class Menu : Container
{
    /// <summary>
    /// Fired when <see cref="State"/> changes.
    /// </summary>
    public event Action<MenuState>? StateChanged;

    /// <summary>
    /// Which way the items run.
    /// </summary>
    public FlowDirection Direction { get; }

    /// <summary>
    /// Whether this is a menu bar: always open, never focused, opening its sub-menus on click and
    /// then switching between them as the cursor moves along it.
    /// </summary>
    public bool TopLevelMenu { get; }

    /// <summary>
    /// How long the cursor has to rest on an item before its sub-menu opens, in milliseconds.
    /// </summary>
    protected double HoverOpenDelay { get; set; } = 100;

    /// <summary>
    /// The narrowest a vertical menu gets, however short its labels are.
    /// </summary>
    public float MinWidth { get; set; }

    /// <summary>
    /// The items, built into <see cref="DrawableMenuItem"/>s in this order.
    /// </summary>
    public IReadOnlyList<MenuItem> Items
    {
        get => items;
        set
        {
            closeSubmenu();
            itemsLayer.Clear();
            drawableItems.Clear();
            highlighted = null;

            items = value;

            foreach (var item in value)
            {
                var drawable = CreateDrawableMenuItem(item);
                drawable.MenuDirection = Direction;
                drawable.Clicked = itemClicked;
                drawable.Hovered = itemHovered;

                drawableItems.Add(drawable);
                itemsLayer.Add(drawable);
            }
        }
    }

    /// <summary>
    /// The item drawables, in order.
    /// </summary>
    public IReadOnlyList<DrawableMenuItem> DrawableItems => drawableItems;

    /// <summary>
    /// The sub-menu, once one has been opened. It stays around closed between uses.
    /// </summary>
    public Menu? Submenu => submenu;

    public Color BackgroundColor
    {
        get => background.Color;
        set => background.Color = value;
    }

    /// <summary>
    /// What the background is drawn in, for a styled menu to round its corners or give it a shadow. The
    /// menu itself isn't masked, since its sub-menus are drawn outside it.
    /// </summary>
    protected readonly Container BackgroundContainer;

    private readonly Box background;
    private readonly Container itemsLayer;
    private readonly List<DrawableMenuItem> drawableItems = new List<DrawableMenuItem>();

    private IReadOnlyList<MenuItem> items = Array.Empty<MenuItem>();

    private Menu? parentMenu;
    private Menu? submenu;

    /// <summary>
    /// The item in <see cref="parentMenu"/> this sub-menu is showing the items of.
    /// </summary>
    private DrawableMenuItem? triggeringItem;

    private DrawableMenuItem? highlighted;
    private ScheduledTask? hoverOpenTask;

    private MenuState state;

    protected Menu(FlowDirection direction, bool topLevelMenu = false)
    {
        Direction = direction;
        TopLevelMenu = topLevelMenu;
        state = topLevelMenu ? MenuState.Open : MenuState.Closed;

        AddInternal(BackgroundContainer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Child = background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                Color = Color.Black
            }
        });

        AddInternal(itemsLayer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One
        });

        if (!topLevelMenu)
            Alpha = 0;
    }

    public MenuState State
    {
        get => state;
        set
        {
            // A menu bar is never closed; closing it means closing whatever it has open.
            if (TopLevelMenu)
            {
                if (value == MenuState.Closed)
                    closeSubmenu();

                return;
            }

            if (state == value)
                return;

            state = value;

            if (value == MenuState.Open)
            {
                AnimateOpen();
                takeFocus();
            }
            else
            {
                closeSubmenu();
                setHighlight(null);
                AnimateClose();

                if (HasFocus)
                    GetContainingFocusManager()?.ChangeFocus(parentMenu is { AcceptsFocus: true } ? parentMenu : null);
            }

            StateChanged?.Invoke(value);
        }
    }

    public void Open() => State = MenuState.Open;

    public void Close() => State = MenuState.Closed;

    public void Toggle() => State = State == MenuState.Open ? MenuState.Closed : MenuState.Open;

    protected virtual void AnimateOpen() => Show();

    protected virtual void AnimateClose() => Hide();

    protected abstract DrawableMenuItem CreateDrawableMenuItem(MenuItem item);

    protected abstract Menu CreateSubMenu();

    private void takeFocus()
    {
        if (!TopLevelMenu && State == MenuState.Open)
            GetContainingFocusManager()?.ChangeFocus(this);
    }

    #region Layout

    public override void Update()
    {
        base.Update();

        layoutItems();

        if (submenu?.State == MenuState.Open)
            positionSubmenu(submenu);
    }

    /// <summary>
    /// Stacks the items and sizes the menu around them. A vertical menu gives every item the widest
    /// one's width so the highlight spans the menu; a horizontal one gives them a common height.
    /// </summary>
    private void layoutItems()
    {
        bool vertical = Direction == FlowDirection.Vertical;

        float across = vertical ? MinWidth : 0;

        foreach (var item in drawableItems)
        {
            var natural = item.NaturalSize;
            across = Math.Max(across, vertical ? natural.X : natural.Y);
        }

        float along = 0;

        foreach (var item in drawableItems)
        {
            var natural = item.NaturalSize;

            if (vertical)
            {
                item.Position = new Vector2(0, along);
                item.Size = new Vector2(across, natural.Y);
                along += natural.Y;
            }
            else
            {
                item.Position = new Vector2(along, 0);
                item.Size = new Vector2(natural.X, across);
                along += natural.X;
            }
        }

        // Whichever axis the caller sized relatively (a menu bar across the top of the screen) stays theirs.
        float width = (RelativeSizeAxes & Axes.X) != 0 ? Size.X : vertical ? across : along;
        float height = (RelativeSizeAxes & Axes.Y) != 0 ? Size.Y : vertical ? along : across;

        Size = new Vector2(width, height);
    }

    /// <summary>
    /// Puts <paramref name="menu"/> beside (vertical) or below (horizontal) the item that opened it,
    /// opening the other way or sliding back in where it would leave the screen.
    /// </summary>
    private void positionSubmenu(Menu menu)
    {
        var item = menu.triggeringItem;

        if (item == null)
            return;

        var bounds = screenBoundsInLocalSpace();
        var size = menu.DrawSize;

        Vector2 position;

        if (Direction == FlowDirection.Vertical)
        {
            position = new Vector2(item.X + item.DrawWidth, item.Y);

            if (position.X + size.X > bounds.X + bounds.Width && item.X - size.X >= bounds.X)
                position.X = item.X - size.X;

            if (position.Y + size.Y > bounds.Y + bounds.Height)
                position.Y = Math.Max(bounds.Y, bounds.Y + bounds.Height - size.Y);
        }
        else
        {
            position = new Vector2(item.X, item.Y + item.DrawHeight);

            if (position.X + size.X > bounds.X + bounds.Width)
                position.X = Math.Max(bounds.X, bounds.X + bounds.Width - size.X);

            if (position.Y + size.Y > bounds.Y + bounds.Height && item.Y - size.Y >= bounds.Y)
                position.Y = item.Y - size.Y;
        }

        menu.Position = position;
    }

    /// <summary>
    /// The screen — the outermost ancestor's bounds — in this menu's local space.
    /// </summary>
    private RectangleF screenBoundsInLocalSpace()
    {
        Drawable root = this;

        while (root.Parent != null)
            root = root.Parent;

        if (root == this)
            return new RectangleF(float.MinValue / 2, float.MinValue / 2, float.MaxValue, float.MaxValue);

        var screen = root.DrawRectangle;
        var topLeft = ToLocalSpace(new Vector2(screen.X, screen.Y));
        var bottomRight = ToLocalSpace(new Vector2(screen.X + screen.Width, screen.Y + screen.Height));

        return new RectangleF(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    #endregion

    #region Sub-menus

    private void openSubmenuFor(DrawableMenuItem item)
    {
        cancelHoverOpen();

        if (submenu == null)
        {
            submenu = CreateSubMenu();
            submenu.parentMenu = this;
            submenu.StateChanged += onSubmenuStateChanged;
            AddInternal(submenu);
        }

        if (submenu.triggeringItem != item || submenu.State == MenuState.Closed)
        {
            if (submenu.triggeringItem != null)
                submenu.triggeringItem.Selected = false;

            submenu.triggeringItem = item;
            submenu.Items = item.Item.Items;
        }

        item.Selected = true;
        positionSubmenu(submenu);

        if (submenu.State == MenuState.Open)
            submenu.takeFocus();
        else
            submenu.Open();
    }

    private void closeSubmenu()
    {
        cancelHoverOpen();
        submenu?.Close();
    }

    private void onSubmenuStateChanged(MenuState newState)
    {
        if (newState == MenuState.Closed && submenu?.triggeringItem != null)
            submenu.triggeringItem.Selected = false;
    }

    private void cancelHoverOpen()
    {
        if (hoverOpenTask != null)
            Scheduler.Cancel(hoverOpenTask);

        hoverOpenTask = null;
    }

    /// <summary>
    /// Closes this menu and every menu that led to it, as using an item or clicking away does.
    /// </summary>
    private void closeAll()
    {
        Close();
        parentMenu?.closeAll();
    }

    #endregion

    #region Items

    private void itemClicked(DrawableMenuItem item)
    {
        if (!item.IsActionable)
            return;

        if (item.HasSubmenu)
        {
            // A menu bar's items toggle their menu, as in every desktop menu bar.
            if (TopLevelMenu && submenu?.State == MenuState.Open && submenu.triggeringItem == item)
            {
                closeSubmenu();
                return;
            }

            openSubmenuFor(item);
            return;
        }

        // Closed first, so an action that opens something focusable (a dialog) keeps the focus it takes.
        var action = item.Item.Action;
        closeAll();
        action?.Invoke();
    }

    private void itemHovered(DrawableMenuItem item)
    {
        setHighlight(item);

        if (TopLevelMenu)
        {
            // Once one of a bar's menus is open, the others open just by moving along the bar.
            if (submenu?.State == MenuState.Open && submenu.triggeringItem != item)
            {
                if (item.HasSubmenu && item.IsActionable)
                    openSubmenuFor(item);
                else
                    closeSubmenu();
            }

            return;
        }

        cancelHoverOpen();

        if (submenu?.State == MenuState.Open && submenu.triggeringItem == item)
            return;

        hoverOpenTask = Scheduler.AddDelayed(() =>
        {
            hoverOpenTask = null;

            if (!item.IsHovered || State != MenuState.Open)
                return;

            if (item.HasSubmenu && item.IsActionable)
                openSubmenuFor(item);
            else
                closeSubmenu();
        }, HoverOpenDelay);
    }

    private void setHighlight(DrawableMenuItem? item)
    {
        if (highlighted == item)
            return;

        if (highlighted != null)
            highlighted.Highlighted = false;

        highlighted = item;

        if (highlighted != null)
            highlighted.Highlighted = true;
    }

    private void moveHighlight(int delta)
    {
        if (drawableItems.Count == 0)
            return;

        int start = highlighted == null ? (delta > 0 ? -1 : drawableItems.Count) : drawableItems.IndexOf(highlighted);

        for (int i = 1; i <= drawableItems.Count; i++)
        {
            int index = ((start + delta * i) % drawableItems.Count + drawableItems.Count) % drawableItems.Count;

            if (drawableItems[index].IsActionable)
            {
                setHighlight(drawableItems[index]);
                return;
            }
        }
    }

    #endregion

    #region Input

    public override bool AcceptsFocus => !TopLevelMenu && State == MenuState.Open;

    /// <summary>
    /// Keeps an open menu on the focus stack while its sub-menu has focus, so closing the sub-menu
    /// hands focus straight back.
    /// </summary>
    public override bool RequestsFocus => !TopLevelMenu && State == MenuState.Open;

    public override bool HandlePositionalInput => State == MenuState.Open;

    public override bool HandleNonPositionalInput => State == MenuState.Open;

    /// <summary>
    /// A sub-menu sits outside this menu's bounds, and positional input only reaches children of a
    /// container the cursor is over, so this menu also answers for the area of an open sub-menu.
    /// </summary>
    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
        base.ReceivePositionalInputAt(screenSpacePos) || (submenu?.State == MenuState.Open && submenu.ReceivePositionalInputAt(screenSpacePos));

    public override void OnFocusLost(FocusLostEvent e)
    {
        base.OnFocusLost(e);

        if (TopLevelMenu || State != MenuState.Open)
            return;

        // Focus moving into our own sub-menu isn't leaving.
        if (submenu?.State == MenuState.Open)
            return;

        // A click on the menus themselves that nothing claimed (a separator, a disabled item) cleared
        // focus on its way through. Take it back rather than close under the cursor.
        if (cursorOverChain())
        {
            Scheduler.Add(() =>
            {
                if (State == MenuState.Open && !HasFocus && submenu?.State != MenuState.Open)
                    takeFocus();
            });

            return;
        }

        closeAll();
    }

    /// <summary>
    /// Whether the cursor is over this menu or any menu that led to it, not counting sub-menus.
    /// </summary>
    private bool cursorOverChain()
    {
        var manager = GetContainingInputManager();

        if (manager == null)
            return false;

        var position = manager.CurrentState.MousePosition;

        for (var menu = this; menu != null; menu = menu.parentMenu)
        {
            if (menu.Contains(position))
                return true;
        }

        return false;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        base.OnMouseDown(e);

        // Nothing behind an open menu should see a press on it, even between items.
        return true;
    }

    public override bool OnHover(MouseEvent e) => true;

    public override bool OnScroll(ScrollEvent e) => true;

    public override bool OnKeyDown(KeyEvent e)
    {
        if (TopLevelMenu || State != MenuState.Open)
            return false;

        bool vertical = Direction == FlowDirection.Vertical;

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                return true;

            case Key.Up when vertical:
            case Key.Left when !vertical:
                moveHighlight(-1);
                return true;

            case Key.Down when vertical:
            case Key.Right when !vertical:
                moveHighlight(1);
                return true;

            case Key.Right when vertical:
                if (highlighted is { HasSubmenu: true, IsActionable: true })
                {
                    openSubmenuFor(highlighted);
                    submenu?.moveHighlight(1);
                }

                return true;

            case Key.Left when vertical:
                // Only a sub-menu backs out this way; the first menu in the chain has nowhere to go.
                if (parentMenu is { TopLevelMenu: false })
                    Close();

                return true;

            case Key.Enter:
            case Key.KeypadEnter:
                if (highlighted != null)
                {
                    itemClicked(highlighted);

                    if (highlighted?.HasSubmenu == true)
                        submenu?.moveHighlight(1);
                }

                return true;
        }

        return false;
    }

    #endregion
}

/// <summary>
/// How a <see cref="MenuItem"/> looks in a <see cref="Menu"/>. Subclasses draw it; this handles the
/// pointer and keeps the state the menu sets.
/// </summary>
public abstract partial class DrawableMenuItem : Container
{
    public MenuItem Item { get; }

    internal Action<DrawableMenuItem>? Clicked;
    internal Action<DrawableMenuItem>? Hovered;

    /// <summary>
    /// The direction of the menu this item is in.
    /// </summary>
    public FlowDirection MenuDirection { get; internal set; } = FlowDirection.Vertical;

    public bool HasSubmenu => Item.Items.Count > 0;

    /// <summary>
    /// Whether using the item does anything: not a separator, enabled, and with an action or a
    /// sub-menu.
    /// </summary>
    public bool IsActionable => Item is not MenuSeparator && Item.Enabled.Value && (HasSubmenu || Item.Action != null);

    private bool highlighted;

    /// <summary>
    /// Whether the item is the one the pointer or the keyboard is on.
    /// </summary>
    public bool Highlighted
    {
        get => highlighted;
        internal set
        {
            if (highlighted == value)
                return;

            highlighted = value;
            UpdateState();
        }
    }

    private bool selected;

    /// <summary>
    /// Whether this item's sub-menu is the one open.
    /// </summary>
    public bool Selected
    {
        get => selected;
        internal set
        {
            if (selected == value)
                return;

            selected = value;
            UpdateState();
        }
    }

    /// <summary>
    /// The size the item wants. A vertical menu widens every item to the widest; a horizontal one
    /// gives them all the tallest height.
    /// </summary>
    public abstract Vector2 NaturalSize { get; }

    protected DrawableMenuItem(MenuItem item)
    {
        Item = item;

        item.Text.ValueChanged += onTextChanged;
        item.Enabled.ValueChanged += onEnabledChanged;

        if (item is ToggleMenuItem toggle)
            toggle.State.ValueChanged += onToggleChanged;
    }

    private void onTextChanged(ValueChangedEvent<string> e) => OnTextChanged(e.NewValue);

    private void onEnabledChanged(ValueChangedEvent<bool> e) => UpdateState();

    private void onToggleChanged(ValueChangedEvent<bool> e) => UpdateState();

    /// <summary>
    /// Called when <see cref="MenuItem.Text"/> changes.
    /// </summary>
    protected virtual void OnTextChanged(string text)
    {
    }

    /// <summary>
    /// Restyles the item for <see cref="Highlighted"/>, <see cref="Selected"/>,
    /// <see cref="IsActionable"/> and, for a <see cref="ToggleMenuItem"/>, its state.
    /// </summary>
    protected abstract void UpdateState();

    protected override void LoadComplete()
    {
        base.LoadComplete();
        UpdateState();
    }

    public override bool OnHover(MouseEvent e)
    {
        Hovered?.Invoke(this);
        return true;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        base.OnMouseDown(e);
        return true;
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        if (e.Button == MouseButton.Left)
            Clicked?.Invoke(this);

        return true;
    }

    protected override void Dispose(bool isDisposing)
    {
        Item.Text.ValueChanged -= onTextChanged;
        Item.Enabled.ValueChanged -= onEnabledChanged;

        if (Item is ToggleMenuItem toggle)
            toggle.State.ValueChanged -= onToggleChanged;

        base.Dispose(isDisposing);
    }
}
