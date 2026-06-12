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

    public event Action<ValueChangedEvent<T>> MinValueChanged;
    public event Action<ValueChangedEvent<T>> MaxValueChanged;
    public event Action<ValueChangedEvent<T>> PrecisionChanged;

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

            // Re-apply constraints so the current value respects the new range.
            setValue(base.Value);
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

            // Re-apply constraints so the current value respects the new range.
            setValue(base.Value);
        }
    }

    public T Precision
    {
        get => precision;
        set
        {
            if (Disabled || precision.Equals(value))
                return;

            if (value <= T.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "Precision must be greater than zero.");

            T oldPrecision = precision;
            precision = value;
            TriggerPrecisionChanged(oldPrecision, value);

            // Re-round the current value onto the new precision step.
            setValue(base.Value);
        }
    }

    public override T Value
    {
        get => base.Value;
        set => setValue(value);
    }

    private void setValue(T value)
    {
        // Out-of-range values are clamped rather than rejected — the behaviour games
        // generally want for sliders/settings
        value = T.Clamp(value, minValue, maxValue);

        if (precision > DefaultPrecision)
        {
            if (typeof(T) == typeof(decimal))
            {
                // Only decimal values need decimal arithmetic for exactness.
                decimal accurateResult = decimal.CreateTruncating(value);
                accurateResult = Math.Round(accurateResult / decimal.CreateTruncating(precision)) * decimal.CreateTruncating(precision);
                base.Value = T.Clamp(T.CreateTruncating(accurateResult), minValue, maxValue);
            }
            else
            {
                // Double arithmetic is exact for float/integral steps in practical ranges
                // and an order of magnitude faster than routing through decimal.
                double doubleResult = double.CreateTruncating(value);
                double doublePrecision = double.CreateTruncating(precision);
                doubleResult = Math.Round(doubleResult / doublePrecision) * doublePrecision;
                base.Value = T.Clamp(T.CreateTruncating(doubleResult), minValue, maxValue);
            }
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
        MinValueChanged?.Invoke(new ValueChangedEvent<T>(oldValue, newValue));
    }

    protected virtual void TriggerMaxValueChanged(T oldValue, T newValue)
    {
        MaxValueChanged?.Invoke(new ValueChangedEvent<T>(oldValue, newValue));
    }

    protected virtual void TriggerPrecisionChanged(T oldValue, T newValue)
    {
        PrecisionChanged?.Invoke(new ValueChangedEvent<T>(oldValue, newValue));
    }

    public bool IsInteger => typeof(T) != typeof(float) && typeof(T) != typeof(double) && typeof(T) != typeof(decimal);

    public override string ToString()
    {
        return $"{GetType().Name} {Value} ({MinValue} - {MaxValue})";
    }
}
