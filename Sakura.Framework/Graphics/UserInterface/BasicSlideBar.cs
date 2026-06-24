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

public partial class BasicSliderBar<T> : SliderBar<T> where T : struct, INumber<T>
{
    public Color BackgroundColor { get; set; } = Color.DarkGreen;
    public Color SelectionColor { get; set; } = Color.LimeGreen;

    /// <summary>
    /// Color of the focus ring.
    /// </summary>
    public Color FocusColor { get; set; } = Color.GreenYellow;

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
        // Clear any in-flight resize so rapid changes (e.g. dragging) animate from the current
        // size rather than fighting a stale transform. The selection box only ever runs resize
        // transforms, so clearing all of them here is safe.
        selection.ClearTransforms();
        selection.ResizeTo(new Vector2(GetFillFraction(), 1), FillAnimationDuration, FillAnimationEasing);
    }

    protected override void OnFocusGained() => BorderColor = FocusColor;

    protected override void OnFocusLost() => BorderColor = Color.Transparent;
}
