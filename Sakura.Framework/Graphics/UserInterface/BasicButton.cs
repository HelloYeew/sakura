// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.UserInterface;

public class BasicButton : ClickableContainer
{
    private readonly Box background;
    private readonly SpriteText spriteText;
    private readonly float textSize = 16;
    private Color defaultColor = Color.DarkGreen;

    public Color DefaultColor
    {
        get => defaultColor;
        set
        {
            background.Color = value;
            defaultColor = value;
        }
    }

    public Color HoverColor { get; set; } = Color.Green;

    public string Text
    {
        get => spriteText.Text;
        set => spriteText.Text = value;
    }

    public float TextSize
    {
        get => textSize;
        set => spriteText.Font = spriteText.Font.With(size: value);
    }

    public BasicButton()
    {
        Size = new Vector2(100, 30);

        Children = new Drawable[]
        {
            background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = DefaultColor
            },
            spriteText = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "",
                Font = FontUsage.Default.With(size: TextSize)
            }
        };

        Enabled.ValueChanged += enabled =>
        {
            background.FadeToColour(enabled.NewValue ? DefaultColor : DefaultColor.Darken(0.5f), 100, Easing.OutQuint);
        };
    }

    public override bool OnHover(MouseEvent e)
    {
        if (!Enabled.Value)
            return false;
        background.Color = HoverColor;
        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        if (!Enabled.Value)
            return false;
        background.Color = DefaultColor;
        return base.OnHoverLost(e);
    }
}
