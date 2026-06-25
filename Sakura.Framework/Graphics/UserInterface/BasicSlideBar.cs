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

    /// <summary>
    /// Clock time (ms) at which the selection fill was last updated
    /// </summary>
    private double lastFillUpdateTime = double.NaN;

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
        var target = new Vector2(GetFillFraction(), 1);

        selection.ClearTransforms();

        double now = selection.Clock.CurrentTime;
        double sinceLast = now - lastFillUpdateTime;
        lastFillUpdateTime = now;

        // there are sometime that the easing time is more than the new value change event
        // lead to the size not even update properly since it's clear transformation too quick
        // just calculate whether it fast enough to considered it as just skip it
        double frameTime = selection.Clock.ElapsedFrameTime;
        double continuousWindow = frameTime > 0 ? frameTime * 2.5 : 0;

        bool isContinuousDrive = !double.IsNaN(sinceLast)
                                 && (sinceLast <= 0 || sinceLast <= continuousWindow);

        if (isContinuousDrive || FillAnimationDuration <= 0)
            selection.Size = target;
        else
            selection.ResizeTo(target, FillAnimationDuration, FillAnimationEasing);
    }

    protected override void OnFocusGained() => BorderColor = FocusColor;

    protected override void OnFocusLost() => BorderColor = Color.Transparent;
}
