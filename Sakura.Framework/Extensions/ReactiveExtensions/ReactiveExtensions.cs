// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Extensions.ReactiveExtensions;

public static class ReactiveExtensions
{
    /// <summary>
    /// Binds <paramref name="target"/> and <paramref name="source"/> so that a change to either
    /// reaches the other, with the target adopting the source's current value.
    /// </summary>
    /// <param name="target">The side that adopts the current value, typically a control.</param>
    /// <param name="source">The authoritative side, typically a configuration entry.</param>
    public static void BindBothWaysTo<T>(this IReactive<T> target, IReactive<T> source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        target.BindTo(source);
        source.BindTo(target);
    }
}
