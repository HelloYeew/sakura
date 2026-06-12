// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
/// <para>
/// The per-level delegates are flattened into a single base-first array per concrete type on
/// first activation, so the steady-state cost of <see cref="Inject"/> and
/// <see cref="BuildChildDependencies"/> is one dictionary lookup plus a plain array loop —
/// no hierarchy walk, no list/closure allocations.
/// </para>
/// </summary>
public static class DependencyActivator
{
    private record struct ActivatorEntry(InjectDependenciesDelegate? InjectDelegate, CacheDependenciesDelegate? Cache);

    /// <summary>
    /// The flattened, base-first delegate chains for a concrete type.
    /// Only levels that actually have something to do are included.
    /// </summary>
    private sealed class ActivatorChain
    {
        public readonly InjectDependenciesDelegate[] Injects;
        public readonly CacheDependenciesDelegate[] Caches;

        public ActivatorChain(InjectDependenciesDelegate[] injects, CacheDependenciesDelegate[] caches)
        {
            Injects = injects;
            Caches = caches;
        }
    }

    private static readonly ConcurrentDictionary<Type, ActivatorEntry> activator_cache = new ConcurrentDictionary<Type, ActivatorEntry>();
    private static readonly ConcurrentDictionary<Type, ActivatorChain> chain_cache = new ConcurrentDictionary<Type, ActivatorChain>();
    private static readonly ActivatorProxy proxy = new ActivatorProxy();

    /// <summary>
    /// Injects all <see cref="ResolvedAttribute"/> members and invokes the
    /// <see cref="BackgroundDependencyLoaderAttribute"/> method on <paramref name="target"/>.
    /// Uses the source-generated fast path when available, otherwise falls back to reflection.
    /// </summary>
    public static void Inject(object target, IReadOnlyDependencyContainer dependencies)
    {
        var injects = getChain(target).Injects;

        // Base-first order so base-class [BackgroundDependencyLoader] methods run before derived ones.
        for (int i = 0; i < injects.Length; i++)
            injects[i](target, dependencies);
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
        var caches = getChain(target).Caches;

        // Build from base to most-derived, chaining containers so each level can add to the same child.
        IReadOnlyDependencyContainer? result = null;

        for (int i = 0; i < caches.Length; i++)
            result = caches[i](target, result ?? parent);

        return result ?? new DependencyContainer(parent);
    }

    /// <summary>
    /// Clears all cached activator entries. Called by <see cref="DependencyActivatorHotReloadHandler"/>
    /// after a hot reload compilation so stale delegates don't survive code changes.
    /// </summary>
    public static void ClearCache()
    {
        activator_cache.Clear();
        chain_cache.Clear();
        ReflectionDependencyActivator.ClearCache();
    }

    // -------------------------------------------------------------------------
    // Registration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the flattened activator chain for <paramref name="target"/>'s concrete type,
    /// registering the type hierarchy on first use.
    /// </summary>
    private static ActivatorChain getChain(object target)
    {
        if (chain_cache.TryGetValue(target.GetType(), out var chain))
            return chain;

        return registerAndBuildChain(target);
    }

    private static ActivatorChain registerAndBuildChain(object target)
    {
        var type = target.GetType();

        if (target is ISourceGeneratedDependencyActivator generated)
        {
            // The generated RegisterForDependencyActivation walks the full virtual chain
            // and calls proxy.Register() for each type level that has generated code.
            generated.RegisterForDependencyActivation(proxy);
        }

        // Register any levels the generated chain skipped (e.g. non-partial classes), or the
        // whole hierarchy when no generated code exists, via the reflection fallback so
        // [Resolved]/[Cached] members on those types still work.
        registerReflectionFallback(type);

        // Flatten the per-level entries into base-first arrays, skipping levels with nothing to do.
        var levels = new List<ActivatorEntry>();

        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            if (activator_cache.TryGetValue(current, out var entry))
                levels.Add(entry);
        }

        var injects = new List<InjectDependenciesDelegate>(levels.Count);
        var caches = new List<CacheDependenciesDelegate>(levels.Count);

        // levels is most-derived-first; iterate in reverse for base-first ordering.
        for (int i = levels.Count - 1; i >= 0; i--)
        {
            if (levels[i].InjectDelegate != null)
                injects.Add(levels[i].InjectDelegate!);
            if (levels[i].Cache != null)
                caches.Add(levels[i].Cache!);
        }

        var chain = new ActivatorChain(injects.ToArray(), caches.ToArray());
        chain_cache[type] = chain;
        return chain;
    }

    private static void registerReflectionFallback(Type type)
    {
        // Walk hierarchy so each level gets its own entry, matching the source-generated layout.
        Type? current = type;
        while (current != null && current != typeof(object))
        {
            if (!activator_cache.ContainsKey(current))
            {
                var injectDelegate = ReflectionDependencyActivator.GetInjectDelegate(current);
                var cacheDelegate = ReflectionDependencyActivator.GetCacheDelegate(current);

                // Only log when this level actually has DI members; an entry-less level is
                // not a problem worth pointing the user at.
                if (injectDelegate != null || cacheDelegate != null)
                {
                    string location = current.IsNested
                        ? $"nested class '{current.DeclaringType?.Name}.{current.Name}'"
                        : $"'{current.FullName}'";
                    Logger.Debug($"[DI] Reflection fallback for {location}. Make it (and any enclosing class) partial to enable source generation.");

                    GlobalStatistics.Get<int>("DI", "Reflection Fallback Uses").Value++;
                }

                activator_cache[current] = new ActivatorEntry(injectDelegate, cacheDelegate);
            }

            current = current.BaseType;
        }
    }

    private sealed class ActivatorProxy : IDependencyActivatorRegistry
    {
        public bool IsRegistered(Type type) => activator_cache.ContainsKey(type);

        public void Register(Type type, InjectDependenciesDelegate? inject, CacheDependenciesDelegate? cache) => activator_cache.TryAdd(type, new ActivatorEntry(inject, cache));
    }
}
