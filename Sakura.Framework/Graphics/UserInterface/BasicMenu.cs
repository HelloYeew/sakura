// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// A plain <see cref="Menu"/>: dark, square, no animation. Games restyle by subclassing
/// <see cref="Menu"/> and <see cref="DrawableMenuItem"/> the same way.
/// </summary>
public partial class BasicMenu : Menu
{
    public BasicMenu(FlowDirection direction, bool topLevelMenu = false)
        : base(direction, topLevelMenu)
    {
        BackgroundColor = ColorExtensions.FromHex("222228");

        if (direction == FlowDirection.Vertical)
            MinWidth = 160;
    }

    protected override DrawableMenuItem CreateDrawableMenuItem(MenuItem item) => new BasicDrawableMenuItem(item);

    protected override Menu CreateSubMenu() => new BasicMenu(FlowDirection.Vertical);
}

public partial class BasicDrawableMenuItem : DrawableMenuItem
{
    private const float item_height = 28;
    private const float separator_height = 9;
    private const float horizontal_padding = 10;

    /// <summary>
    /// The column for a toggle's check mark, kept in every vertical item so labels line up whether
    /// or not their item is a toggle.
    /// </summary>
    private const float gutter = 22;

    private const float shortcut_spacing = 28;
    private const float arrow_width = 18;
    private const float text_size = 14;

    private static readonly Color hover_color = ColorExtensions.FromHex("3d3d4a");
    private static readonly Color text_color = Color.White;
    private static readonly Color dim_text_color = ColorExtensions.FromHex("8a8a96");
    private static readonly Color separator_color = ColorExtensions.FromHex("3a3a44");

    private readonly Box background;
    private readonly SpriteText? label;
    private readonly SpriteText? shortcut;
    private readonly IconSprite? arrow;
    private readonly IconSprite? check;

    private bool isSeparator => Item is MenuSeparator;

    public BasicDrawableMenuItem(MenuItem item)
        : base(item)
    {
        Add(background = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = isSeparator ? separator_color : hover_color,
            Alpha = 0
        });

        if (isSeparator)
        {
            background.RelativeSizeAxes = Axes.X;
            background.Size = new Vector2(1, 1);
            background.Anchor = Anchor.CentreLeft;
            background.Origin = Anchor.CentreLeft;
            background.Alpha = 1;
            return;
        }

        Add(label = new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = item.Text.Value,
            Font = FontUsage.Default.With(size: text_size),
            Color = text_color
        });

        if (!string.IsNullOrEmpty(item.ShortcutText))
        {
            Add(shortcut = new SpriteText
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Text = item.ShortcutText,
                Font = FontUsage.Default.With(size: text_size),
                Color = dim_text_color
            });
        }

        if (item.Items.Count > 0)
        {
            Add(arrow = new IconSprite
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Icon = IconUsage.ChevronRight,
                IconSize = 16,
                Color = dim_text_color
            });
        }

        if (item is ToggleMenuItem)
        {
            Add(check = new IconSprite
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.Centre,
                Icon = IconUsage.Check,
                IconSize = 16,
                Color = text_color
            });
        }
    }

    private bool vertical => MenuDirection == FlowDirection.Vertical;

    public override Vector2 NaturalSize
    {
        get
        {
            if (isSeparator)
                return new Vector2(0, vertical ? separator_height : 0);

            // Text isn't measurable until it has a font, which it gets on load.
            if (!IsLoaded || label == null)
                return new Vector2(0, item_height);

            float width = horizontal_padding * 2 + label.ContentSize.X;

            if (vertical)
                width += gutter;

            if (shortcut != null)
                width += shortcut_spacing + shortcut.ContentSize.X;

            if (arrow != null)
                width += arrow_width;

            return new Vector2(width, item_height);
        }
    }

    public override void Update()
    {
        base.Update();

        if (label == null)
            return;

        label.X = horizontal_padding + (vertical ? gutter : 0);

        if (check != null)
            check.X = horizontal_padding + gutter / 2 - 2;

        if (arrow != null)
            arrow.X = -horizontal_padding + 6;

        if (shortcut != null)
            shortcut.X = -horizontal_padding - (arrow != null ? arrow_width : 0);
    }

    protected override void OnTextChanged(string text)
    {
        if (label != null)
            label.Text = text;
    }

    protected override void UpdateState()
    {
        if (isSeparator || label == null)
            return;

        bool lit = IsActionable && (Highlighted || Selected);

        background.Alpha = lit ? 1 : 0;
        label.Color = IsActionable ? text_color : dim_text_color;

        if (check != null)
            check.Alpha = Item is ToggleMenuItem { State.Value: true } ? 1 : 0;
    }
}
