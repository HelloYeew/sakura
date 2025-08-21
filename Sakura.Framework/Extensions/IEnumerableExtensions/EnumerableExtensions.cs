// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;

namespace Sakura.Framework.Extensions.IEnumerableExtensions;

public static class EnumerableExtensions
{
    /// <summary>
    /// Wraps this object instance into an <see cref="IEnumerable{T}"/>
    /// consisting of a single item.
    /// </summary>
    /// <typeparam name="T">The type of the object.</typeparam>
    /// <param name="item">The instance that will be wrapped.</param>
    /// <returns> An <see cref="IEnumerable{T}"/> consisting of a single item.</returns>
    public static IEnumerable<T> Yield<T>(this T item) => new[] { item };
}
