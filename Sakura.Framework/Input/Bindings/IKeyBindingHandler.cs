// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input.Bindings;

/// <summary>
/// Non-generic marker for any drawable that handles key bindings. Used internally for subtree
/// discovery. Implement <see cref="IKeyBindingHandler{T}"/> rather than this directly.
/// </summary>
public interface IKeyBindingHandler
{
}

/// <summary>
/// Implemented by drawables that wish to react to key-binding actions of type <typeparamref name="T"/>
/// dispatched by an ancestor <see cref="KeyBindingContainer{T}"/>.
/// </summary>
/// <typeparam name="T">The action enum type.</typeparam>
public interface IKeyBindingHandler<T> : IKeyBindingHandler where T : struct
{
    /// <summary>
    /// Called when a bound combination for <paramref name="e"/>'s action becomes pressed.
    /// </summary>
    /// <returns><c>true</c> to mark the press handled and stop further propagation.</returns>
    bool OnPressed(KeyBindingPressEvent<T> e);

    /// <summary>
    /// Called when a previously-pressed bound combination is released. Only invoked on handlers
    /// that returned <c>true</c> from the corresponding <see cref="OnPressed"/>.
    /// </summary>
    void OnReleased(KeyBindingReleaseEvent<T> e);
}
