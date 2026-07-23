// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// A ready-to-use text box with a green color scheme.
/// For a custom-styled text box, extend <see cref="TextBox"/> directly.
/// </summary>
public partial class BasicTextBox : TextBox
{
    private Box BackgroundBox => (Box)Background!;

    private Color backgroundColor = Color.Green;

    /// <summary>
    /// Background color while unfocused.
    /// </summary>
    public Color BackgroundColor
    {
        get => backgroundColor;
        set
        {
            backgroundColor = value;
            if (!HasFocus)
                BackgroundBox.Color = value;
        }
    }

    /// <summary>
    /// Background color while focused. Defaults to a lightened <see cref="BackgroundColor"/>.
    /// </summary>
    public Color? BackgroundFocusedColor { get; set; }

    private Color effectiveFocusedColor => BackgroundFocusedColor ?? backgroundColor.Lighten(0.3f);

    public BasicTextBox()
    {
        Size = new Vector2(200, 30);
        PlaceholderSprite.Color = Color.White;
        ImeText.Color = Color.Yellow;
        SpriteText.Color = Color.White;
    }

    protected override Drawable CreateBackground() => new Box
    {
        RelativeSizeAxes = Axes.Both,
        Size = new Vector2(1),
        Color = backgroundColor
    };

    protected override Drawable CreateCaret() => new Box
    {
        Width = 2,
        RelativeSizeAxes = Axes.Y,
        Height = 0.8f,
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Color = Color.White,
        Alpha = 0
    };

    protected override Drawable CreateSelectionBox() => new Box
    {
        RelativeSizeAxes = Axes.Y,
        Height = 0.8f,
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Color = Color.Blue,
        Alpha = 0f
    };

    protected override void OnFocusGained() => BackgroundBox.FadeToColor(effectiveFocusedColor, 150, Easing.OutQuint);

    protected override void OnFocusLost() => BackgroundBox.FadeToColor(backgroundColor, 150, Easing.OutQuint);
}
