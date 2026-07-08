// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Audio;

/// <summary>
/// Base class for a store that retrieves and caches audio component from a <see cref="Storage"/>
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class AudioStore<T> : IAudioStore<T>, IDisposable where T : class
{
    private readonly Storage storage;
    private readonly IAudioManager audioManager;
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new ConcurrentDictionary<string, CacheEntry>();
    private readonly Lock evictionLock = new Lock();

    private long accessCounter;

    /// <summary>
    /// Maximum number of decoded components to keep resident at once. Once exceeded, the least
    /// recently used entry is evicted and disposed (if <typeparamref name="T"/> implements
    /// <see cref="IDisposable"/>).
    /// Defaults to unbounded so existing stores (e.g. sample stores, whose callers hold onto
    /// <c>Get()</c> results indefinitely and expect them to always stay valid) keep their current
    /// "cache forever" behaviour unless a derived store opts in.
    /// </summary>
    protected virtual int MaxCachedComponents => int.MaxValue;

    protected AudioStore(Storage storage, IAudioManager audioManager)
    {
        this.storage = storage;
        this.audioManager = audioManager;
    }

    public T Get(string name)
    {
        if (cache.TryGetValue(name, out var cached))
        {
            Interlocked.Exchange(ref cached.LastAccess, Interlocked.Increment(ref accessCounter));
            return cached.Component;
        }

        if (!storage.Exists(name))
            return null;

        T component;
        using (Stream stream = storage.GetStream(name))
            component = CreateComponent(stream);

        if (component == null)
            return null;

        var entry = new CacheEntry(component, Interlocked.Increment(ref accessCounter));

        if (!cache.TryAdd(name, entry))
        {
            // Lost a race with a concurrent Get() for the same name so just dispose our redundant
            // copy, and hand back whichever instance actually made it into the cache.
            if (component is IDisposable redundant)
                redundant.Dispose();

            return cache.TryGetValue(name, out var existing) ? existing.Component : null;
        }

        GlobalStatistics.Get<int>("Audio", $"Cached {typeof(T).Name}s").Value = cache.Count;
        evictExcess();

        return component;
    }

    /// <summary>
    /// Evicts and disposes the least-recently-used entries until the cache is back within
    /// <see cref="MaxCachedComponents"/>. Cheap to skip entirely when unbounded (the default).
    /// </summary>
    private void evictExcess()
    {
        if (MaxCachedComponents == int.MaxValue || cache.Count <= MaxCachedComponents)
            return;

        lock (evictionLock)
        {
            while (cache.Count > MaxCachedComponents)
            {
                string? oldestKey = null;
                long oldestAccess = long.MaxValue;

                foreach (var kvp in cache)
                {
                    long lastAccess = Interlocked.Read(ref kvp.Value.LastAccess);
                    if (lastAccess < oldestAccess)
                    {
                        oldestAccess = lastAccess;
                        oldestKey = kvp.Key;
                    }
                }

                if (oldestKey == null)
                    break;

                if (cache.TryRemove(oldestKey, out var removed) && removed.Component is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        GlobalStatistics.Get<int>("Audio", $"Cached {typeof(T).Name}s").Value = cache.Count;
    }

    /// <summary>
    /// Abstract method for derived classes to create a specific component type (track or sample)
    /// </summary>
    /// <param name="stream">Data stream</param>
    /// <returns>The loaded component</returns>
    protected abstract T CreateComponent(Stream stream);

    public void Dispose()
    {
        foreach (var entry in cache.Values)
        {
            if (entry.Component is IDisposable disposable)
                disposable.Dispose();
        }

        cache.Clear();
    }

    private sealed class CacheEntry
    {
        public readonly T Component;
        public long LastAccess;

        public CacheEntry(T component, long lastAccess)
        {
            Component = component;
            LastAccess = lastAccess;
        }
    }
}
