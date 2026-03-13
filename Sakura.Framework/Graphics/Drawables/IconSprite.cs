// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A drawable that renders an icon from the icon font.
/// </summary>
public class IconSprite : SpriteText
{
    private IconUsage icon;
    // TODO: If we have support for multiple icon library?
    private MaterialIconStyle style = MaterialIconStyle.Outlined;

    public float IconSize
    {
        get => Font.Size;
        set => Font = Font.With(size: value);
    }

    public IconUsage Icon
    {
        get => icon;
        set
        {
            if (icon == value) return;
            icon = value;
            Text = char.ConvertFromUtf32((int)icon);
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
