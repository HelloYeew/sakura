// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.UserInterface;

public partial class BasicCheckbox : Checkbox
{
    private readonly Box background;
    private readonly Box fill;

    private Color checkedColor = Color.LimeGreen;
    private Color uncheckedColor = Color.DarkGreen;
    private Color hoverColor = Color.Green;

    public Color CheckedColor
    {
        get => checkedColor;
        set
        {
            checkedColor = value;
            if (Current.Value) fill.Color = value;
        }
    }

    public Color UncheckedColor
    {
        get => uncheckedColor;
        set
        {
            uncheckedColor = value;
            if (!Current.Value) background.Color = value;
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
                background = new Box { RelativeSizeAxes = Axes.Both, Color = UncheckedColor },
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
    }

    protected override void OnCheckChanged(bool isChecked)
    {
        if (isChecked)
            fill.FadeIn(100, Easing.OutQuint);
        else
            fill.FadeOut(100, Easing.OutQuint);
    }

    protected override void OnHovered() =>
        background.FadeToColor(HoverColor, 100, Easing.OutQuint);

    protected override void OnHoverLost() =>
        background.FadeToColor(UncheckedColor, 100, Easing.OutQuint);

    protected override void OnEnabledChanged(bool enabled) => this.FadeTo(enabled ? 1 : 0.5f, 100, Easing.OutQuint);
}
