// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Allocation;

/// <summary>
/// Declaratively registers a dependency so that child drawables can resolve it via <see cref="ResolvedAttribute"/>.
/// <para>
/// Can be applied to a class to cache <c>this</c> instance, or to a field/property to cache that member's value.
/// An optional <see cref="CacheAs"/> type allows caching under an interface or base type rather than the concrete type.
/// </para>
/// <example>
/// Cache the class itself (children can resolve MyComponent):
/// <code>
/// [Cached]
/// public partial class MyComponent : CompositeDrawable { }
/// </code>
/// Cache as a specific interface (children resolve IMyService, not MyComponent):
/// <code>
/// [Cached(typeof(IMyService))]
/// public partial class MyComponent : CompositeDrawable { }
/// </code>
/// Cache a field/property:
/// <code>
/// [Cached]
/// private readonly AudioManager audioManager = new AudioManager();
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public class CachedAttribute : Attribute
{
    /// <summary>
    /// The type to cache the dependency as.
    /// If null, the actual type of the member or class is used.
    /// </summary>
    public Type? CacheAs { get; }

    /// <summary>
    /// Creates a <see cref="CachedAttribute"/> that caches under the member's or class's own type.
    /// </summary>
    public CachedAttribute()
    {
    }

    /// <summary>
    /// Creates a <see cref="CachedAttribute"/> that caches under <paramref name="cacheAs"/>.
    /// Use this to expose a concrete type as an interface to child drawables.
    /// </summary>
    /// <param name="cacheAs">The type to register the dependency under.</param>
    public CachedAttribute(Type cacheAs)
    {
        CacheAs = cacheAs;
    }
}
