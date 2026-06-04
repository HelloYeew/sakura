// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace Sakura.Framework.Lists;

/// <summary>
/// A wrapper for an array that provides notifications when elements are changed.
/// </summary>
/// <typeparam name="T">The type of elements stored in the array.</typeparam>
public class ObservableArray<T> : IReadOnlyList<T>, IEquatable<ObservableArray<T>>
{
    /// <summary>
    /// Invoked when an element of the array is changed via <see cref="this[int]"/>.
    /// </summary>
    [CanBeNull]
    public event Action ArrayElementChanged;

    [NotNull]
    private readonly T[] wrappedArray;

    public ObservableArray(T[] arrayToWrap)
    {
        wrappedArray = arrayToWrap ?? throw new ArgumentNullException(nameof(arrayToWrap));
    }

    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>)wrappedArray).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return wrappedArray.GetEnumerator();
    }

    public bool Equals(ObservableArray<T> other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;

        return wrappedArray == other.wrappedArray;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;

        return obj.GetType() == GetType() && Equals((ObservableArray<T>)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(wrappedArray);
    }

    public int Count => wrappedArray.Length;

    public T this[int index]
    {
        get => wrappedArray[index];
        set
        {
            if (EqualityComparer<T>.Default.Equals(wrappedArray[index], value))
                return;

            wrappedArray[index] = value;

            OnArrayElementChanged();
        }
    }

    protected void OnArrayElementChanged()
    {
        ArrayElementChanged?.Invoke();
    }

    public static implicit operator ObservableArray<T>(T[] source) => source == null ? null : new ObservableArray<T>(source);
}
