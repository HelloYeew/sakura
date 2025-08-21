// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Reactive;

public interface IReactiveNumber<T> : IReactive<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>
    /// The event that is raised when the <see cref="MinValue"/> changes.
    /// </summary>
    event Action<ValueChangedEvent<T>> MinValueChanged;

    /// <summary>
    /// The event that is raised when the <see cref="MaxValue"/> changes.
    /// </summary>
    event Action<ValueChangedEvent<T>> MaxValueChanged;

    /// <summary>
    /// The event that is raised when the <see cref="Precision"/> changes.
    /// </summary>
    event Action<ValueChangedEvent<T>> PrecisionChanged;

    /// <summary>
    /// The minimum value this reactive number can hold.
    /// </summary>
    T MinValue { get; set; }

    /// <summary>
    /// The maximum value this reactive number can hold.
    /// </summary>
    T MaxValue { get; set; }

    /// <summary>
    /// The precision of this reactive number which determines the value of this reactive number should be rounded to.
    /// </summary>
    T Precision { get; set; }

    /// <summary>
    /// Whether <typeparamref name="T"/> is an integer type.
    /// </summary>
    bool IsInteger { get; }
}
