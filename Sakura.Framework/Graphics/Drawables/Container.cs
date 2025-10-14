// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Drawables;

public class Container : Drawable
{
    private readonly List<Drawable> children = new();
    public IReadOnlyList<Drawable> Children => children;
    private Drawable? draggedChild;

    public Vector2 ChildSize
    {
        get
        {
            // Start with the container's logical size, not final screen rectangle.
            var containerSize = DrawSize;

            // Calculate padding, scaling it relative to our own size if needed.
            MarginPadding relativePadding = Padding;
            if ((RelativeSizeAxes & Axes.X) != 0)
            {
                relativePadding.Left *= containerSize.X;
                relativePadding.Right *= containerSize.X;
            }
            if ((RelativeSizeAxes & Axes.Y) != 0)
            {
                relativePadding.Top *= containerSize.Y;
                relativePadding.Bottom *= containerSize.Y;
            }

            // Subtract the total scaled padding to get the area for children.
            return new Vector2(containerSize.X - relativePadding.Total.X, containerSize.Y - relativePadding.Total.Y);
        }
    }

    public void Add(Drawable drawable)
    {
        // A drawable cannot be its own parent.
        if (drawable == this)
            throw new InvalidOperationException("A container cannot be added to itself.");

        // A drawable cannot be added to one of its own children, as this would create a circular dependency.
        // To check, we walk up the hierarchy from the potential parent ('this'). If we find the `drawable`
        // we're trying to add, it means 'this' is a descendant of `drawable`.
        for (var p = Parent; p != null; p = p.Parent)
        {
            if (p == drawable)
                throw new InvalidOperationException("Cannot add an ancestor drawable as a child. This would create a circular dependency.");
        }

        if (drawable.Parent != null)
            drawable.Parent.Remove(drawable);

        drawable.Parent = this;
        children.Add(drawable);

        Invalidate(InvalidationFlags.DrawInfo);

        if (IsLoaded)
        {
            drawable.Load();
            drawable.LoadComplete();
            drawable.Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public void Remove(Drawable drawable)
    {
        if (children.Remove(drawable))
        {
            drawable.Parent = null;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public override void Update()
    {
        // Check whether our layout was dirty before base.Update() is called, as it will clear our invalidation flags.
        bool layoutWasInvalidated = (Invalidation & InvalidationFlags.DrawInfo) != 0;

        // Base update to call UpdateTransforms() on this container if it was invalid.
        base.Update();

        if (layoutWasInvalidated)
        {
            foreach (var child in children)
            {
                child.Invalidate(InvalidationFlags.DrawInfo, false);
            }
        }

        foreach (var child in children)
        {
            child.Update();
        }
    }

    public override void Draw(IRenderer renderer)
    {
        base.Draw(renderer);

        foreach (var child in children.OrderBy(c => c.Depth))
        {
            child.Draw(renderer);
        }
    }

    public override void Load()
    {
        base.Load();
        foreach (var child in children)
        {
            child.Load();
        }
    }

    public override void LoadComplete()
    {
        base.LoadComplete();
        foreach (var child in children)
        {
            child.LoadComplete();
        }
    }

    #region Event Propagation

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        // Propagate in reverse draw order to handle top-most drawables first.
        foreach (var c in children.OrderByDescending(d => d.Depth))
        {
            if (c.Contains(e.ScreenSpaceMousePosition) && c.OnMouseDown(e))
            {
                // This child handled the event, so it becomes our potential drag target.
                draggedChild = c;
                return true;
            }
        }

        return false;
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        // If a drag was in progress, only the dragged child should receive the OnMouseUp event.
        if (draggedChild != null)
        {
            bool result = draggedChild.OnMouseUp(e);
            draggedChild = null; // The drag operation concludes.
            return result;
        }

        return children.Any(c => c.OnMouseUp(e));
    }

    public override bool OnMouseMove(MouseEvent e)
    {
        // If a drag is in progress, route the event exclusively to the dragged child.
        if (draggedChild != null)
        {
            return draggedChild.OnMouseMove(e);
        }

        // Otherwise, propagate to all children for hover updates.
        // We don't use .Any() because multiple children might need to react
        // (e.g., one losing hover, another gaining it).
        bool handled = false;
        foreach (var c in children)
        {
            if (c.OnMouseMove(e))
                handled = true;
        }

        return handled;
    }

    public override bool OnScroll(ScrollEvent e)
    {
        // Propagate to the first child that contains the mouse position.
        foreach (var c in children.OrderByDescending(d => d.Depth))
        {
            if (c.Contains(e.ScreenSpaceMousePosition) && c.OnScroll(e))
                return true;
        }
        return children.Any(c => c.Contains(e.ScreenSpaceMousePosition) && c.OnScroll(e));
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        return children.Any(c => c.OnKeyDown(e));
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        return children.Any(c => c.OnKeyUp(e));
    }

    #endregion
}
