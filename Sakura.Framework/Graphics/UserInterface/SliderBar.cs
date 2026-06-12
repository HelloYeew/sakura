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

    /// <summary>
    /// How much one arrow-key press moves the value as a fraction of the total range.
    /// Defaults to 1/100 (1 %). Ctrl+Arrow uses 10× this step.
    /// </summary>
    public float KeyboardStep { get; set; } = 0.01f;

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
        float step = KeyboardStep * (ctrl ? 10f : 1f);

        float min = float.CreateTruncating(MinValue);
        float max = float.CreateTruncating(MaxValue);
        float cur = float.CreateTruncating(Current.Value);
        float range = max - min;

        if (range <= 0) return false;

        float delta = 0;

        if (e.Key == Key.Left || e.Key == Key.Down)
            delta = -step * range;
        else if (e.Key == Key.Right || e.Key == Key.Up)
            delta = step * range;
        else if (e.Key == Key.Home)
            delta = min - cur;
        else if (e.Key == Key.End)
            delta = max - cur;
        else
            return false;

        Current.Value = T.CreateTruncating(Math.Clamp(cur + delta, min, max));
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
