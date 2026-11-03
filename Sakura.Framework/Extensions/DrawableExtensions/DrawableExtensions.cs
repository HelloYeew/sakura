// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Extensions.DrawableExtensions;

public static class DrawableExtensions
{
    /// <summary>
    /// Counts the number of drawables of a specific type in a drawable hierarchy.
    /// </summary>
    /// <param name="drawable">The root drawable to start counting from.</param>
    /// <typeparam name="T">The type of drawable to count.</typeparam>
    /// <returns>The number of drawables of the specified type in the hierarchy.</returns>
    public static int CountOfType<T>(Drawable drawable) where T : Drawable
    {
        int count = drawable is T ? 1 : 0;

        if (drawable is Container container)
        {
            foreach (var child in container.Children)
                count += CountOfType<T>(child);
        }

        return count;
    }

    /// <summary>
    /// Everything under <paramref name="drawable"/>, parents before their children and each container's in its order:
    /// the whole tree, so a container's internal children too (a scrollable container's bars and content holder as well
    /// as what's in it), not only what <see cref="Container.Children"/> lists.
    /// </summary>
    /// <remarks>
    /// Each container's children are read as it's reached, so the tree can change between steps without the walk
    /// failing; what's added under a container already passed isn't seen. Walked with a stack of its own, so a deep tree
    /// costs no deeper calls.
    /// </remarks>
    /// <param name="drawable">The drawable to look under; not itself returned (see <see cref="DescendantsAndSelf"/>).</param>
    /// <returns>Every drawable under it.</returns>
    public static IEnumerable<Drawable> Descendants(this Drawable drawable)
    {
        var pending = new Stack<Drawable>();
        push(pending, drawable);

        while (pending.Count > 0)
        {
            var next = pending.Pop();
            yield return next;

            push(pending, next);
        }
    }

    /// <summary>
    /// <paramref name="drawable"/>, then everything under it, as <see cref="Descendants"/> walks it.
    /// </summary>
    /// <param name="drawable">The drawable to start from.</param>
    /// <returns>It and every drawable under it.</returns>
    public static IEnumerable<Drawable> DescendantsAndSelf(this Drawable drawable)
    {
        yield return drawable;

        foreach (var descendant in drawable.Descendants())
            yield return descendant;
    }

    // A container's children, last first, so the first is popped first.
    private static void push(Stack<Drawable> pending, Drawable drawable)
    {
        if (drawable is not Container container)
            return;

        var children = container.InternalChildren;

        for (int i = children.Count - 1; i >= 0; i--)
            pending.Push(children[i]);
    }
}
