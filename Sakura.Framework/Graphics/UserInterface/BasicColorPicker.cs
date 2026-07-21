// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// A basic implementation of <see cref="ColorPicker"/>
/// </summary>
public partial class BasicColorPicker : ColorPicker
{
    public Color PanelColor { get; init; } = Color.FromArgb(255, 30, 30, 30);

    public BasicColorPicker()
    {
        Masking = true;
        CornerRadius = 8;
    }

    protected override Drawable CreateBackground() => new Box
    {
        RelativeSizeAxes = Axes.Both,
        Color = PanelColor
    };

    protected override Drawable CreateSaturationValueMarker() => new CircularContainer
    {
        Size = new Vector2(18),
        BorderThickness = 3,
        BorderColor = Color.White,
        Child = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = Color.Transparent
        }
    };

    protected override Drawable CreateHueMarker() => new Container
    {
        Size = new Vector2(10, HueBarHeight + 8),
        Masking = true,
        CornerRadius = 4,
        BorderThickness = 3,
        BorderColor = Color.White,
        Child = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = Color.Transparent
        }
    };

    protected override TextBox? CreateHexInput() => new BasicTextBox
    {
        Size = new Vector2(0, HueBarHeight + 8)
    };

    private Box? previewFill;

    protected override Drawable CreatePreview() => new Container
    {
        Size = new Vector2(64, HueBarHeight + 8),
        Masking = true,
        CornerRadius = 6,
        Child = previewFill = new Box { RelativeSizeAxes = Axes.Both }
    };

    protected override void UpdatePreview(Drawable previewDrawable, Color color)
    {
        if (previewFill != null)
            previewFill.Color = color;
    }
}
