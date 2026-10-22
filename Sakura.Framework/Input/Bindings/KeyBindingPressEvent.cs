// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input.Bindings;

/// <summary>
/// Fired to an <see cref="IKeyBindingHandler{T}"/> when one of its bound combinations becomes pressed.
/// </summary>
/// <typeparam name="T">The action enum type.</typeparam>
public readonly struct KeyBindingPressEvent<T> where T : struct
{
    /// <summary>
    /// The action that was triggered.
    /// </summary>
    public readonly T Action;

    /// <summary>
    /// Whether this press is a repeat caused by a key being held down.
    /// </summary>
    public readonly bool Repeat;

    /// <summary>
    /// For scroll-triggered bindings, the scroll delta that produced this press. <see cref="Vector2.Zero"/>
    /// for non-scroll bindings. Useful to scale an action (e.g. volume) by the scroll amount.
    /// </summary>
    public readonly Vector2 ScrollDelta;

    /// <summary>
    /// When the key, button or touch that made this press happened, in milliseconds on the shared
    /// <see cref="Sakura.Framework.Timing.TimeSource"/> timeline, as the input event that made it reports it.
    /// <see cref="double.NaN"/> when that event carries no timestamp (a scroll, or synthesized test input).
    /// </summary>
    /// <remarks>
    /// For hit judgement, use <see cref="Sakura.Framework.Timing.GameplayClock.GetTimeAt"/> to translate this into gameplay
    /// time, as with a raw key event's.
    /// </remarks>
    public readonly double Timestamp;

    public KeyBindingPressEvent(T action, bool repeat = false, Vector2 scrollDelta = default, double timestamp = double.NaN)
    {
        Action = action;
        Repeat = repeat;
        ScrollDelta = scrollDelta;
        Timestamp = timestamp;
    }

    public override string ToString() => $"KeyBindingPressEvent: Action={Action}, Repeat={Repeat}";
}
