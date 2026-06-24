// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Input.Bindings;

/// <summary>
/// Non-generic base for <see cref="KeyBindingContainer{T}"/>. Holds device-independent configuration
/// so that consuming code can refer to a container without knowing its action type.
/// </summary>
public abstract partial class KeyBindingContainer : Container
{
    /// <summary>
    /// How many bindings may be active simultaneously. See <see cref="SimultaneousBindingMode"/>.
    /// </summary>
    public SimultaneousBindingMode SimultaneousMode { get; protected set; } = SimultaneousBindingMode.None;

    /// <summary>
    /// How strictly the set of pressed keys must match a combination. See
    /// <see cref="KeyCombinationMatchingMode"/>.
    /// </summary>
    public KeyCombinationMatchingMode MatchingMode { get; protected set; } = KeyCombinationMatchingMode.Any;

    /// <summary>
    /// Whether key bindings should send repeat press events while a combination is held. When
    /// <c>false</c>, repeated key-down events for an already-pressed combination are ignored.
    /// </summary>
    public bool SendRepeats { get; protected set; }
}
