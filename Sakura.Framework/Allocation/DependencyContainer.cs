// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Allocation;

/// <summary>
/// A container for managing and resolving dependencies.
/// <remarks>
/// Since 2026.609.0 the activator moved to <see cref="DependencyActivator"/>
/// This class now just a storage.
/// </remarks>
/// </summary>
public class DependencyContainer : IReadOnlyDependencyContainer
{
    private readonly IReadOnlyDependencyContainer? parent;

    // Lazily allocated: most drawables never cache anything, so most containers stay empty.
    // Keeping empty containers dictionary-free makes the per-drawable container nearly free
    // and keeps deep-tree resolution walks cheap.
    private ConcurrentDictionary<Type, object>? cache;

    private static readonly GlobalStatistic<int> cached_count_statistic = GlobalStatistics.Get<int>("DI", "Cached Dependencies");

    public DependencyContainer(IReadOnlyDependencyContainer? parent = null)
    {
        this.parent = parent;
    }

    /// <summary>
    /// Cache a dependency instance of the specified type.
    /// </summary>
    public void Cache<T>(T instance) where T : class => cacheAs(typeof(T), instance);

    /// <summary>
    /// Caches <paramref name="instance"/> under a different type <typeparamref name="T"/>.
    /// Use this when a concrete type should be resolvable as an interface or base type.
    /// </summary>
    public void CacheAs<T>(object instance) where T : class => cacheAs(typeof(T), instance);

    private void cacheAs(Type type, object instance)
    {
        var c = cache ??= new ConcurrentDictionary<Type, object>();
        c[type] = instance;
        cached_count_statistic.Value++;
    }

    /// <summary>
    /// Retrieves a dependency of type <typeparamref name="T"/>, or null when not found.
    /// Walks up to the parent container if not found locally.
    /// </summary>
    public T? TryGet<T>() where T : class
    {
        IReadOnlyDependencyContainer? current = this;

        while (current is DependencyContainer dc)
        {
            var c = dc.cache;
            if (c != null && c.TryGetValue(typeof(T), out object? obj))
                return (T)obj;

            current = dc.parent;
        }

        return current?.TryGet<T>();
    }

    /// <summary>
    /// Retrieves a dependency of type <typeparamref name="T"/>.
    /// Walks up to the parent container if not found locally.
    /// </summary>
    public T Get<T>() where T : class
    {
        // Iterative walk: deep drawable trees produce long chains of containers that cache
        // nothing, so the per-level cost must stay at a couple of null checks (no recursion).
        IReadOnlyDependencyContainer? current = this;

        while (current is DependencyContainer dc)
        {
            var c = dc.cache;
            if (c != null && c.TryGetValue(typeof(T), out object? obj))
                return (T)obj;

            current = dc.parent;
        }

        // A non-DependencyContainer implementation in the chain resolves through the interface.
        if (current != null)
            return current.Get<T>();

        throw new InvalidOperationException($"Dependency of type {typeof(T).FullName} not found.");
    }
}
