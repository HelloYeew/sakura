// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sakura.Framework.Allocation;

/// <summary>
/// A container for managing and resolving dependencies.
/// </summary>
public class DependencyContainer : IReadOnlyDependencyContainer
{
    private readonly IReadOnlyDependencyContainer? parent;
    private readonly ConcurrentDictionary<Type, object> cache = new();

    private static readonly ConcurrentDictionary<Type, Action<object, IReadOnlyDependencyContainer>> injection_delegates = new();

    public DependencyContainer(IReadOnlyDependencyContainer? parent = null)
    {
        this.parent = parent;
    }

    /// <summary>
    /// Cache a dependency instance of the specified type.
    /// </summary>
    /// <param name="instance">The instance to cache.</param>
    /// <typeparam name="T">The type of the dependency to cache as.</typeparam>
    public void Cache<T>(T instance) where T : class
    {
        cache[typeof(T)] = instance!;
    }

    /// <summary>
    /// Retrieves a dependency of the specified type. If not found in this container,
    /// it will attempt to retrieve it from the parent container if available.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public T Get<T>() where T : class
    {
        if (cache.TryGetValue(typeof(T), out object? obj))
            return (T)obj;

        if (parent != null)
            return parent.Get<T>();

        throw new InvalidOperationException($"Dependency of type {typeof(T).FullName} not found.");
    }

    /// <summary>
    /// Inject dependencies into the fields and properties of the given instance
    /// </summary>
    /// <param name="instance">Instance to inject dependencies into.</param>
    /// <typeparam name="T">Type of the instance.</typeparam>
    public void Inject<T>(T instance) where T : class
    {
        var injector = injection_delegates.GetOrAdd(instance.GetType(), createInjector);
        injector(instance, this);

        parent?.Inject(instance);
    }

    private static Action<object, IReadOnlyDependencyContainer> createInjector(Type type)
    {
        var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<ResolvedAttribute>() != null);

        var delegates = new List<Action<object, IReadOnlyDependencyContainer>>();

        var getMethod = typeof(IReadOnlyDependencyContainer).GetMethod(nameof(Get), BindingFlags.Public | BindingFlags.Instance);
        if (getMethod == null)
            throw new InvalidOperationException($"Could not find the '{nameof(Get)}' method on {nameof(IReadOnlyDependencyContainer)}.");

        foreach (var member in members)
        {
            if (member is PropertyInfo property)
            {
                var setMethod = property.SetMethod;
                if (setMethod == null)
                    throw new InvalidOperationException($"Member {type.Name}.{member.Name} is marked with [Resolved] but has no setter.");

                var concreteGetMethod = getMethod.MakeGenericMethod(property.PropertyType);

                delegates.Add((instance, container) =>
                {
                    object? dependency = concreteGetMethod.Invoke(container, null);
                    setMethod.Invoke(instance, new[] { dependency });
                });
            }
            else if (member is FieldInfo field)
            {
                var concreteGetMethod = getMethod.MakeGenericMethod(field.FieldType);

                delegates.Add((instance, container) =>
                {
                    object? dependency = concreteGetMethod.Invoke(container, null);
                    field.SetValue(instance, dependency);
                });
            }
        }

        return (instance, container) =>
        {
            foreach (var d in delegates)
            {
                d(instance, container);
            }
        };
    }
}
