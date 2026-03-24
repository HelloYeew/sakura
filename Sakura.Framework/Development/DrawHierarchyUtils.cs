// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Text;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Development;

public static class DrawHierarchyUtils
{
    /// <summary>
    /// Log the entire drawable hierarchy of the given app to the logger.
    /// </summary>
    /// <param name="app">The root app whose hierarchy to print.</param>
    public static void PrintAppHierarchy(App app)
    {
        var builder = new StringBuilder();
        builder.AppendLine("--- HIERARCHY DUMP ---");
        PrintHierarchy(app, builder);
        Logger.Log(builder.ToString());
        Logger.Log("Hierarchy dumped to console. Press F1 to dump again.");
    }

    /// <summary>
    /// Recursively print the drawable hierarchy starting from the given drawable, appending details to the provided <see cref="StringBuilder"/>
    /// </summary>
    /// <param name="drawable">The drawable to print (and its children recursively)</param>
    /// <param name="builder">The <see cref="StringBuilder"/> to append the hierarchy details to.</param>
    /// <param name="indent">The current indentation string for formatting the hierarchy output. This is used internally for recursive calls and should not be set by the caller.</param>
    public static void PrintHierarchy(Drawable drawable, StringBuilder builder, string indent = "")
    {
        if (drawable is FpsGraph)
            return;

        builder.AppendLine($"{indent}- {drawable.GetDisplayName()}");
        if (drawable is Container)
            builder.AppendLine($"{indent}  AutoSizeAxes: {((Container)drawable).AutoSizeAxes}");
        builder.AppendLine($"{indent}  Size: {drawable.Size} (DrawSize: {drawable.DrawSize}, RelativeSize: {drawable.RelativeSizeAxes})");
        builder.AppendLine($"{indent}  Position (Relative): {drawable.Position} (Anchor: {drawable.Anchor}, Origin: {drawable.Origin}, RelativePosition: {drawable.RelativePositionAxes})");
        builder.AppendLine($"{indent}  DrawRectangle (Absolute): {drawable.DrawRectangle}");
        builder.AppendLine($"{indent}  Alpha: {drawable.Alpha} (DrawAlpha: {drawable.DrawAlpha})");
        builder.AppendLine($"{indent}  ModelMatrix: {drawable.ModelMatrix}");
        builder.AppendLine($"{indent}  Clock: {drawable.Clock}");

        if (drawable is Container container)
        {
            builder.AppendLine($"{indent}  Children ({container.Children.Count}):");
            foreach (var child in container.Children)
            {
                PrintHierarchy(child, builder, indent + "  ");
            }
        }
    }
}
