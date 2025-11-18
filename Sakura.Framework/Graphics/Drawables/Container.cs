// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Drawables;

public class Container : Drawable
{
    private readonly List<Drawable> children = new();
    private Drawable? draggedChild;

    /// <summary>
    /// If true, children will be clipped to the bounds of this container.
    /// </summary>
    public bool Masking { get; set; }

    /// <summary>
    /// The radius of the corners when masking.
    /// </summary>
    public float CornerRadius { get; set; }

    /// <summary>
    /// The thickness of the border when masking.
    /// </summary>
    public float BorderThickness { get; set; }

    /// <summary>
    /// The color of the border when masking.
    /// </summary>
    public Color BorderColor { get; set; } = Color.White;

    public IReadOnlyList<Drawable> Children
    {
        get => children;
        set
        {
            children.Clear();
            foreach (var child in value)
            {
                Add(child);
            }
        }
    }

    public Drawable Child
    {
        set => Children = new[] { value };
    }

    public Vector2 ChildSize
    {
        get
        {
            var containerSize = DrawSize;
            return new Vector2(containerSize.X - Padding.Total.X, containerSize.Y - Padding.Total.Y);
        }
    }

    public Container()
    {
        Texture = null;
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
        drawable.Clock = new FramedClock(Clock);

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
        bool colourWasInvalidated = (Invalidation & InvalidationFlags.Colour) != 0;

        base.Update();

        if (!AlwaysPresent && Precision.AlmostEqualZero(Alpha))
            return;

        if (layoutWasInvalidated)
        {
            foreach (var child in children)
            {
                child.Invalidate(InvalidationFlags.DrawInfo, false);
            }
        }

        if (colourWasInvalidated)
        {
            foreach (var child in children)
            {
                child.Invalidate(InvalidationFlags.Colour, false);
            }
        }

        foreach (var child in children)
        {
            child.Update();
        }
    }

    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0)
            return;

        if (Masking)
            renderer.PushMask(this, CornerRadius);

        foreach (var child in children.OrderBy(c => c.Depth))
        {
            child.Draw(renderer);
        }

        if (Masking)
            renderer.PopMask(this, CornerRadius, BorderThickness, BorderColor);
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
        base.OnMouseDown(e);

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
        base.OnMouseUp(e);

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
        base.OnMouseMove(e);

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
