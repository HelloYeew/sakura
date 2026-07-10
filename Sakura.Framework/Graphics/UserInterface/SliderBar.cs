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
public abstract partial class SliderBar<T> : Container where T : struct, INumber<T>, IMinMaxValue<T>
{
    /// <summary>
    /// The current value. Assigning to <see cref="ReactiveNumber{T}.Value"/> (directly, via binding,
    /// or via mouse/keyboard input) is always clamped between <see cref="MinValue"/> and <see cref="MaxValue"/>.
    /// </summary>
    public ReactiveNumber<T> Current { get; } = new ReactiveNumber<T>(T.Zero);

    /// <summary>
    /// The minimum value of the slider. Backed by <see cref="Current"/>'s own min bound, so the
    /// current value is re-clamped automatically whenever this changes.
    /// </summary>
    public T MinValue
    {
        get => Current.MinValue;
        set => Current.MinValue = value;
    }

    /// <summary>
    /// The maximum value of the slider. Backed by <see cref="Current"/>'s own max bound, so the
    /// current value is re-clamped automatically whenever this changes.
    /// </summary>
    public T MaxValue
    {
        get => Current.MaxValue;
        set => Current.MaxValue = value;
    }

    /// <summary>
    /// How much one arrow-key press moves the value, in the same units as <see cref="MinValue"/> /
    /// <see cref="MaxValue"/> (i.e. an absolute step, not a fraction of the range).
    /// Defaults to 1. Ctrl+Arrow moves 10× this amount.
    /// </summary>
    public T KeyboardStep { get; set; } = T.One;

    public override bool AcceptsFocus => true;

    protected SliderBar()
    {
        Current.ValueChanged += e => OnValueChanged(e.NewValue);
    }

    public override void OnFocus(FocusEvent e)
    {
        base.OnFocus(e);
        OnFocusGained();
    }

    public override void OnFocusLost(FocusLostEvent e)
    {
        base.OnFocusLost(e);
        OnFocusLost();
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

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!HasFocus) return false;

        bool ctrl = (e.Modifiers & KeyModifiers.Control) > 0;
        T step = ctrl ? KeyboardStep * T.CreateTruncating(10) : KeyboardStep;

        T min = MinValue;
        T max = MaxValue;
        T cur = Current.Value;

        if (max <= min) return false;

        T newValue;

        // Bounded via headroom (cur - min / max - cur, both always >= 0 since Current is kept
        // within range) rather than a raw "cur ± step" followed by clamping, so unsigned numeric
        // types (byte, uint, ulong, ...) never underflow/overflow when stepping near an edge.
        if (e.Key == Key.Left || e.Key == Key.Down)
            newValue = cur - T.Min(step, cur - min);
        else if (e.Key == Key.Right || e.Key == Key.Up)
            newValue = cur + T.Min(step, max - cur);
        else if (e.Key == Key.Home)
            newValue = min;
        else if (e.Key == Key.End)
            newValue = max;
        else
            return false;

        // Current is a ReactiveNumber<T>, so this is clamped to [MinValue, MaxValue] as a safety net.
        Current.Value = newValue;
        return true;
    }

    /// <summary>
    /// Returns the current fill fraction in [0, 1].
    /// Useful inside <see cref="OnValueChanged"/>.
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
    /// </summary>
    protected virtual void OnValueChanged(T value) { }

    /// <summary>
    /// Called when the slider gains keyboard focus. Override to show a focus ring.
    /// </summary>
    protected virtual void OnFocusGained() { }

    /// <summary>
    /// Called when the slider loses keyboard focus. Override to hide the focus ring.
    /// </summary>
    protected new virtual void OnFocusLost() { }
}
