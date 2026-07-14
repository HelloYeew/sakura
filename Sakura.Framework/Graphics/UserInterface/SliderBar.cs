// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Numerics;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// Abstract base for slider/scrubber controls.
/// </summary>
public abstract partial class SliderBar<T> : Container, IHasTooltip where T : struct, INumber<T>, IMinMaxValue<T>
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

    /// <summary>
    /// Grid spacing that <see cref="Current"/> snaps to on every change from mouse drag/click,
    /// keyboard, or a direct/bound assignment. Thin pass-through to
    /// <see cref="ReactiveNumber{T}.Precision"/>. Independent of <see cref="KeyboardStep"/>,
    /// which controls how far one arrow-key press moves the value rather than which values are
    /// reachable at all. e.g. Step = 0.25 on a continuous float slider only allows
    /// 0, 0.25, 0.5, 0.75, 1, etc.
    /// </summary>
    public T Step
    {
        get => Current.Precision;
        set => Current.Precision = value;
    }

    private int? decimalPlaces;

    /// <summary>
    /// Rounds <see cref="Current"/> to this many decimal places on every change, trimming
    /// floating-point drift from continuous dragging (e.g. 0.748274837 -> 0.75 when set to 2).
    /// Only meaningful for fractional <typeparamref name="T"/> (float/double/decimal), a no-op
    /// for integer types, which are already whole numbers. Null (default) disables rounding.
    /// </summary>
    public int? DecimalPlaces
    {
        get => decimalPlaces;
        set => decimalPlaces = value;
    }

    /// <summary>
    /// Value <see cref="Current"/> resets to on double-click. Null (default) disables
    /// double-click-to-reset entirely.
    /// </summary>
    public T? DefaultValue { get; set; }

    /// <summary>
    /// Whether this slider responds to mouse, keyboard, and double-click input. Disabling also
    /// prevents it from taking keyboard focus.
    /// </summary>
    public readonly ReactiveBool Enabled = new ReactiveBool(true);

    public override bool AcceptsFocus => Enabled.Value;

    public virtual string? TooltipText => DecimalPlaces is int places && !Current.IsInteger
        ? Current.Value.ToString($"F{places}", null)
        : Current.Value.ToString();

    protected SliderBar()
    {
        Current.ValueChanged += onCurrentValueChanged;
    }

    public override void LoadComplete()
    {
        base.LoadComplete();
        Enabled.BindValueChanged(e => OnEnabledChanged(e.NewValue), true);

        // invoke value change one time to make it properly update using true value
        onCurrentValueChanged(new ValueChangedEvent<T>(Current.Value, Current.Value));
    }

    private void onCurrentValueChanged(ValueChangedEvent<T> e)
    {
        if (decimalPlaces is int places && !Current.IsInteger)
        {
            double roundedRaw = Math.Round(double.CreateTruncating(e.NewValue), places, MidpointRounding.AwayFromZero);
            T rounded = T.Clamp(T.CreateTruncating(roundedRaw), MinValue, MaxValue);

            if (rounded != e.NewValue)
            {
                Current.Value = rounded;
                return;
            }
        }

        OnValueChanged(e.NewValue);
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

    public override bool OnHover(MouseEvent e)
    {
        if (!Enabled.Value) return false;

        OnHovered();
        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        if (!Enabled.Value) return false;

        OnHoverLost();
        return base.OnHoverLost(e);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (!Enabled.Value) return false;

        applyMousePosition(e.ScreenSpaceMousePosition);
        base.OnMouseDown(e);
        return true;
    }

    public override bool OnDoubleClick(MouseButtonEvent e)
    {
        if (!Enabled.Value) return false;

        if (DefaultValue is T resetValue)
        {
            Current.Value = resetValue;
            return true;
        }

        return base.OnDoubleClick(e);
    }

    public override bool OnDragStart(MouseButtonEvent e) => Enabled.Value;

    public override bool OnDrag(MouseEvent e)
    {
        if (!Enabled.Value) return false;

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
        float raw = min + progress * (max - min);

        if (Current.IsInteger)
            raw = MathF.Round(raw, MidpointRounding.AwayFromZero);

        Current.Value = T.CreateTruncating(raw);
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!HasFocus || !Enabled.Value) return false;

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

    /// <summary>
    /// Called when hover begins and the slider is enabled. Override to apply hover visuals.
    /// </summary>
    protected virtual void OnHovered() { }

    /// <summary>
    /// Called when hover ends and the slider is enabled. Override to revert hover visuals.
    /// </summary>
    protected virtual void OnHoverLost() { }

    /// <summary>
    /// Called when <see cref="Enabled"/> changes. Override to apply disabled visuals.
    /// </summary>
    protected virtual void OnEnabledChanged(bool enabled) { }
}
