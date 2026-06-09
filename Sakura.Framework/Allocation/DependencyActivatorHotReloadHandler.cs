// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(Sakura.Framework.Allocation.DependencyActivatorHotReloadHandler))]

namespace Sakura.Framework.Allocation;

/// <summary>
/// Clears the <see cref="DependencyActivator"/> cache whenever the .NET hot reload system
/// finishes applying a compilation update. This ensures stale source-generated or
/// reflection-cached delegates do not survive code changes during a live development session.
/// </summary>
internal static class DependencyActivatorHotReloadHandler
{
    /// <summary>
    /// Called by the hot reload infrastructure before applying metadata updates.
    /// </summary>
    public static void ClearCache(Type[]? _) => DependencyActivator.ClearCache();

    /// <summary>
    /// Called by the hot reload infrastructure after applying metadata updates.
    /// </summary>
    public static void UpdateApplication(Type[]? _) => DependencyActivator.ClearCache();
}
