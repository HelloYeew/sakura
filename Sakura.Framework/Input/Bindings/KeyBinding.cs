// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input.Bindings;

/// <summary>
/// Associates a <see cref="KeyCombination"/> with an action to be fired when that combination is
/// pressed. The action is stored boxed; a <see cref="KeyBindingContainer{T}"/> retrieves it as its
/// strongly-typed action enum <c>T</c>.
/// </summary>
public class KeyBinding
{
    /// <summary>
    /// The combination of keys that triggers <see cref="Action"/>.
    /// </summary>
    public KeyCombination KeyCombination { get; set; }

    /// <summary>
    /// The action this binding fires, boxed. Typically an enum value of the container's action type.
    /// </summary>
    public object Action { get; set; }

    public KeyBinding(KeyCombination keyCombination, object action)
    {
        KeyCombination = keyCombination;
        Action = action;
    }

    /// <summary>
    /// Convenience constructor taking a single <see cref="InputKey"/>.
    /// </summary>
    public KeyBinding(InputKey key, object action)
        : this(new KeyCombination(key), action)
    {
    }

    /// <summary>
    /// Returns <see cref="Action"/> cast to <typeparamref name="T"/>.
    /// </summary>
    public T GetAction<T>() where T : struct => (T)Action;

    public override string ToString() => $"{KeyCombination} => {Action}";
}
