// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Threading;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Tracks every live <see cref="Texture"/> in the process, for tooling and VRAM accounting.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately separate from a texture manager's <i>cache</i>. A cache answers "is a texture
/// already loaded under this key, so it can be shared" and only contains textures that were given a
/// key. A registry answers "what textures exist right now" and contains everything. Conflating the two
/// meant anything created without a cache key — cover art, generated pixel data, render targets — was
/// invisible to the texture viewer and absent from the loaded-texture count, which hid the largest
/// textures in a game from the only tool built to show them.
/// </para>
/// <para>
/// Entries are weak: registration never keeps a texture alive, so forgetting to unregister leaks
/// nothing but a small dead slot, reclaimed on the next <see cref="Prune"/>.
/// </para>
/// <para>
/// <see cref="LiveCount"/> and <see cref="LiveBytes"/> track textures that were constructed and not yet
/// disposed. A texture collected <i>without</i> being disposed is reconciled out of the counters by the
/// next <see cref="Prune"/> or enumeration, since each entry remembers the byte size it added.
/// </para>
/// </remarks>
public static class TextureRegistry
{
    private static readonly Lock mutex = new Lock();
    private static readonly List<Entry> entries = new List<Entry>();

    private static readonly GlobalStatistic<int> stat_live_count = GlobalStatistics.Get<int>("Textures", "Live Count");
    private static readonly GlobalStatistic<long> stat_live_bytes = GlobalStatistics.Get<long>("Textures", "Live Bytes");
    private static readonly GlobalStatistic<long> stat_peak_bytes = GlobalStatistics.Get<long>("Textures", "Peak Bytes");

    private static int liveCount;
    private static long liveBytes;

    /// <summary>
    /// Number of textures constructed and not yet disposed.
    /// </summary>
    public static int LiveCount
    {
        get
        {
            lock (mutex)
                return liveCount;
        }
    }

    /// <summary>
    /// Approximate GPU bytes held by live textures, assuming 32-bit RGBA. Atlas slices are excluded:
    /// they are views into a page that is itself registered, so counting both would double-count.
    /// </summary>
    public static long LiveBytes
    {
        get
        {
            lock (mutex)
                return liveBytes;
        }
    }

    /// <summary>
    /// One registered texture. Held by the <see cref="Texture"/> itself so unregistering is O(1) rather
    /// than a search, and it remembers <see cref="Bytes"/> so the counters can be reconciled for a
    /// texture that was collected without ever being disposed.
    /// </summary>
    internal sealed class Entry
    {
        public readonly WeakReference<Texture> Reference;
        public readonly long Bytes;

        /// <summary>
        /// Whether this entry's contribution has already been subtracted from the counters, either by
        /// <see cref="Unregister"/> or by reconciliation during a prune.
        /// </summary>
        public bool Released;

        public Entry(Texture texture, long bytes)
        {
            Reference = new WeakReference<Texture>(texture);
            Bytes = bytes;
        }
    }

    internal static Entry Register(Texture texture)
    {
        var entry = new Entry(texture, bytesOf(texture));

        lock (mutex)
        {
            entries.Add(entry);

            liveCount++;
            liveBytes += entry.Bytes;

            publish();
        }

        return entry;
    }

    internal static void Unregister(Entry entry)
    {
        if (entry == null)
            return;

        lock (mutex)
        {
            releaseLocked(entry);
            publish();
        }
    }

    /// <summary>
    /// A snapshot of every live texture, dropping entries whose texture has been disposed or collected.
    /// </summary>
    /// <remarks>
    /// Disposed textures are filtered by <see cref="Texture.IsDisposed"/> rather than removed from the
    /// list on dispose. That keeps <see cref="Unregister"/> O(1) — tearing down a screen can dispose
    /// hundreds of textures at once, and an O(n) removal each would make that quadratic — while the
    /// dead slots are cleaned up here and in <see cref="Prune"/>.
    /// </remarks>
    public static IReadOnlyList<Texture> GetAll()
    {
        lock (mutex)
        {
            var alive = new List<Texture>(entries.Count);
            pruneLocked(alive);
            return alive;
        }
    }

    /// <summary>
    /// Drops entries whose texture has been disposed or collected, reconciling the counters for any that
    /// were collected without being disposed. Cheap to call periodically; enumeration via
    /// <see cref="GetAll"/> prunes as a side effect anyway.
    /// </summary>
    public static void Prune()
    {
        lock (mutex)
        {
            pruneLocked(null);
            publish();
        }
    }

    /// <summary>
    /// Walks the entry list, removing dead slots and optionally collecting the live textures.
    /// </summary>
    /// <param name="collect">
    /// When non-null, receives every still-live texture. Order is unspecified.
    /// </param>
    private static void pruneLocked(List<Texture>? collect)
    {
        bool changed = false;

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];

            if (entry.Reference.TryGetTarget(out var texture))
            {
                if (!texture.IsDisposed)
                {
                    collect?.Add(texture);
                    continue;
                }
            }
            else
            {
                // Collected without ever being disposed, so Unregister never ran and the counters are
                // still carrying it. This is the only place that can notice, since there is no callback
                // for "the GC took it".
                changed |= releaseLocked(entry);
            }

            entries.RemoveAt(i);
        }

        if (changed)
            publish();
    }

    /// <summary>
    /// Forgets every entry and resets the counters.
    /// </summary>
    /// <remarks>
    /// Intended for test isolation: the registry is process-wide, so a fixture that asserts on counts
    /// needs a clean slate. Existing entries are marked released first, so a texture disposed after a
    /// reset cannot decrement counters it no longer contributes to.
    /// </remarks>
    public static void Reset()
    {
        lock (mutex)
        {
            foreach (var entry in entries)
                entry.Released = true;

            entries.Clear();
            liveCount = 0;
            liveBytes = 0;
            stat_peak_bytes.Value = 0;
            publish();
        }
    }

    /// <summary>
    /// Subtracts an entry's contribution from the counters, once.
    /// </summary>
    /// <returns>Whether this call was the one that released it.</returns>
    private static bool releaseLocked(Entry entry)
    {
        if (entry.Released)
            return false;

        entry.Released = true;

        liveCount = Math.Max(0, liveCount - 1);
        liveBytes = Math.Max(0, liveBytes - entry.Bytes);

        return true;
    }

    /// <summary>
    /// Atlas slices share their page's GPU allocation, so only whole textures are counted.
    /// </summary>
    private static long bytesOf(Texture texture)
    {
        if (texture.BackendTexture == null)
            return 0; // dimension-only proxy (e.g. the video pipeline): no GPU allocation of its own.

        var uv = texture.UvRect;
        if (uv.Width < 1f || uv.Height < 1f)
            return 0; // a slice of a page that is registered in its own right.

        return (long)texture.Width * texture.Height * 4;
    }

    private static void publish()
    {
        stat_live_count.Value = liveCount;
        stat_live_bytes.Value = liveBytes;

        if (liveBytes > stat_peak_bytes.Value)
            stat_peak_bytes.Value = liveBytes;
    }
}
