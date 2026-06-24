// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Sakura.Framework.Input.Bindings;

/// <summary>
/// An immutable, order-independent set of <see cref="InputKey"/> values that together describe a
/// single key combination (e.g. <c>Ctrl+Shift+A</c>).
/// </summary>
public readonly struct KeyCombination : IEquatable<KeyCombination>
{
    private readonly ImmutableArray<InputKey> keys;

    /// <summary>
    /// The keys that make up this combination, normalised (physical modifiers folded to logical,
    /// sorted, de-duplicated). Safe to read on a default-valued <see cref="KeyCombination"/> (returns empty).
    /// </summary>
    public ImmutableArray<InputKey> Keys => keys.IsDefault ? ImmutableArray<InputKey>.Empty : keys;

    /// <summary>
    /// An empty combination that matches nothing.
    /// </summary>
    public static readonly KeyCombination NONE = new KeyCombination(ImmutableArray<InputKey>.Empty);

    public KeyCombination(IEnumerable<InputKey> keys)
    {
        this.keys = normalise(keys);
    }

    public KeyCombination(params InputKey[] keys)
        : this((IEnumerable<InputKey>)keys)
    {
    }

    /// <summary>
    /// Parses a combination from a string such as <c>"Control+A"</c> or <c>"Shift+MouseLeft"</c>.
    /// Tokens are matched case-insensitively against <see cref="InputKey"/> names, with keyboard
    /// keys also accepted by their <see cref="Key"/> name (e.g. <c>"A"</c>, <c>"F1"</c>).
    /// </summary>
    public static KeyCombination Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return NONE;

        var keys = value
                   .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Select(parseToken)
                   .Where(k => k != InputKey.None);

        return new KeyCombination(keys);
    }

    private static InputKey parseToken(string token)
    {
        // Direct InputKey name match (modifiers, mouse, scroll).
        if (Enum.TryParse<InputKey>(token, ignoreCase: true, out var inputKey) && inputKey != InputKey.None)
            return inputKey;

        // Gamepad buttons serialised as "Gamepad{Button}" (e.g. "GamepadSouth").
        if (token.StartsWith("Gamepad", StringComparison.OrdinalIgnoreCase) && token.Length > "Gamepad".Length)
        {
            string buttonName = token["Gamepad".Length..];
            if (Enum.TryParse<GamepadButton>(buttonName, ignoreCase: true, out var gamepadButton))
                return InputKeyExtensions.FromGamepadButton(gamepadButton);
        }

        // Fall back to a keyboard Key name (e.g. "A", "Space", "F1").
        if (Enum.TryParse<Key>(token, ignoreCase: true, out var key) && key != Key.Unknown)
            return InputKeyExtensions.FromKey(key);

        return InputKey.None;
    }

    /// <summary>
    /// Serialises this combination to a string that round-trips through <see cref="Parse"/>.
    /// Keyboard keys are emitted by their <see cref="Key"/> name (e.g. <c>"A"</c>), not their raw
    /// numeric <see cref="InputKey"/> value.
    /// </summary>
    public override string ToString() => string.Join('+', Keys.Select(InputKeyExtensions.GetReadableName));

    /// <summary>
    /// A human-readable representation suitable for display in UI (same as <see cref="ToString"/>).
    /// </summary>
    public string DisplayString => ToString();

    /// <summary>
    /// Whether this combination is satisfied by the given set of currently-pressed keys.
    /// </summary>
    /// <param name="pressedKeys">The currently-pressed keys. May contain physical modifiers; they
    /// are folded to logical form before comparison.</param>
    /// <param name="matchingMode">How strictly the pressed set must match.</param>
    public bool IsPressed(IReadOnlyCollection<InputKey> pressedKeys, KeyCombinationMatchingMode matchingMode)
    {
        if (Keys.IsEmpty)
            return false;

        var pressed = normalise(pressedKeys);

        switch (matchingMode)
        {
            case KeyCombinationMatchingMode.Any:
                return containsAll(pressed, Keys);

            case KeyCombinationMatchingMode.Exact:
                return pressed.Length == Keys.Length && containsAll(pressed, Keys);

            case KeyCombinationMatchingMode.Modifiers:
                // All combination keys must be pressed...
                if (!containsAll(pressed, Keys))
                    return false;

                // ...and the pressed modifier set must equal the combination's modifier set.
                var pressedMods = pressed.Where(k => k.IsLogicalModifier()).ToHashSet();
                var requiredMods = Keys.Where(k => k.IsLogicalModifier()).ToHashSet();
                return pressedMods.SetEquals(requiredMods);

            default:
                return false;
        }
    }

    private static bool containsAll(ImmutableArray<InputKey> haystack, ImmutableArray<InputKey> needles)
    {
        foreach (var n in needles)
        {
            if (!haystack.Contains(n))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether this combination contains the given key (after normalisation).
    /// </summary>
    public bool Contains(InputKey key)
    {
        var folded = key.ToLogicalModifier();
        if (folded == InputKey.None)
            folded = key;
        return Keys.Contains(folded);
    }

    private static ImmutableArray<InputKey> normalise(IEnumerable<InputKey> keys)
    {
        var set = new SortedSet<InputKey>();

        foreach (var key in keys)
        {
            if (key == InputKey.None)
                continue;

            // Fold physical modifiers (ShiftLeft/ShiftRight/...) into logical modifiers so that a
            // binding declared with either side matches a press of either side.
            var folded = key.ToLogicalModifier();
            set.Add(folded != InputKey.None ? folded : key);
        }

        return set.ToImmutableArray();
    }

    public bool Equals(KeyCombination other) => Keys.SequenceEqual(other.Keys);

    public override bool Equals(object? obj) => obj is KeyCombination other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var key in Keys)
            hash.Add(key);
        return hash.ToHashCode();
    }

    public static bool operator ==(KeyCombination left, KeyCombination right) => left.Equals(right);
    public static bool operator !=(KeyCombination left, KeyCombination right) => !left.Equals(right);

    public static implicit operator KeyCombination(InputKey key) => new KeyCombination(key);
    public static implicit operator KeyCombination(Key key) => new KeyCombination(InputKeyExtensions.FromKey(key));
}
