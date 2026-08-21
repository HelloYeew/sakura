// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Sakura.Framework.Extensions.ObjectExtensions;

namespace Sakura.Framework.Reactive;

/// <summary>
/// The minimum implementation of a reactive object that can be bound to other reactive objects.
/// When the value of this object changes, it will raise an event.
/// </summary>
/// <typeparam name="T">The type of the <see cref="Value"/> that this reactive object holds.</typeparam>
public class Reactive<T> : IReactive<T>
{
    private Action<ValueChangedEvent<T>> valueChanged;

    /// <summary>
    /// How many handlers <see cref="ValueChanged"/> currently has.
    /// </summary>
    /// <remarks>
    /// Only used to tell "one subscriber" from "several", so that the common single-subscriber case
    /// notifies without materializing an invocation list (see <see cref="TriggerValueChanged"/>).
    /// Advisory: a concurrent subscribe races the count, and the cost of being briefly wrong is one
    /// notification taking the other path.
    /// </remarks>
    private int handlerCount;

    /// <summary>
    /// Raised after <see cref="Value"/> changes.
    /// </summary>
    /// <remarks>
    /// Written out rather than left as an auto-event so that <see cref="handlerCount"/> can be kept
    /// alongside the delegate. The add/remove are the same lock-free compare-and-swap the compiler
    /// would have generated.
    /// </remarks>
    public event Action<ValueChangedEvent<T>> ValueChanged
    {
        add
        {
            if (value.IsNull())
                return;

            Action<ValueChangedEvent<T>> prior;
            Action<ValueChangedEvent<T>> combined;

            do
            {
                prior = valueChanged;
                combined = (Action<ValueChangedEvent<T>>)Delegate.Combine(prior, value);
            } while (Interlocked.CompareExchange(ref valueChanged, combined, prior) != prior);

            Interlocked.Increment(ref handlerCount);
        }
        remove
        {
            if (value.IsNull())
                return;

            Action<ValueChangedEvent<T>> prior;
            Action<ValueChangedEvent<T>> reduced;

            do
            {
                prior = valueChanged;
                reduced = (Action<ValueChangedEvent<T>>)Delegate.Remove(prior, value);
            } while (Interlocked.CompareExchange(ref valueChanged, reduced, prior) != prior);

            // Delegate.Remove hands back the original when nothing matched, so this counts only real
            // removals and cannot drift below the number of live handlers.
            if (!ReferenceEquals(prior, reduced))
                Interlocked.Decrement(ref handlerCount);
        }
    }

    private T value;

    private List<IReactive<T>> bindings;
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

            T coerced = CoerceValue(value);

            if (EqualityComparer<T>.Default.Equals(this.value, coerced))
                return;

            T oldValue = this.value;
            this.value = coerced;

            TriggerValueChanged(oldValue, coerced);
        }
    }

    /// <summary>
    /// Adjusts a candidate value before it is stored. The base implementation accepts it as-is,
    /// <see cref="ReactiveNumber{T}"/> clamps it into range and rounds it onto its precision grid.
    /// </summary>
    /// <remarks>
    /// This is a hook rather than an override of <see cref="Value"/> because a value can arrive from
    /// two directions — a direct assignment, or a bound source pushing one in — and both have to be
    /// coerced the same way. Overriding the setter only covers the first, which leaves a bound
    /// reactive holding values its own rules say are impossible.
    /// </remarks>
    protected virtual T CoerceValue(T candidate) => candidate;

    /// <summary>
    /// Re-runs <see cref="CoerceValue"/> against the stored value, for a derived type whose coercion
    /// rules have changed underneath it.
    /// </summary>
    protected void ReapplyCoercion() => Value = value;

    public bool IsDefault => EqualityComparer<T>.Default.Equals(value, defaultValue);

    public void BindTo(IReactive<T> other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));
        if (other == this)
            throw new InvalidOperationException("Cannot bind the reactive object to itself.");

        bindings ??= new List<IReactive<T>>();

        if (bindings.Contains(other))
            return; // Already bound to this source.

        bindings.Add(other);
        other.ValueChanged += OnBoundValueChanged;

        setValueFromBinding(other.Value);
    }

    /// <summary>
    /// Subscribes to <see cref="ValueChanged"/>, optionally invoking the callback immediately
    /// with the current value (with <c>OldValue == NewValue</c>).
    /// </summary>
    /// <param name="onChange">The callback to run on changes.</param>
    /// <param name="runOnceImmediately">Whether to invoke the callback right away with the current value.</param>
    public void BindValueChanged(Action<ValueChangedEvent<T>> onChange, bool runOnceImmediately = false)
    {
        if (onChange == null)
            throw new ArgumentNullException(nameof(onChange));

        ValueChanged += onChange;

        if (runOnceImmediately)
            onChange(new ValueChangedEvent<T>(value, value));
    }

    public void UnbindFrom(IReactive<T> other)
    {
        other.ValueChanged -= OnBoundValueChanged;
        bindings?.Remove(other);
    }

    public void UnbindAll()
    {
        if (bindings == null)
            return;

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

        // Coerced exactly as a direct assignment would be. A bound ReactiveNumber that skipped this
        // could hold a value outside its own [MinValue, MaxValue] or off its precision grid, purely
        // because of which direction the value came from.
        T coerced = CoerceValue(newValue);

        if (EqualityComparer<T>.Default.Equals(value, coerced))
            return;

        T oldValue = value;
        value = coerced;

        TriggerValueChanged(oldValue, coerced);
    }

    protected virtual void TriggerValueChanged(T oldValue, T newValue)
    {
        var handlers = valueChanged;

        if (handlers == null)
            return;

        // A single handler has nothing queued behind it, so there is no stale notification to guard
        // against and no reason to build a list.
        if (handlerCount <= 1)
        {
            handlers.Invoke(new ValueChangedEvent<T>(oldValue, newValue));
            return;
        }

        var invocationList = handlers.GetInvocationList();

        for (int i = 0; i < invocationList.Length; i++)
        {
            // A handler that changed the value on its way through has already notified every
            // subscriber with the newer one, so this pass is superseded and stops here.
            // Continuing would hand the remaining subscribers a value that is no longer true, and
            // anything bound two-way would push that stale value straight back in which is not a
            // cosmetic glitch but an infinite ping-pong
            if (!EqualityComparer<T>.Default.Equals(value, newValue))
                return;

            ((Action<ValueChangedEvent<T>>)invocationList[i]).Invoke(new ValueChangedEvent<T>(oldValue, newValue));
        }
    }

    public override string ToString()
    {
        return $"{GetType().Name}(Value: {value}, Default: {defaultValue}, Disabled: {disabled})";
    }

    public static implicit operator T(Reactive<T> reactive) => reactive.Value;
}
