// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Numerics;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Vector2 = Sakura.Framework.Maths.Vector2;

namespace Sakura.Framework.Graphics.UserInterface;

public partial class BasicSliderBar<T> : SliderBar<T> where T : struct, INumber<T>, IMinMaxValue<T>
{
    public Color BackgroundColor { get; set; } = Color.DarkGreen;
    public Color SelectionColor { get; set; } = Color.LimeGreen;

    /// <summary>
    /// Color of the focus ring.
    /// </summary>
    public Color FocusColor { get; set; } = Color.GreenYellow;

    /// <summary>
    /// Color the track fades to while hovered and not disabled.
    /// </summary>
    public Color HoverColor { get; set; } = Color.Green;

    /// <summary>
    /// Duration in milliseconds over which the selection fill animates towards a new value.
    /// Set to 0 to snap instantly. Defaults to 150ms.
    /// </summary>
    public double FillAnimationDuration { get; set; } = 150;

    /// <summary>
    /// Easing curve used while animating the selection fill.
    /// </summary>
    public Easing FillAnimationEasing { get; set; } = Easing.OutQuint;

    private readonly Box background;
    private readonly Box selection;

    /// <summary>
    /// The selection fill's current width as a fraction of the full track, in [0, 1].
    /// While an animation is in flight this reflects the in-progress (eased) value rather than the
    /// final target, so it can be used to observe the fill animation.
    /// </summary>
    public float CurrentFillWidth => selection.Size.X;

    public BasicSliderBar()
    {
        Size = new Vector2(200, 20);
        Masking = true;
        BorderThickness = 2;
        BorderColor = Color.Transparent;

        Children = new Drawable[]
        {
            background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = BackgroundColor
            },
            selection = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0, 1),
                Color = SelectionColor
            }
        };
    }

    protected override void OnValueChanged(T value)
    {
        float target = GetFillFraction();

        // Snap instantly when there is no animation window or before the tree is live (no clock yet).
        if (FillAnimationDuration <= 0 || !IsLoaded)
        {
            selection.Size = new Vector2(target, 1);
            return;
        }

        selection.ResizeTo(new Vector2(target, 1), FillAnimationDuration, FillAnimationEasing);
    }

    protected override void OnFocusGained() => BorderColor = FocusColor;

    protected override void OnFocusLost() => BorderColor = Color.Transparent;

    protected override void OnHovered() => background.FadeToColour(HoverColor, 100, Easing.OutQuint);

    protected override void OnHoverLost() => background.FadeToColour(BackgroundColor, 100, Easing.OutQuint);

    protected override void OnEnabledChanged(bool enabled) => this.FadeTo(enabled ? 1f : 0.5f, 100, Easing.OutQuint);
}
