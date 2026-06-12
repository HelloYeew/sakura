// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Numerics;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// Abstract base for slider/scrubber controls.
/// </summary>
public abstract partial class SliderBar<T> : Container where T : struct, INumber<T>
{
    /// <summary>
    /// The current value, clamped between <see cref="MinValue"/> and <see cref="MaxValue"/>.
    /// </summary>
    public Reactive<T> Current { get; } = new Reactive<T>(T.Zero);

    public T MinValue { get; set; }
    public T MaxValue { get; set; }

    protected SliderBar()
    {
        Current.ValueChanged += e => OnValueChanged(e.NewValue);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        applyMousePosition(e.ScreenSpaceMousePosition);
        base.OnMouseDown(e);
        return true;
    }

    public override bool OnDragStart(MouseButtonEvent e) => true;

    public override bool OnDrag(MouseEvent e)
    {
        applyMousePosition(e.ScreenSpaceMousePosition);
        return true;
    }

    private void applyMousePosition(Maths.Vector2 screenSpacePos)
    {
        if (DrawRectangle.Width == 0) return;

        float localX = screenSpacePos.X - DrawRectangle.X;
        float progress = Math.Clamp(localX / DrawRectangle.Width, 0f, 1f);

        float min = float.CreateTruncating(MinValue);
        float max = float.CreateTruncating(MaxValue);
        Current.Value = T.CreateTruncating(min + progress * (max - min));
    }

    /// <summary>
    /// Returns the current fill fraction in [0, 1] based on <see cref="Current"/>,
    /// <see cref="MinValue"/>, and <see cref="MaxValue"/>. Useful inside <see cref="OnValueChanged"/>.
    /// </summary>
    protected float GetFillFraction()
    {
        float min = float.CreateTruncating(MinValue);
        float max = float.CreateTruncating(MaxValue);
        float cur = float.CreateTruncating(Current.Value);
        float range = max - min;
        return range <= 0 ? 0f : Math.Clamp((cur - min) / range, 0f, 1f);
    }

    /// <summary>
    /// Called whenever <see cref="Current"/> changes. Override to update visuals.
    /// Use <see cref="GetFillFraction"/> for a normalised [0,1] value.
    /// </summary>
    protected virtual void OnValueChanged(T value) { }
}
