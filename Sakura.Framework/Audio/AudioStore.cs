// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Concurrent;
using System.IO;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Audio;

/// <summary>
/// Base class for a store that retrieves and caches audio component from a <see cref="Storage"/>
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class AudioStore<T> : IAudioStore<T> where T : class
{
    private readonly Storage storage;
    private readonly IAudioManager audioManager;
    private readonly ConcurrentDictionary<string, T> cache = new ConcurrentDictionary<string, T>();

    protected AudioStore(Storage storage, IAudioManager audioManager)
    {
        this.storage = storage;
        this.audioManager = audioManager;
    }

    public T Get(string name)
    {
        if (cache.TryGetValue(name, out var cached))
            return cached;

        if (!storage.Exists(name))
            return null;

        using (Stream stream = storage.GetStream(name))
        {
            var component = CreateComponent(stream);
            if (component != null)
            {
                cache.TryAdd(name, component);
                GlobalStatistics.Get<int>("Audio", $"Cached {typeof(T).Name}s").Value = cache.Count;
                return component;
            }
        }

        return null;
    }

    /// <summary>
    /// Abstract method for derived classes to create a specific component type (track or sample)
    /// </summary>
    /// <param name="stream">Data stream</param>
    /// <returns>The loaded component</returns>
    protected abstract T CreateComponent(Stream stream);
}
