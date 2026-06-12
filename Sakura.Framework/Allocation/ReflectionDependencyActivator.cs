// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Sakura.Framework.Allocation;

/// <summary>
/// Fallback DI activator used for types that were not processed by the source generator
/// (e.g., types in assemblies that don't reference the generator, or during hot reload).
/// <para>
/// Builds inject/cache delegates by compiling expression trees once per type, so the
/// steady-state cost is a single delegate call with direct (JIT-compiled) member access —
/// no <see cref="MethodInfo.Invoke"/>, boxing or argument arrays per activation.
/// </para>
/// </summary>
internal static class ReflectionDependencyActivator
{
    private static readonly ConcurrentDictionary<Type, InjectDependenciesDelegate> inject_cache = new ConcurrentDictionary<Type, InjectDependenciesDelegate>();
    private static readonly ConcurrentDictionary<Type, CacheDependenciesDelegate> cache_delegates = new ConcurrentDictionary<Type, CacheDependenciesDelegate>();

    private static readonly MethodInfo get_method =
        typeof(IReadOnlyDependencyContainer).GetMethod(nameof(IReadOnlyDependencyContainer.Get), BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Could not find '{nameof(IReadOnlyDependencyContainer.Get)}' on {nameof(IReadOnlyDependencyContainer)}.");

    private static readonly MethodInfo cache_as_method =
        typeof(DependencyContainer).GetMethod(nameof(DependencyContainer.CacheAs), BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Could not find '{nameof(DependencyContainer.CacheAs)}' on {nameof(DependencyContainer)}.");

    private static readonly ConstructorInfo container_ctor =
        typeof(DependencyContainer).GetConstructor(new[] { typeof(IReadOnlyDependencyContainer) })
        ?? throw new InvalidOperationException($"Could not find the parent constructor on {nameof(DependencyContainer)}.");

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
    /// Returns null if this type level has no inject work, so empty levels cost nothing.
    /// </summary>
    internal static InjectDependenciesDelegate? GetInjectDelegate(Type type)
    {
        if (inject_cache.TryGetValue(type, out var d))
            return d;

        var built = buildInjectDelegate(type);
        if (built != null)
            inject_cache[type] = built;

        return built;
    }

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
    /// Builds an expression that resolves <paramref name="dependencyType"/> from the container parameter.
    /// </summary>
    private static Expression resolveExpression(ParameterExpression depsParam, Type dependencyType)
        => Expression.Call(depsParam, get_method.MakeGenericMethod(dependencyType));

    /// <summary>
    /// Builds an inject delegate that handles only members <b>declared directly on <paramref name="type"/></b>
    /// (i.e. <c>BindingFlags.DeclaredOnly</c>). <see cref="DependencyActivator"/> walks the full hierarchy
    /// by calling per-level delegates, so we must not re-walk ancestors here.
    /// All member injections and the loader invocation are compiled into one delegate.
    /// </summary>
    private static InjectDependenciesDelegate? buildInjectDelegate(Type type)
    {
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var depsParam = Expression.Parameter(typeof(IReadOnlyDependencyContainer), "deps");
        var typedInstance = Expression.Convert(instanceParam, type);

        var body = new List<Expression>();

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

                body.Add(Expression.Call(typedInstance, setter, resolveExpression(depsParam, property.PropertyType)));
            }
            else if (member is FieldInfo field)
            {
                body.Add(Expression.Assign(
                    Expression.Field(typedInstance, field),
                    resolveExpression(depsParam, field.FieldType)));
            }
        }

        // [BackgroundDependencyLoader] declared directly on this type only.
        var loaderMethod = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.GetCustomAttribute<BackgroundDependencyLoaderAttribute>() != null);

        if (loaderMethod != null)
        {
            var args = loaderMethod.GetParameters()
                .Select(p => resolveExpression(depsParam, p.ParameterType))
                .ToArray();

            body.Add(Expression.Call(typedInstance, loaderMethod, args));
        }

        if (body.Count == 0)
            return null;

        return Expression.Lambda<InjectDependenciesDelegate>(
            Expression.Block(body), instanceParam, depsParam).Compile();
    }

    /// <summary>
    /// Builds a cache delegate for members <b>declared directly on <paramref name="type"/></b> only.
    /// Returns null if this type level has no [Cached] members.
    /// The container construction and all CacheAs calls are compiled into one delegate.
    /// </summary>
    private static CacheDependenciesDelegate? buildCacheDelegate(Type type)
    {
        var targetParam = Expression.Parameter(typeof(object), "target");
        var parentParam = Expression.Parameter(typeof(IReadOnlyDependencyContainer), "parent");
        var containerVar = Expression.Variable(typeof(DependencyContainer), "container");
        var typedTarget = Expression.Convert(targetParam, type);

        var steps = new List<Expression>();

        // container.CacheAs<cacheType>(value)
        void addCacheStep(Type cacheType, Expression value) =>
            steps.Add(Expression.Call(
                containerVar,
                cache_as_method.MakeGenericMethod(cacheType),
                Expression.Convert(value, typeof(object))));

        // Class-level [Cached] attributes — cache `this` under the specified type(s).
        // GetCustomAttributes with inherit:false so we only see attributes declared on this type.
        foreach (CachedAttribute attr in type.GetCustomAttributes(typeof(CachedAttribute), inherit: false))
            addCacheStep(attr.CacheAs ?? type, targetParam);

        // Member-level [Cached] — DeclaredOnly, base levels handled by their own per-level delegates.
        foreach (var member in type.GetMembers(
                     BindingFlags.Public | BindingFlags.NonPublic |
                     BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (CachedAttribute attr in member.GetCustomAttributes(typeof(CachedAttribute), inherit: false))
            {
                if (member is PropertyInfo property)
                {
                    if (property.GetGetMethod(true) == null) continue;

                    addCacheStep(attr.CacheAs ?? property.PropertyType, Expression.Property(typedTarget, property));
                }
                else if (member is FieldInfo field)
                {
                    addCacheStep(attr.CacheAs ?? field.FieldType, Expression.Field(typedTarget, field));
                }
            }
        }

        if (steps.Count == 0)
            return null;

        // (target, parent) => { var container = new DependencyContainer(parent); ...steps...; return container; }
        var body = new List<Expression>
        {
            Expression.Assign(containerVar, Expression.New(container_ctor, parentParam))
        };
        body.AddRange(steps);
        body.Add(Expression.Convert(containerVar, typeof(IReadOnlyDependencyContainer)));

        return Expression.Lambda<CacheDependenciesDelegate>(
            Expression.Block(typeof(IReadOnlyDependencyContainer), new[] { containerVar }, body),
            targetParam, parentParam).Compile();
    }
}
