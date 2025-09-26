// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

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
        base.Draw(renderer);
        foreach (var child in children)
        {
            child.Draw(renderer);
        }
    }
}
