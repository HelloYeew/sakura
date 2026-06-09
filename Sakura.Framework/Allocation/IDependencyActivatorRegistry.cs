// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Allocation;

/// <summary>
/// Delegate that injects resolved dependencies into a target object.
/// Called by the source-generated fast path instead of reflection.
/// </summary>
/// <param name="target">The object to inject into.</param>
/// <param name="dependencies">The dependency container to resolve from.</param>
public delegate void InjectDependenciesDelegate(object target, IReadOnlyDependencyContainer dependencies);

/// <summary>
/// Delegate that builds a child <see cref="IReadOnlyDependencyContainer"/> for a target object,
/// caching any <see cref="CachedAttribute"/>-decorated members into it.
/// </summary>
/// <param name="target">The object whose [Cached] members will be registered.</param>
/// <param name="parent">The parent container to inherit from.</param>
/// <returns>A new container that wraps <paramref name="parent"/> and contains the cached dependencies.</returns>
public delegate IReadOnlyDependencyContainer CacheDependenciesDelegate(object target, IReadOnlyDependencyContainer? parent);

/// <summary>
/// Receives per-type inject and cache delegate registrations from <see cref="ISourceGeneratedDependencyActivator.RegisterForDependencyActivation"/>.
/// Implemented internally by <see cref="DependencyActivator"/>.
/// </summary>
public interface IDependencyActivatorRegistry
{
    /// <summary>
    /// Returns true if delegates for <paramref name="type"/> have already been registered,
    /// preventing double-registration when the virtual chain is walked.
    /// </summary>
    bool IsRegistered(Type type);

    /// <summary>
    /// Registers the inject and/or cache delegates for <paramref name="type"/>.
    /// Either delegate may be null if the type has no [Resolved]/[BackgroundDependencyLoader] members
    /// or no [Cached] members respectively.
    /// </summary>
    void Register(Type type, InjectDependenciesDelegate? inject, CacheDependenciesDelegate? cache);
}
