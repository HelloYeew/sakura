// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Primitives;
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

    /// <summary>
    /// Control which axes that this container automatically sized based on its children's sizes.
    /// </summary>
    public Axes AutoSizeAxes { get; set; } = Axes.None;

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

    public virtual void Add(Drawable drawable)
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

    public virtual void Remove(Drawable drawable)
    {
        if (children.Remove(drawable))
        {
            drawable.Parent = null;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public override void Update()
    {
        if (AutoSizeAxes != Axes.None)
            UpdateAutoSize();

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
                if (!child.IsAlive && child.RemoveWhenNotAlive)
                {
                    Remove(child);
                    continue;
                }
                child.Invalidate(InvalidationFlags.Colour, false);
            }
        }

        foreach (var child in children)
        {
            child.Update();
        }
    }

    protected virtual void UpdateAutoSize()
    {
        Vector2 maxBound = Vector2.Zero;

        foreach (var child in children)
        {
            if (!child.AlwaysPresent && child.Alpha <= 0)
                continue;

            // Calculate the child's size in pixels
            // We cannot rely on child.DrawSize here because that might be from the previous frame.
            // We must calculate it based on the current state.
            Vector2 childSize = child.Size;

            // Note : If a child is RelativeSize on the same axis we are AutoSizing,
            // we must ignore it to prevent circular dependency (or endless expansion).
            // e.g., Parent (AutoSize X) -> Child (Relative X) -> Paradox.

            if ((AutoSizeAxes & Axes.X) != 0 && (child.RelativeSizeAxes & Axes.X) != 0)
                childSize.X = 0; // Ignore relative width for auto-width calculation

            if ((AutoSizeAxes & Axes.Y) != 0 && (child.RelativeSizeAxes & Axes.Y) != 0)
                childSize.Y = 0; // Ignore relative height for auto-height calculation

            // Apply Scale
            Vector2 scaledSize = childSize * child.Scale;

            // Determine position.
            // Note: For advanced auto-sizing (handling rotations/shears), we would need full matrix bounding boxes.
            // For this implementation, we assume standard AABB flow (Position + Size + Margin).
            Vector2 childPos = child.Position;

            // If child is relatively positioned, it technically positions based on us.
            // But since we are determining our size, we treat relative positioning as 0 or ignore it
            // to avoid stability issues, unless we implement a multi-pass layout solver.
            // For simplicity: We use the raw local position.
            float right = childPos.X + scaledSize.X + child.Margin.Right;
            float bottom = childPos.Y + scaledSize.Y + child.Margin.Bottom;

            // Also account for origin offsets if necessary, but standard implementation
            // usually assumes TopLeft origin for simple flow calculations.
            // If you use Center anchors, you might need to subtract the anchor offset.
            // For now, simple bounding box extension:
            if (right > maxBound.X) maxBound.X = right;
            if (bottom > maxBound.Y) maxBound.Y = bottom;
        }

        // Apply Padding of the container itself
        maxBound += Padding.Total;

        // Apply to Size
        Vector2 currentSize = Size;

        if ((AutoSizeAxes & Axes.X) != 0)
            currentSize.X = maxBound.X;

        if ((AutoSizeAxes & Axes.Y) != 0)
            currentSize.Y = maxBound.Y;

        // Only assign if changed to prevent constant invalidation
        if (Size != currentSize)
        {
            Size = currentSize;
            // We changed size, so we must invalidate ourselves so `base.Update()` recalculates matrices
            Invalidate(InvalidationFlags.DrawInfo);
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

    protected override void OnClockChanged()
    {
        base.OnClockChanged();

        foreach (var child in children)
        {
            child.Clock = new FramedClock(Clock);
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

    public override bool OnDragDropFile(DragDropFileEvent e)
    {
        foreach (var c in children.OrderByDescending(d => d.Depth))
        {
            if (c.IsLoaded && !c.IsHidden && c.Contains(e.Position))
            {
                if (c.OnDragDropFile(e))
                    return true;
            }
        }

        return base.OnDragDropFile(e);
    }

    public override bool OnDragDropText(DragDropTextEvent e)
    {
        foreach (var c in children.OrderByDescending(d => d.Depth))
        {
            if (c.IsLoaded && !c.IsHidden && c.Contains(e.Position))
            {
                if (c.OnDragDropText(e))
                    return true;
            }
        }

        return base.OnDragDropText(e);
    }

    #endregion
}
