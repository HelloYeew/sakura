// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input.Bindings;

/// <summary>
/// Fired to an <see cref="IKeyBindingHandler{T}"/> when a previously-pressed combination is released.
/// </summary>
/// <typeparam name="T">The action enum type.</typeparam>
public readonly struct KeyBindingReleaseEvent<T> where T : struct
{
    /// <summary>
    /// The action that was released.
    /// </summary>
    public readonly T Action;

    /// <summary>
    /// When the input that ended this press was let go, in milliseconds on the shared
    /// <see cref="Sakura.Framework.Timing.TimeSource"/> timeline. <see cref="double.NaN"/> when that event carries no
    /// timestamp, or when the press ended for another reason (the bindings changed).
    /// </summary>
    public readonly double Timestamp;

    public KeyBindingReleaseEvent(T action, double timestamp = double.NaN)
    {
        Action = action;
        Timestamp = timestamp;
    }

    public override string ToString() => $"KeyBindingReleaseEvent: Action={Action}";
}
