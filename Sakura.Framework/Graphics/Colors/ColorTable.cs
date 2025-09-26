// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Sakura.Framework.Graphics.Colors;

/// <summary>
/// Specifies the known system colors.
/// Inherited from System.Drawing.ColorTable
/// </summary>
internal static class ColorTable
{
    // ReSharper disable once InconsistentNaming
    private static readonly Lazy<Dictionary<string, Color>> s_colorConstants = new Lazy<Dictionary<string, Color>>(GetColors);

    private static Dictionary<string, Color> GetColors()
    {
        var colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        FillWithProperties(colors, typeof(Color));
        FillWithProperties(colors, typeof(SystemColors));
        return colors;
    }

    private static void FillWithProperties(
        Dictionary<string, Color> dictionary,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type typeWithColors)
    {
        foreach (PropertyInfo prop in typeWithColors.GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (prop.PropertyType == typeof(Color))
                dictionary[prop.Name] = (Color)prop.GetValue(null, null)!;
        }
    }

    internal static Dictionary<string, Color> Colors => s_colorConstants.Value;

    internal static bool TryGetNamedColor(string name, out Color result) => Colors.TryGetValue(name, out result);

    internal static bool IsKnownNamedColor(string name) => Colors.TryGetValue(name, out _);
}
