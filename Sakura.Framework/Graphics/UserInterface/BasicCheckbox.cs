// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.UserInterface;

public partial class BasicCheckbox : ClickableContainer
{
    private readonly Box background;
    private readonly Box fill;

    private Color checkedColor = Color.LimeGreen;
    private Color uncheckedColor = Color.DarkGreen;
    private Color hoverColor = Color.Green;

    /// <summary>
    /// Current value of the checkbox
    /// </summary>
    public ReactiveBool Current { get; } = new ReactiveBool(false);

    public Color CheckedColor
    {
        get => checkedColor;
        set
        {
            checkedColor = value;
            if (Current.Value)
                fill.Color = value;
        }
    }

    public Color UncheckedColor
    {
        get => uncheckedColor;
        set
        {
            uncheckedColor = value;
            if (!Current.Value)
                background.Color = value;
        }
    }

    public Color HoverColor
    {
        get => hoverColor;
        set => hoverColor = value;
    }

    public BasicCheckbox()
    {
        Size = new Vector2(20);

        Child = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = UncheckedColor
                },
                fill = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Color = CheckedColor,
                    Alpha = 0
                }
            }
        };

        Action = () => Current.Value = !Current.Value;

        Current.ValueChanged += e =>
        {
            if (e.NewValue)
                fill.FadeIn(100, Easing.OutQuint);
            else
                fill.FadeOut(100, Easing.OutQuint);
        };

        Enabled.ValueChanged += enabled =>
        {
            this.FadeTo(enabled.NewValue ? 1 : 0.5f, 100, Easing.OutQuint);
        };
    }

    public override bool OnHover(MouseEvent e)
    {
        if (!Enabled.Value)
            return false;

        background.FadeToColour(HoverColor, 100, Easing.OutQuint);
        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        if (!Enabled.Value)
            return false;

        background.FadeToColour(UncheckedColor, 100, Easing.OutQuint);
        return base.OnHoverLost(e);
    }
}
