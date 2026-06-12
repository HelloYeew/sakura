// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Attributes;
using Sakura.Framework.Allocation;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Measures the dependency injection system. DI runs on every drawable load
/// (<see cref="DependencyActivator.BuildChildDependencies"/> + <see cref="DependencyActivator.Inject"/>),
/// so its steady-state (warm cache) cost is a per-spawn tax — critical for non-pooled
/// note spawning in rhythm gameplay. Resolution cost matters per [Resolved] member and
/// per manual <c>Dependencies.Get</c> call.
/// </summary>
[MemoryDiagnoser]
public class DependencyInjectionBenchmarks
{
    private DependencyContainer dependencies = null!;
    private DependencyContainer chainLeaf = null!;
    private DependencyContainer cacheTarget = null!;

    private ReflectionInjectTarget reflectionTarget = null!;
    private SourceGenInjectTarget sourceGenTarget = null!;
    private DeepLevel5 deepTarget = null!;
    private EmptyTarget emptyTarget = null!;
    private CachedProviderTarget cachedProviderTarget = null!;

    private readonly FakeConfigService configInstance = new FakeConfigService();

    [GlobalSetup]
    public void Setup()
    {
        dependencies = new DependencyContainer();
        dependencies.Cache(new FakeConfigService());
        dependencies.Cache(new FakeAudioService());
        dependencies.Cache(new FakeTextureService());

        // A 10-deep chain of containers that cache nothing, with the dependency at the root —
        // models resolving from a leaf drawable in a deep tree where no ancestor caches anything.
        DependencyContainer current = dependencies;
        for (int i = 0; i < 10; i++)
            current = new DependencyContainer(current);
        chainLeaf = current;

        cacheTarget = new DependencyContainer();

        reflectionTarget = new ReflectionInjectTarget();
        sourceGenTarget = new SourceGenInjectTarget();
        deepTarget = new DeepLevel5();
        emptyTarget = new EmptyTarget();
        cachedProviderTarget = new CachedProviderTarget();

        // Warm the activator caches so benchmarks measure steady state, not first-use registration.
        DependencyActivator.Inject(reflectionTarget, dependencies);
        DependencyActivator.Inject(sourceGenTarget, dependencies);
        DependencyActivator.Inject(deepTarget, dependencies);
        DependencyActivator.BuildChildDependencies(emptyTarget, dependencies);
        DependencyActivator.BuildChildDependencies(cachedProviderTarget, dependencies);
    }

    /// <summary>
    /// A single resolution that hits the local container — the floor cost of any Get.
    /// </summary>
    [Benchmark(Baseline = true)]
    public FakeConfigService Container_GetLocal() => dependencies.Get<FakeConfigService>();

    /// <summary>
    /// A resolution that walks 10 empty containers before hitting — the cost a leaf drawable
    /// pays in a deep tree. Highlights per-level container walk overhead.
    /// </summary>
    [Benchmark]
    public FakeConfigService Container_GetThroughChain10() => chainLeaf.Get<FakeConfigService>();

    /// <summary>
    /// Caching one instance into a container (the per-[Cached]-member cost during load).
    /// </summary>
    [Benchmark]
    public void Container_Cache() => cacheTarget.Cache(configInstance);

    /// <summary>
    /// Warm injection via the reflection fallback: two [Resolved] members + one
    /// [BackgroundDependencyLoader] parameter. This is the per-spawn cost for game
    /// assemblies that don't reference the source generator.
    /// </summary>
    [Benchmark]
    public void Inject_ReflectionFallback() => DependencyActivator.Inject(reflectionTarget, dependencies);

    /// <summary>
    /// Warm injection via the source-generated path, same shape as the reflection target.
    /// The gap between this and <see cref="Inject_ReflectionFallback"/> is the fallback overhead.
    /// </summary>
    [Benchmark]
    public void Inject_SourceGenerated() => DependencyActivator.Inject(sourceGenTarget, dependencies);

    /// <summary>
    /// Warm injection on a 5-level class hierarchy with one [Resolved] member per level —
    /// measures the per-level hierarchy walk inside the activator.
    /// </summary>
    [Benchmark]
    public void Inject_DeepHierarchy5() => DependencyActivator.Inject(deepTarget, dependencies);

    /// <summary>
    /// Building child dependencies for a type with no [Cached] members — paid by every
    /// drawable on load even when it provides nothing to children.
    /// </summary>
    [Benchmark]
    public IReadOnlyDependencyContainer BuildChildDependencies_Empty()
        => DependencyActivator.BuildChildDependencies(emptyTarget, dependencies);

    /// <summary>
    /// Building child dependencies for a type with a class-level [Cached] and one
    /// [Cached] member — the cost of being a dependency provider.
    /// </summary>
    [Benchmark]
    public IReadOnlyDependencyContainer BuildChildDependencies_WithCached()
        => DependencyActivator.BuildChildDependencies(cachedProviderTarget, dependencies);

    /// <summary>
    /// The full per-drawable DI load path: build child container, then inject.
    /// </summary>
    [Benchmark]
    public void FullLoadPath_BuildAndInject()
    {
        var deps = DependencyActivator.BuildChildDependencies(reflectionTarget, dependencies);
        DependencyActivator.Inject(reflectionTarget, deps);
    }
}

// Benchmark target types. Kept at namespace level so the source generator can
// process the partial one; the non-partial ones intentionally take the
// reflection fallback path.

public class FakeConfigService { }

public class FakeAudioService { }

public class FakeTextureService { }

/// <summary>
/// Not partial — the source generator skips it, forcing the reflection fallback.
/// </summary>
#pragma warning disable SFDI0001
public class ReflectionInjectTarget : IDependencyInjectionCandidate
#pragma warning restore SFDI0001
{
    [Resolved]
    private FakeConfigService config { get; set; } = null!;

    [Resolved]
    private FakeAudioService audio { get; set; } = null!;

    [BackgroundDependencyLoader]
    private void load(FakeTextureService textures)
    {
    }
}

/// <summary>
/// Partial — processed by the source generator (fast path).
/// </summary>
public partial class SourceGenInjectTarget : IDependencyInjectionCandidate
{
    [Resolved]
    private FakeConfigService config { get; set; } = null!;

    [Resolved]
    private FakeAudioService audio { get; set; } = null!;

    [BackgroundDependencyLoader]
    private void load(FakeTextureService textures)
    {
    }
}

#pragma warning disable SFDI0001
public class DeepLevel1 : IDependencyInjectionCandidate
#pragma warning restore SFDI0001
{
    [Resolved]
    private FakeConfigService dep1 { get; set; } = null!;
}

#pragma warning disable SFDI0001
public class DeepLevel2 : DeepLevel1
#pragma warning restore SFDI0001
{
    [Resolved]
    private FakeAudioService dep2 { get; set; } = null!;
}

#pragma warning disable SFDI0001
public class DeepLevel3 : DeepLevel2
#pragma warning restore SFDI0001
{
    [Resolved]
    private FakeTextureService dep3 { get; set; } = null!;
}

#pragma warning disable SFDI0001
public class DeepLevel4 : DeepLevel3
#pragma warning restore SFDI0001
{
    [Resolved]
    private FakeConfigService dep4 { get; set; } = null!;
}

#pragma warning disable SFDI0001
public class DeepLevel5 : DeepLevel4
#pragma warning restore SFDI0001
{
    [Resolved]
    private FakeAudioService dep5 { get; set; } = null!;
}

/// <summary>
/// No DI members at all — the common case for most drawables.
/// </summary>
public class EmptyTarget : IDependencyInjectionCandidate
{
}

/// <summary>
/// Provides dependencies: cached as itself (class-level) plus one cached member.
/// </summary>
[Cached]
public class CachedProviderTarget : IDependencyInjectionCandidate
{
    [Cached]
    private FakeConfigService provided { get; set; } = new FakeConfigService();
}
