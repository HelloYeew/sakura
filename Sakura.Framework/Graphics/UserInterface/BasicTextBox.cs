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
/// A ready-to-use text box with a green colour scheme.
/// For a custom-styled text box, extend <see cref="TextBox"/> directly.
/// </summary>
public partial class BasicTextBox : TextBox
{
    private Box BackgroundBox => (Box)Background!;

    private Color backgroundColour = Color.Green;

    /// <summary>
    /// Background colour while unfocused.
    /// </summary>
    public Color BackgroundColour
    {
        get => backgroundColour;
        set
        {
            backgroundColour = value;
            if (!HasFocus)
                BackgroundBox.Color = value;
        }
    }

    /// <summary>
    /// Background color while focused. Defaults to a lightened <see cref="BackgroundColour"/>.
    /// </summary>
    public Color? BackgroundFocusedColour { get; set; }

    private Color effectiveFocusedColour => BackgroundFocusedColour ?? backgroundColour.Lighten(0.3f);

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
        Color = backgroundColour
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

    protected override void OnFocusGained() => BackgroundBox.FadeToColour(effectiveFocusedColour, 150, Easing.OutQuint);

    protected override void OnFocusLost() => BackgroundBox.FadeToColour(backgroundColour, 150, Easing.OutQuint);
}
