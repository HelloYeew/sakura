// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Sakura.Framework.Statistic;

public static class GlobalStatistics
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IGlobalStatistic>> statistics = new ConcurrentDictionary<string, ConcurrentDictionary<string, IGlobalStatistic>>();

    /// <summary>
    /// Every statistic, ordered by group and then by name, cached until the set of statistics changes.
    /// </summary>
    private static IGlobalStatistic[] ordered = Array.Empty<IGlobalStatistic>();

    private static volatile bool orderedStale = true;

    private static readonly object ordered_lock = new object();

    public static GlobalStatistic<T> Get<T>(string group, string name)
    {
        if (statistics.TryGetValue(group, out var existingGroupStats) && existingGroupStats.TryGetValue(name, out var existingStat))
            return (GlobalStatistic<T>)existingStat;

        var groupStats = statistics.GetOrAdd(group, static _ => new ConcurrentDictionary<string, IGlobalStatistic>());
        var stat = groupStats.GetOrAdd(name, static (n, g) => new GlobalStatistic<T>(g, n), group);

        // Only reached when the fast path missed, so this runs once per statistic rather than per lookup.
        // Over-invalidating on a race just rebuilds once more, which is harmless.
        orderedStale = true;

        return (GlobalStatistic<T>)stat;
    }

    public static void Clear()
    {
        foreach (var stat in GetStatistics())
            stat.Clear();
    }

    /// <summary>
    /// Every registered statistic, ordered by group and then by name.
    /// </summary>
    public static ReadOnlySpan<IGlobalStatistic> GetStatistics()
    {
        if (orderedStale)
            rebuildOrdered();

        return ordered;
    }

    private static void rebuildOrdered()
    {
        lock (ordered_lock)
        {
            if (!orderedStale)
                return;

            var all = new List<IGlobalStatistic>();

            foreach (var group in statistics)
            {
                foreach (var stat in group.Value.Values)
                    all.Add(stat);
            }

            all.Sort(static (a, b) =>
            {
                int byGroup = string.CompareOrdinal(a.Group, b.Group);
                return byGroup != 0 ? byGroup : string.CompareOrdinal(a.Name, b.Name);
            });

            ordered = all.ToArray();

            orderedStale = false;
        }
    }

    public static void Remove(IGlobalStatistic statistic)
    {
        if (statistic == null)
            return;
        Remove(statistic.Group, statistic.Name);
    }

    public static void Remove(string group, string name)
    {
        if (statistics.TryGetValue(group, out var groupStats))
        {
            groupStats.TryRemove(name, out _);

            if (groupStats.IsEmpty)
            {
                statistics.TryRemove(group, out _);
            }

            orderedStale = true;
        }
    }
}
