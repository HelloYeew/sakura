// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Extensions.IconUsageExtensions;

/// <summary>
/// Extensions for working with <see cref="IconUsage"/> icons in text.
/// </summary>
public static class IconUsageExtensions
{
    /// <summary>
    /// Converts an <see cref="IconUsage"/> into the string it represents, so it can be placed directly
    /// in a <see cref="SpriteText.Text"/> or interpolated alongside other text. The icon font is
    /// resolved automatically via the font fallback chain, so this works even when the surrounding
    /// text uses a different (e.g. bold or custom) font.
    /// </summary>
    /// <example>
    /// <code>
    /// new SpriteText { Text = $"Play {IconUsage.PlayArrow.ToGlyph()}" };
    /// </code>
    /// </example>
    /// <param name="icon">The icon to convert.</param>
    /// <returns>The string (which may be a surrogate pair) representing the icon glyph.</returns>
    public static string ToGlyph(this IconUsage icon) => char.ConvertFromUtf32((int)icon);
}
