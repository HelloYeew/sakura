// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Reference-counts textures shared by a string key, so identical images e.g. the same texture shown
/// by several drawables at once, or re-loaded as a list scrolls a panel back into view will map to a single
/// GPU texture instead of one decode/upload per user. Each <see cref="TryAcquire"/>/<see cref="AddOrAcquire"/>
/// must be balanced by a <see cref="Release"/>, the underlying texture is disposed only when the last
/// reference is released.
/// </summary>
public sealed class SharedTextureStore
{
    private sealed class Entry
    {
        public readonly Texture Texture;
        public int Count;

        public Entry(Texture texture)
        {
            Texture = texture;
            Count = 1;
        }
    }

    private readonly Lock sync = new Lock();
    private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

    /// <summary>
    /// Number of distinct keys currently held
    /// </summary>
    public int Count
    {
        get
        {
            lock (sync)
                return entries.Count;
        }
    }

    /// <summary>
    /// If <paramref name="key"/> is already held, increments its reference count and returns its texture.
    /// Returns false otherwise (decode, then call <see cref="AddOrAcquire"/>).
    /// </summary>
    public bool TryAcquire(string key, out Texture texture)
    {
        lock (sync)
        {
            if (!string.IsNullOrEmpty(key) && entries.TryGetValue(key, out var entry))
            {
                entry.Count++;
                texture = entry.Texture;
                return true;
            }
        }

        texture = null!;
        return false;
    }

    /// <summary>
    /// Returns the texture for <paramref name="key"/>, creating and storing it via <paramref name="create"/>
    /// on first use (reference count 1) or incrementing the count of the existing one. Handles the race
    /// where two threads miss simultaneously: <paramref name="create"/> runs at most once per live key.
    /// </summary>
    public Texture AddOrAcquire(string key, Func<Texture> create)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("A non-empty key is required.", nameof(key));
        ArgumentNullException.ThrowIfNull(create);

        lock (sync)
        {
            if (entries.TryGetValue(key, out var existing))
            {
                existing.Count++;
                return existing.Texture;
            }

            var texture = create();
            entries[key] = new Entry(texture);
            return texture;
        }
    }

    /// <summary>
    /// Releases one reference to <paramref name="key"/>. When the last reference is released the entry is
    /// removed and <paramref name="dispose"/> is invoked (outside the lock) so the caller can free the GPU
    /// resource. No-op for an unknown or already-freed key.
    /// </summary>
    public void Release(string key, Action<Texture>? dispose)
    {
        if (string.IsNullOrEmpty(key))
            return;

        Texture? toDispose = null;

        lock (sync)
        {
            if (entries.TryGetValue(key, out var entry))
            {
                entry.Count--;
                if (entry.Count <= 0)
                {
                    entries.Remove(key);
                    toDispose = entry.Texture;
                }
            }
        }

        if (toDispose != null)
            dispose?.Invoke(toDispose);
    }
}
