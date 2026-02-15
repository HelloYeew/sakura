// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(Sakura.Framework.Testing.HotReloadManager))]

namespace Sakura.Framework.Testing;

public static class HotReloadManager
{
    /// <summary>
    /// Fired whenever .NET successfully hot-reloads the assembly.
    /// </summary>
    public static event Action OnHotReload = delegate { };

    /// <summary>
    /// Fired before .NET hot-reloads the assembly,
    /// allowing you to clear any cached data or references to the old types.
    /// </summary>
    /// <param name="updatedTypes"></param>
    public static void ClearCache(Type[]? updatedTypes)
    {
    }

    /// <summary>
    /// Fired after .NET successfully hot-reloads the assembly,
    /// allowing you to re-initialize any data or references to the new types.
    /// </summary>
    /// <param name="updatedTypes"></param>
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        OnHotReload.Invoke();
    }
}
