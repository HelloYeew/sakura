// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Sakura.Framework.Configurations;

/// <summary>
/// Every config manager there is, an app's own as well as the framework's, so they can all be saved at once where the
/// process may be ended without a word: an app gone to the background on a phone.
/// </summary>
internal static class ConfigManagers
{
    private static readonly List<WeakReference<IFlushableConfig>> managers = new List<WeakReference<IFlushableConfig>>();
    private static readonly Lock sync = new Lock();

    public static void Add(IFlushableConfig manager)
    {
        lock (sync)
        {
            managers.RemoveAll(reference => !reference.TryGetTarget(out _));
            managers.Add(new WeakReference<IFlushableConfig>(manager));
        }
    }

    /// <summary>
    /// Writes what each one has waiting, now.
    /// </summary>
    public static void FlushAll()
    {
        var live = new List<IFlushableConfig>();

        lock (sync)
        {
            foreach (var reference in managers)
            {
                if (reference.TryGetTarget(out var manager))
                    live.Add(manager);
            }
        }

        // Outside the lock: a flush waits on a save, which a new manager being made mustn't wait behind.
        foreach (var manager in live)
            manager.Flush();
    }
}

internal interface IFlushableConfig
{
    void Flush();
}
