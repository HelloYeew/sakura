// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input.Bindings;

/// <summary>
/// Controls how strictly the set of currently-pressed keys must match a <see cref="KeyCombination"/>.
/// </summary>
public enum KeyCombinationMatchingMode
{
    /// <summary>
    /// Matches as long as every key in the combination is pressed. Extra unrelated keys (including
    /// extra modifiers) are ignored. This is the most permissive mode and the default.
    /// </summary>
    Any,

    /// <summary>
    /// Matches only when the pressed keys are exactly the keys in the combination — no more, no less.
    /// </summary>
    Exact,

    /// <summary>
    /// Like <see cref="Any"/> for non-modifier keys, but requires the set of pressed modifiers to
    /// exactly match the modifiers in the combination. Useful to distinguish e.g. <c>Ctrl+A</c> from
    /// <c>Ctrl+Shift+A</c> while still allowing extra non-modifier keys.
    /// </summary>
    Modifiers,
}
