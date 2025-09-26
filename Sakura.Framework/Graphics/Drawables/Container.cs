// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Drawables;

public class Container : Drawable
{
    private readonly List<Drawable> children = new();
    public IReadOnlyList<Drawable> Children => children;

    public Vector2 ChildSize => new Vector2(DrawRectangle.Width - Padding.Total.X, DrawRectangle.Height - Padding.Total.Y);

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
    }

    public void Remove(Drawable drawable)
    {
        if (children.Remove(drawable))
        {
            drawable.Parent = null;
        }
    }

    public override void Update()
    {
        base.Update();
        foreach (var child in children)
        {
            child.Update();
        }
    }

    public override void Draw(IRenderer renderer)
    {
        foreach (var child in children)
        {
            child.Draw(renderer);
        }
    }
}
