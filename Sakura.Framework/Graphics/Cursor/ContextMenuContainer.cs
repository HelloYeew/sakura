// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Cursor;

/// <summary>
/// Shows a menu at the cursor when an <see cref="IHasContextMenu"/> inside it is right-clicked.
/// </summary>
/// <remarks>
/// <para>
/// Right-clicks are caught by an invisible layer in front of the content rather than left to bubble
/// up, because a child handling the press (a button's <c>OnClick</c> fires for every button) would
/// otherwise swallow it first. The layer looks through what is under the cursor, nearest first, for
/// the first <see cref="IHasContextMenu"/> with something to say; left clicks, hover and scroll pass
/// straight through it.
/// </para>
/// <para>
/// While the menu is open, a press anywhere else closes it and goes no further, so dismissing a menu
/// over an editor never also places or selects something. A right-click elsewhere opens that spot's
/// menu instead. The menu follows its target if the target moves, and closes if it goes away.
/// </para>
/// </remarks>
public partial class ContextMenuContainer : Container
{
    private readonly Container content;
    private readonly Menu menu;

    private Drawable? target;
    private Vector2 targetLocalPosition;

    protected override Container Content => content;

    /// <summary>
    /// The menu this container shows.
    /// </summary>
    public Menu Menu => menu;

    public ContextMenuContainer()
    {
        AddInternal(content = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One
        });

        AddInternal(new PressCatcher(this)
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One
        });

        AddInternal(menu = CreateMenu());

        menu.StateChanged += state =>
        {
            if (state == MenuState.Closed)
                target = null;
        };
    }

    /// <summary>
    /// The menu to show. Override for a styled one.
    /// </summary>
    protected virtual Menu CreateMenu() => new BasicMenu(FlowDirection.Vertical);

    /// <summary>
    /// Opens the menu for whatever is under <paramref name="screenSpacePosition"/>.
    /// </summary>
    /// <returns>Whether a menu opened.</returns>
    private bool openAt(Vector2 screenSpacePosition)
    {
        var manager = GetContainingInputManager();

        if (manager == null)
            return false;

        // Built for this press before it was dispatched, nearest first.
        foreach (var drawable in manager.PositionalInputQueue)
        {
            if (drawable is not IHasContextMenu provider || !isInContent(drawable))
                continue;

            var items = provider.ContextMenuItems;

            if (items == null)
                continue;

            if (items.Count == 0)
                return false;

            target = drawable;
            targetLocalPosition = drawable.ToLocalSpace(screenSpacePosition);

            menu.Items = items;
            menu.Open();
            positionMenu();
            return true;
        }

        return false;
    }

    private bool isInContent(Drawable drawable)
    {
        for (var parent = drawable.Parent; parent != null; parent = parent.Parent)
        {
            if (parent == content)
                return true;

            if (parent == this)
                return false;
        }

        return false;
    }

    public override void Update()
    {
        base.Update();

        if (menu.State == MenuState.Open)
            positionMenu();
    }

    /// <summary>
    /// Keeps the menu at the point that was clicked on its target, pulled back inside this container
    /// where it would overflow it.
    /// </summary>
    private void positionMenu()
    {
        if (target == null)
            return;

        if (target.IsDisposed || !target.IsAlive || !isInContent(target))
        {
            menu.Close();
            return;
        }

        var position = ToLocalSpace(target.ToScreenSpace(targetLocalPosition));
        var size = menu.DrawSize;

        position.X = Math.Clamp(position.X, 0, Math.Max(0, DrawWidth - size.X));
        position.Y = Math.Clamp(position.Y, 0, Math.Max(0, DrawHeight - size.Y));

        menu.Position = position;
    }

    /// <summary>
    /// Full-size and in front of the content, drawing nothing: sees every press first.
    /// </summary>
    private partial class PressCatcher : Container
    {
        private readonly ContextMenuContainer owner;

        public PressCatcher(ContextMenuContainer owner)
        {
            this.owner = owner;
        }

        public override bool OnMouseDown(MouseButtonEvent e)
        {
            // The menu is in front of this, so a press that gets here missed it.
            if (owner.menu.State == MenuState.Open)
            {
                owner.menu.Close();

                if (e.Button == MouseButton.Right)
                    owner.openAt(e.ScreenSpaceMousePosition);

                return true;
            }

            return e.Button == MouseButton.Right && owner.openAt(e.ScreenSpaceMousePosition);
        }
    }
}
