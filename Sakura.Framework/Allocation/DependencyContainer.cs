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
    private readonly ConcurrentDictionary<Type, object> cache = new();

    public DependencyContainer(IReadOnlyDependencyContainer? parent = null)
    {
        this.parent = parent;
    }

    /// <summary>
    /// Cache a dependency instance of the specified type.
    /// </summary>
    public void Cache<T>(T instance) where T : class
    {
        cache[typeof(T)] = instance;
        GlobalStatistics.Get<int>("DI", "Cached Dependencies").Value = cache.Count;
    }

    /// <summary>
    /// Caches <paramref name="instance"/> under a different type <typeparamref name="T"/>.
    /// Use this when a concrete type should be resolvable as an interface or base type.
    /// </summary>
    public void CacheAs<T>(object instance) where T : class
    {
        cache[typeof(T)] = instance;
        GlobalStatistics.Get<int>("DI", "Cached Dependencies").Value = cache.Count;
    }

    /// <summary>
    /// Retrieves a dependency of type <typeparamref name="T"/>.
    /// Walks up to the parent container if not found locally.
    /// </summary>
    public T Get<T>() where T : class
    {
        if (cache.TryGetValue(typeof(T), out object? obj))
            return (T)obj;

        if (parent != null)
            return parent.Get<T>();

        throw new InvalidOperationException($"Dependency of type {typeof(T).FullName} not found.");
    }
}
