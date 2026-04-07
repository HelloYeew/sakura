// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.UserInterface;

public class BasicButton : ClickableContainer
{
    private readonly Box background;
    private readonly SpriteText spriteText;

    public Color DefaultColor { get; set; } = Color.DarkGreen;
    public Color HoverColor { get; set; } = Color.Green;

    public string Text
    {
        get => spriteText.Text;
        set => spriteText.Text = value;
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
                Text = "Button"
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
