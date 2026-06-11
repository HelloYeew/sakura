// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Timing;

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

    /// <summary>
    /// The time at which this event physically occurred, in milliseconds on the shared
    /// <see cref="TimeSource"/> timeline. Captured from the OS event
    /// timestamp where available, making it more accurate than the time the event is processed.
    /// <see cref="double.NaN"/> when no timestamp was available (e.g. synthesized test input).
    /// <remarks>
    /// For rhythm game implementation, use <see cref="GameplayClock.GetTimeAt"/> to translate this
    /// into gameplay time for hit judgement.
    /// </remarks>
    /// </summary>
    public readonly double Timestamp;

    public bool ControlPressed => (Modifiers & KeyModifiers.Control) != 0;

    public KeyEvent(Key key, KeyModifiers modifiers, bool isRepeat, double timestamp = double.NaN)
    {
        Key = key;
        Modifiers = modifiers;
        IsRepeat = isRepeat;
        Timestamp = timestamp;
    }
}
