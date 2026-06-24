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

    public KeyBindingReleaseEvent(T action)
    {
        Action = action;
    }

    public override string ToString() => $"KeyBindingReleaseEvent: Action={Action}";
}
