// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Allocation;

/// <summary>
/// Central dispatcher for the Sakura DI system.
/// <para>
/// For types processed by the source generator, delegates are registered once via
/// <see cref="ISourceGeneratedDependencyActivator.RegisterForDependencyActivation"/> and then
/// called directly with no reflection overhead on subsequent activations.
/// </para>
/// <para>
/// For types that's not processed by the generator (e.g. assemblies without the generator reference,
/// or types loaded after hot reload), <see cref="ReflectionDependencyActivator"/> is used as a
/// transparent fallback and its results are also cached per type.
/// </para>
/// </summary>
public static class DependencyActivator
{
    private record struct ActivatorEntry(InjectDependenciesDelegate? InjectDelegate, CacheDependenciesDelegate? Cache);

    private static readonly ConcurrentDictionary<Type, ActivatorEntry> activator_cache = new ConcurrentDictionary<Type, ActivatorEntry>();
    private static readonly ActivatorProxy proxy = new ActivatorProxy();

    /// <summary>
    /// Injects all <see cref="ResolvedAttribute"/> members and invokes the
    /// <see cref="BackgroundDependencyLoaderAttribute"/> method on <paramref name="target"/>.
    /// Uses the source-generated fast path when available, otherwise falls back to reflection.
    /// </summary>
    public static void Inject(object target, IReadOnlyDependencyContainer dependencies)
    {
        ensureRegistered(target);
        var type = target.GetType();

        // Walk from base to most-derived, calling the inject delegate for each registered level.
        activateHierarchy(type, entry =>
        {
            entry.InjectDelegate?.Invoke(target, dependencies);
        });
    }

    /// <summary>
    /// Builds a child <see cref="IReadOnlyDependencyContainer"/> for <paramref name="target"/>.
    /// If the type (or any base type) has <see cref="CachedAttribute"/> members they are registered
    /// into the new container so child drawables can resolve them.
    /// Returns a plain <see cref="DependencyContainer"/> wrapping <paramref name="parent"/>
    /// when nothing needs to be cached.
    /// </summary>
    public static IReadOnlyDependencyContainer BuildChildDependencies(object target, IReadOnlyDependencyContainer? parent)
    {
        ensureRegistered(target);
        var type = target.GetType();

        // Build from base to most-derived, chaining containers so each level can add to the same child.
        IReadOnlyDependencyContainer? result = null;

        activateHierarchy(type, entry =>
        {
            if (entry.Cache != null)
                result = entry.Cache(target, result ?? parent);
        });

        return result ?? new DependencyContainer(parent);
    }

    /// <summary>
    /// Clears all cached activator entries. Called by <see cref="DependencyActivatorHotReloadHandler"/>
    /// after a hot reload compilation so stale delegates don't survive code changes.
    /// </summary>
    public static void ClearCache()
    {
        activator_cache.Clear();
        ReflectionDependencyActivator.ClearCache();
    }

    // -------------------------------------------------------------------------
    // Registration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Ensures the type hierarchy of <paramref name="target"/> has been registered.
    /// If <paramref name="target"/> implements <see cref="ISourceGeneratedDependencyActivator"/>,
    /// the generated virtual chain is walked once to populate the cache.
    /// Otherwise the reflection fallback registers the type.
    /// </summary>
    private static void ensureRegistered(object target)
    {
        var type = target.GetType();

        // chech whether already fully registered.
        if (activator_cache.ContainsKey(type))
            return;

        if (target is ISourceGeneratedDependencyActivator generated)
        {
            // The generated RegisterForDependencyActivation walks the full virtual chain
            // and calls proxy.Register() for each type level that has generated code.
            generated.RegisterForDependencyActivation(proxy);

            // Some levels in the hierarchy may not be partial (e.g. Container, TestScene,
            // or concrete test classes), so the generated chain skips them. Fill in any
            // gaps with the reflection fallback so [Resolved] members on those types are
            // still injected.
            registerReflectionFallback(type);
        }
        else
        {
            // Reflection fallback: register just this type.
            registerReflectionFallback(type);
        }
    }

    private static void registerReflectionFallback(Type type)
    {
        // Walk hierarchy so each level gets its own entry, matching the source-generated layout.
        Type? current = type;
        while (current != null && current != typeof(object))
        {
            if (!activator_cache.ContainsKey(current))
            {
                string location = current.IsNested
                    ? $"nested class '{current.DeclaringType?.Name}.{current.Name}'"
                    : $"'{current.FullName}'";
                Logger.Debug($"[DI] Reflection fallback for {location}. Make it (and any enclosing class) partial to enable source generation.");

                activator_cache[current] = new ActivatorEntry(
                    ReflectionDependencyActivator.GetInjectDelegate(current),
                    ReflectionDependencyActivator.GetCacheDelegate(current)
                );

                GlobalStatistics.Get<int>("DI", "Reflection Fallback Uses").Value++;
            }
            current = current.BaseType;
        }
    }

    /// <summary>
    /// Walks from the least-derived registered type down to <paramref name="type"/>,
    /// invoking <paramref name="action"/> for each level that has an entry in the cache.
    /// This preserves base-first ordering so base-class [BackgroundDependencyLoader] methods
    /// run before derived-class ones.
    /// </summary>
    private static void activateHierarchy(Type type, Action<ActivatorEntry> action)
    {
        // Collect the chain from most-derived to base, then reverse.
        // We only care about types that actually have registered entries.
        var chain = new System.Collections.Generic.List<ActivatorEntry>();

        Type? current = type;
        while (current != null && current != typeof(object))
        {
            if (activator_cache.TryGetValue(current, out var entry))
                chain.Add(entry);
            current = current.BaseType;
        }

        // Reverse so base runs first.
        chain.Reverse();

        foreach (var entry in chain)
            action(entry);
    }

    private sealed class ActivatorProxy : IDependencyActivatorRegistry
    {
        public bool IsRegistered(Type type) => activator_cache.ContainsKey(type);

        public void Register(Type type, InjectDependenciesDelegate? inject, CacheDependenciesDelegate? cache) => activator_cache.TryAdd(type, new ActivatorEntry(inject, cache));
    }
}
