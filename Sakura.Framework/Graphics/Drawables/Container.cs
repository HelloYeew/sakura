// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Drawables;

public class Container : Drawable
{
    private readonly List<Drawable> children = new();
    public IReadOnlyList<Drawable> Children => children;

    public Vector2 ChildSize
    {
        get
        {
            // Start with the container's full drawable size.
            var containerSize = DrawRectangle.Size;

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
                child.Invalidate(InvalidationFlags.DrawInfo);
            }
        }

        foreach (var child in children)
        {
            child.Update();
        }
    }

    public override void Draw(IRenderer renderer)
    {
        foreach (var child in children.OrderBy(c => c.Depth))
        {
            child.Draw(renderer);
        }
    }
}
