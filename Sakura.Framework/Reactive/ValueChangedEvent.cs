// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Reactive;

/// <summary>
/// A value-changed event that encapsulates the old and new values of a property when the reactive object value changes.
/// </summary>
/// <typeparam name="T"></typeparam>
public readonly struct ValueChangedEvent<T>
{
    public T OldValue { get; }
    public T NewValue { get; }

    public ValueChangedEvent(T oldValue, T newValue)
    {
        OldValue = oldValue;
        NewValue = newValue;
    }

    public override string ToString()
    {
        return $"ValueChangedEvent(OldValue: {OldValue}, NewValue: {NewValue})";
    }
}
