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

        T component = createComponent(name);

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
        evictExcess(name);

        return component;
    }

    /// <summary>
    /// Creates a component for a stored name, preferring the filesystem path when the backing
    /// storage can provide one and the derived store can use it.
    /// </summary>
    /// <remarks>
    /// The stream path has to buffer the whole encoded file into memory and keep it there for the
    /// component's lifetime, because the native audio library reads from a pointer it is given. When
    /// the file is on disk, the library can open it itself and none of that memory is needed.
    /// </remarks>
    private T createComponent(string name)
    {
        string? filePath = storage.GetFileSystemPath(name);

        if (filePath != null)
        {
            T fromFile = CreateComponent(filePath);

            if (fromFile != null)
                return fromFile;
        }

        using (Stream stream = storage.GetStream(name))
            return CreateComponent(stream);
    }

    /// <summary>
    /// Evicts and disposes the least-recently-used entries until the cache is back within
    /// <see cref="MaxCachedComponents"/>. Cheap to skip entirely when unbounded (the default).
    /// </summary>
    /// <remarks>
    /// Entries reporting live channels through <see cref="IHasActiveChannels"/> are skipped rather
    /// than evicted: least-recently-*requested* is not the same as least-recently-*used*, and the
    /// track that is currently playing is exactly the one nothing has asked the store for lately.
    /// Disposing it would tear down decoder state underneath a live channel. When everything
    /// evictable has been evicted the cache is left temporarily over its cap, which is the right
    /// trade — the cap is a memory target, not a correctness boundary.
    /// </remarks>
    /// <param name="justAddedKey">
    /// The entry this eviction pass was triggered by, which is never evicted. Without this, a cache
    /// full of in-use components would evict the only evictable entry — the one just created — and
    /// hand the caller back an already-disposed component.
    /// </param>
    private void evictExcess(string justAddedKey)
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
                    if (kvp.Key == justAddedKey)
                        continue;

                    if (kvp.Value.Component is IHasActiveChannels { HasActiveChannels: true })
                        continue;

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

    /// <summary>
    /// Creates a component directly from a file on disk, avoiding a memory-resident copy of the
    /// encoded data. Return null to decline, in which case the stream path is used instead.
    /// </summary>
    /// <param name="filePath">An absolute path to an existing file.</param>
    /// <returns>The loaded component, or null if this store does not support loading from a path.</returns>
    protected virtual T? CreateComponent(string filePath) => null;

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
