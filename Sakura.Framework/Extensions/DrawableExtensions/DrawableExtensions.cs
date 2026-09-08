// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

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
}
