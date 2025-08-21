// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Numerics;

namespace Sakura.Framework.Reactive;

public class ReactiveNumber<T> : Reactive<T>, IReactiveNumber<T>
    where T : struct, INumber<T>, IMinMaxValue<T>
{
    private T minValue;
    private T maxValue;
    private T precision;

    public ReactiveNumber(T defaultValue) : base(defaultValue)
    {
        minValue = DefaultMinValue;
        maxValue = DefaultMaxValue;
        precision = DefaultPrecision;
    }

    public event Action<ValueChangedEvent<T>> MinValueChanged = delegate { };
    public event Action<ValueChangedEvent<T>> MaxValueChanged = delegate { };
    public event Action<ValueChangedEvent<T>> PrecisionChanged = delegate { };

    protected T DefaultMinValue => T.MinValue;

    protected T DefaultMaxValue => T.MaxValue;

    protected T DefaultPrecision
    {
        get
        {
            if (typeof(T) == typeof(float))
                return (T)(object)float.Epsilon;
            if (typeof(T) == typeof(double))
                return (T)(object)double.Epsilon;

            return T.One;
        }
    }

    public T MinValue
    {
        get => minValue;
        set
        {
            if (Disabled || minValue.Equals(value))
                return;

            // Cross check to ensure MinValue is not greater than MaxValue
            if (value > MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), $"MinValue cannot be greater than MaxValue ({MaxValue}).");

            T oldMinValue = minValue;
            minValue = value;
            TriggerMinValueChanged(oldMinValue, value);
        }
    }

    public T MaxValue
    {
        get => maxValue;
        set
        {
            if (Disabled || maxValue.Equals(value))
                return;

            // Cross check to ensure MaxValue is not less than MinValue
            if (value < MinValue)
                throw new ArgumentOutOfRangeException(nameof(value), $"MaxValue cannot be less than MinValue ({MinValue}).");

            T oldMaxValue = maxValue;
            maxValue = value;
            TriggerMaxValueChanged(oldMaxValue, value);
        }
    }

    public T Precision
    {
        get => precision;
        set
        {
            if (Disabled || precision.Equals(value))
                return;

            if (precision <= T.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "Precision must be greater than zero.");

            TriggerPrecisionChanged(precision, value);
        }
    }

    public override T Value
    {
        get => base.Value;
        set => setValue(value);
    }

    private void setValue(T value)
    {
        if (value < MinValue || value > MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), $"Value must be between {MinValue} and {MaxValue}.");

        if (Precision > DefaultPrecision)
        {
            decimal accurateResult = decimal.CreateTruncating(T.Clamp(value, MinValue, MaxValue));
            accurateResult = Math.Round(accurateResult / decimal.CreateTruncating(Precision)) * decimal.CreateTruncating(Precision);
            base.Value = T.CreateTruncating(accurateResult);
        }
        else
        {
            base.Value = value;
        }
    }

    public override void Parse(object input, IFormatProvider formatProvider = null)
    {
        base.Parse(input, formatProvider);

        setValue(Value);
    }

    protected virtual void TriggerMinValueChanged(T oldValue, T newValue)
    {
        MinValueChanged(new ValueChangedEvent<T>(oldValue, newValue));
    }

    protected virtual void TriggerMaxValueChanged(T oldValue, T newValue)
    {
        MaxValueChanged(new ValueChangedEvent<T>(oldValue, newValue));
    }

    protected virtual void TriggerPrecisionChanged(T oldValue, T newValue)
    {
        PrecisionChanged(new ValueChangedEvent<T>(oldValue, newValue));
    }

    public bool IsInteger => typeof(T) != typeof(float) && typeof(T) != typeof(double) && typeof(T) != typeof(decimal);

    public override string ToString()
    {
        return $"{GetType().Name} {Value} ({MinValue} - {MaxValue})";
    }
}
