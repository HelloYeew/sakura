// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sakura.Framework.Allocation;

/// <summary>
/// Fallback DI activator used for types that were not processed by the source generator
/// (e.g., types in assemblies that don't reference the generator, or during hot reload).
/// Uses reflection to build inject/cache delegates, caching them per type so the reflection
/// cost is paid only once.
/// </summary>
internal static class ReflectionDependencyActivator
{
    private static readonly ConcurrentDictionary<Type, InjectDependenciesDelegate> inject_cache = new ConcurrentDictionary<Type, InjectDependenciesDelegate>();
    private static readonly ConcurrentDictionary<Type, CacheDependenciesDelegate> cache_delegates = new ConcurrentDictionary<Type, CacheDependenciesDelegate>();

    private static readonly MethodInfo get_method =
        typeof(IReadOnlyDependencyContainer).GetMethod(nameof(IReadOnlyDependencyContainer.Get), BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Could not find '{nameof(IReadOnlyDependencyContainer.Get)}' on {nameof(IReadOnlyDependencyContainer)}.");

    /// <summary>
    /// Clears all cached reflection delegates. Called on hot reload.
    /// </summary>
    internal static void ClearCache()
    {
        inject_cache.Clear();
        cache_delegates.Clear();
    }

    /// <summary>
    /// Returns (or builds and caches) an inject delegate for <paramref name="type"/>.
    /// The delegate fills all <see cref="ResolvedAttribute"/> members and invokes the
    /// <see cref="BackgroundDependencyLoaderAttribute"/> method if present.
    /// </summary>
    internal static InjectDependenciesDelegate GetInjectDelegate(Type type)
        => inject_cache.GetOrAdd(type, buildInjectDelegate);

    /// <summary>
    /// Returns (or builds and caches) a cache delegate for <paramref name="type"/>.
    /// The delegate registers all <see cref="CachedAttribute"/> members into a new child container.
    /// Returns null if the type has no cacheable members.
    /// </summary>
    internal static CacheDependenciesDelegate? GetCacheDelegate(Type type)
    {
        if (cache_delegates.TryGetValue(type, out var d))
            return d;

        var built = buildCacheDelegate(type);
        if (built != null)
            cache_delegates[type] = built;

        return built;
    }

    /// <summary>
    /// Builds an inject delegate that handles only members <b>declared directly on <paramref name="type"/></b>
    /// (i.e. <c>BindingFlags.DeclaredOnly</c>). <see cref="DependencyActivator"/> walks the full hierarchy
    /// by calling per-level delegates, so we must not re-walk ancestors here.
    /// </summary>
    private static InjectDependenciesDelegate buildInjectDelegate(Type type)
    {
        var steps = new List<Action<object, IReadOnlyDependencyContainer>>();

        // Only members declared at this exact type level.
        foreach (var member in type.GetMembers(
                     BindingFlags.Public | BindingFlags.NonPublic |
                     BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (member.GetCustomAttribute<ResolvedAttribute>() == null)
                continue;

            if (member is PropertyInfo property)
            {
                var setter = property.GetSetMethod(true);
                if (setter == null)
                    throw new InvalidOperationException(
                        $"Member {type.Name}.{property.Name} is marked [Resolved] but has no setter.");

                var getter = get_method.MakeGenericMethod(property.PropertyType);
                steps.Add((instance, deps) =>
                    setter.Invoke(instance, new[] { getter.Invoke(deps, null) }));
            }
            else if (member is FieldInfo field)
            {
                var getter = get_method.MakeGenericMethod(field.FieldType);
                steps.Add((instance, deps) =>
                    field.SetValue(instance, getter.Invoke(deps, null)));
            }
        }

        // [BackgroundDependencyLoader] declared directly on this type only.
        var loaderMethod = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.GetCustomAttribute<BackgroundDependencyLoaderAttribute>() != null);

        if (loaderMethod != null)
        {
            var parameters = loaderMethod.GetParameters();
            var paramGetters = parameters
                .Select(p => get_method.MakeGenericMethod(p.ParameterType))
                .ToArray();

            steps.Add((instance, deps) =>
            {
                object?[] args = new object?[paramGetters.Length];
                for (int i = 0; i < paramGetters.Length; i++)
                    args[i] = paramGetters[i].Invoke(deps, null);
                loaderMethod.Invoke(instance, args);
            });
        }

        return (instance, deps) =>
        {
            foreach (var step in steps)
                step(instance, deps);
        };
    }

    /// <summary>
    /// Builds a cache delegate for members <b>declared directly on <paramref name="type"/></b> only.
    /// Returns null if this type level has no [Cached] members.
    /// </summary>
    private static CacheDependenciesDelegate? buildCacheDelegate(Type type)
    {
        var steps = new List<Action<object, DependencyContainer>>();

        // Class-level [Cached] attributes — cache `this` under the specified type(s).
        // GetCustomAttributes with inherit:false so we only see attributes declared on this type.
        foreach (CachedAttribute attr in type.GetCustomAttributes(typeof(CachedAttribute), inherit: false))
        {
            var cacheType = attr.CacheAs ?? type;
            var cacheMethod = typeof(DependencyContainer)
                .GetMethod(nameof(DependencyContainer.CacheAs), BindingFlags.Public | BindingFlags.Instance)!
                .MakeGenericMethod(cacheType);

            steps.Add((instance, container) => cacheMethod.Invoke(container, new[] { instance }));
        }

        // Member-level [Cached] — DeclaredOnly, base levels handled by their own per-level delegates.
        foreach (var member in type.GetMembers(
                     BindingFlags.Public | BindingFlags.NonPublic |
                     BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (CachedAttribute attr in member.GetCustomAttributes(typeof(CachedAttribute), inherit: false))
            {
                if (member is PropertyInfo property)
                {
                    var getter = property.GetGetMethod(true);
                    if (getter == null) continue;

                    var cacheType = attr.CacheAs ?? property.PropertyType;
                    var cacheMethod = typeof(DependencyContainer)
                        .GetMethod(nameof(DependencyContainer.CacheAs), BindingFlags.Public | BindingFlags.Instance)!
                        .MakeGenericMethod(cacheType);

                    steps.Add((instance, container) =>
                        cacheMethod.Invoke(container, new[] { getter.Invoke(instance, null) }));
                }
                else if (member is FieldInfo field)
                {
                    var cacheType = attr.CacheAs ?? field.FieldType;
                    var cacheMethod = typeof(DependencyContainer)
                        .GetMethod(nameof(DependencyContainer.CacheAs), BindingFlags.Public | BindingFlags.Instance)!
                        .MakeGenericMethod(cacheType);

                    steps.Add((instance, container) =>
                        cacheMethod.Invoke(container, new[] { field.GetValue(instance) }));
                }
            }
        }

        if (steps.Count == 0)
            return null;

        return (instance, parent) =>
        {
            var container = new DependencyContainer(parent);
            foreach (var step in steps)
                step(instance, container);
            return container;
        };
    }
}
