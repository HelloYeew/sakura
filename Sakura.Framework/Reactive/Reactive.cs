// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using Sakura.Framework.Extensions.ObjectExtensions;

namespace Sakura.Framework.Reactive;

/// <summary>
/// The minimum implementation of a reactive object that can be bound to other reactive objects.
/// When the value of this object changes, it will raise an event.
/// </summary>
/// <typeparam name="T">The type of the <see cref="Value"/> that this reactive object holds.</typeparam>
public class Reactive<T> : IReactive<T>
{
    public event Action<ValueChangedEvent<T>> ValueChanged = delegate { };

    private T value;
    private readonly List<IReactive<T>> bindings = new List<IReactive<T>>();
    private readonly T defaultValue;

    public T Default => defaultValue;

    public Reactive(T defaultValue)
    {
        this.defaultValue = defaultValue;
        value = defaultValue;
    }

    private bool disabled;

    public bool Disabled
    {
        get => disabled;
        set => disabled = value;
    }

    public virtual T Value
    {
        get => value;
        set
        {
            if (Disabled)
                return;

            if (EqualityComparer<T>.Default.Equals(this.value, value))
                return;

            T oldValue = this.value;
            this.value = value;

            TriggerValueChanged(oldValue, value);
        }
    }

    public bool IsDefault => EqualityComparer<T>.Default.Equals(value, defaultValue);

    public void BindTo(IReactive<T> other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));
        if (other == this)
            throw new InvalidOperationException("Cannot bind the reactive object to itself.");
        if (bindings.Contains(other))
            return; // Already bound to this source.

        bindings.Add(other);
        other.ValueChanged += OnBoundValueChanged;

        setValueFromBinding(other.Value);
    }

    public void UnbindFrom(IReactive<T> other)
    {
        other.ValueChanged -= OnBoundValueChanged;
        bindings.Remove(other);
    }

    public void UnbindAll()
    {
        foreach (var binding in bindings)
        {
            binding.ValueChanged -= OnBoundValueChanged;
        }
        bindings.Clear();
    }

    private void OnBoundValueChanged(ValueChangedEvent<T> e)
    {
        // When any bound source changes, update this reactive object's value.
        // The new value is whatever changed last.
        setValueFromBinding(e.NewValue);
    }

    /// <summary>
    /// Parses the input object into the value of this reactive object.
    /// </summary>
    /// <param name="input">The input object to parse.</param>
    /// <param name="formatProvider"><see cref="IFormatProvider"/> to use for parsing, defaults to <see cref="CultureInfo.InvariantCulture"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown if the reactive object is bound to another source or if parsing fails.</exception>
    public virtual void Parse(object input, IFormatProvider formatProvider = null)
    {
        // TODO: Parse value from the Reactive object should be parsable if the child type is parsable.

        if (Disabled)
            return;

        if (input == null)
        {
            Value = Default;
            return;
        }

        Type underlyingType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (underlyingType.IsEnum)
        {
            Value = (T)Enum.Parse(underlyingType, input.ToString().AsNonNull());
        }
        else
        {
            Value = (T)Convert.ChangeType(input, underlyingType, formatProvider);
        }
    }

    private void setValueFromBinding(T newValue)
    {
        if (Disabled)
            return;

        if (EqualityComparer<T>.Default.Equals(value, newValue))
            return;

        T oldValue = value;
        value = newValue;

        TriggerValueChanged(oldValue, newValue);
    }

    protected virtual void TriggerValueChanged(T oldValue, T newValue)
    {
        ValueChanged.Invoke(new ValueChangedEvent<T>(oldValue, newValue));
    }

    public override string ToString()
    {
        return $"{GetType().Name}(Value: {value}, Default: {defaultValue}, Disabled: {disabled})";
    }

    public static implicit operator T(Reactive<T> reactive) => reactive.Value;
}
