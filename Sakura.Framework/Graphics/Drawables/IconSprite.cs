// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.IconUsageExtensions;
using Sakura.Framework.Graphics.Text;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A drawable that renders an icon from the icon font.
/// </summary>
public partial class IconSprite : SpriteText
{
    private IconUsage icon;
    // TODO: If we have support for multiple icon library?
    private MaterialIconStyle style = MaterialIconStyle.Outlined;

    // Material Symbols expose an optical-size (opsz) axis; by default we drive it from the render
    // size so icons stay optically balanced as they scale. Setting OpticalSize explicitly disables
    // this automatic behaviour.
    private bool autoOpticalSize = true;

    public float IconSize
    {
        get => Font.Size;
        set => Font = Font.With(size: value, opticalSize: autoOpticalSize ? value : null);
    }

    public IconUsage Icon
    {
        get => icon;
        set
        {
            if (icon == value) return;
            icon = value;
            Text = icon.ToGlyph();
        }
    }

    public MaterialIconStyle Style
    {
        get => style;
        set
        {
            if (style == value) return;
            style = value;
            updateFontFamily();
        }
    }

    /// <summary>
    /// The stroke weight of the icon, mapped onto the Material Symbols <c>wght</c> axis. Lets an icon
    /// visually match nearby text weight. Defaults to <see cref="FontWeights.Regular"/>.
    /// </summary>
    public FontWeights IconWeight
    {
        get => Enum.TryParse<FontWeights>(Font.Weight, out var w) ? w : FontWeights.Regular;
        set => Font = Font.With(weight: value.ToString());
    }

    /// <summary>
    /// Whether the icon is filled (<c>FILL</c> axis 1) or outlined (0)
    /// </summary>
    public bool Filled
    {
        get => (Font.Fill ?? 0f) >= 0.5f;
        set => Font = Font.With(fill: value ? 1f : 0f);
    }

    /// <summary>
    /// Optional emphasis adjustment via the Material Symbols <c>GRAD</c> (grade) axis (typically
    /// −50…200), useful for fine-tuning weight on dark backgrounds. Null leaves the font default.
    /// </summary>
    public float? Grade
    {
        get => Font.Grade;
        set => Font = Font.With(grade: value);
    }

    /// <summary>
    /// Explicit override for the Material Symbols <c>opsz</c> (optical size) axis. Setting this
    /// disables the automatic optical size derived from <see cref="IconSize"/>.
    /// </summary>
    public float? OpticalSize
    {
        get => Font.OpticalSize;
        set
        {
            autoOpticalSize = value == null;
            Font = value == null
                ? Font.With(opticalSize: IconSize) // revert to auto (track render size)
                : Font.With(opticalSize: value);
        }
    }

    public IconSprite()
    {
        updateFontFamily();
        IconSize = 24f;
    }

    private void updateFontFamily()
    {
        string familyName = style switch
        {
            MaterialIconStyle.Rounded => "MaterialSymbolsRounded",
            MaterialIconStyle.Sharp => "MaterialSymbolsSharp",
            _ => "MaterialSymbolsOutlined"
        };
        Font = Font.With(family: familyName);
    }
}

public enum MaterialIconStyle
{
    Outlined,
    Rounded,
    Sharp
}
