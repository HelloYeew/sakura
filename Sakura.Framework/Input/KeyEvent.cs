// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input;

/// <summary>
/// Represent a keyboard event.
/// </summary>
public readonly struct KeyEvent
{
    /// <summary>
    /// The key that triggered the event.
    /// </summary>
    public readonly Key Key;

    /// <summary>
    /// The modifier keys that were active when the event was generated.
    /// </summary>
    public readonly KeyModifiers Modifiers;

    /// <summary>
    /// Whether this event is a repeat from the key being held down.
    /// </summary>
    public readonly bool IsRepeat;

    public KeyEvent(Key key, KeyModifiers modifiers, bool isRepeat)
    {
        Key = key;
        Modifiers = modifiers;
        IsRepeat = isRepeat;
    }
}
