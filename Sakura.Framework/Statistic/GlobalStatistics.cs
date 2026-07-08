// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Sakura.Framework.Statistic;

public static class GlobalStatistics
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IGlobalStatistic>> statistics = new ConcurrentDictionary<string, ConcurrentDictionary<string, IGlobalStatistic>>();

    public static GlobalStatistic<T> Get<T>(string group, string name)
    {
        if (statistics.TryGetValue(group, out var existingGroupStats) && existingGroupStats.TryGetValue(name, out var existingStat))
            return (GlobalStatistic<T>)existingStat;

        var groupStats = statistics.GetOrAdd(group, static _ => new ConcurrentDictionary<string, IGlobalStatistic>());
        var stat = groupStats.GetOrAdd(name, static (n, g) => new GlobalStatistic<T>(g, n), group);

        return (GlobalStatistic<T>)stat;
    }

    public static void Clear()
    {
        foreach (var stat in GetStatistics())
            stat.Clear();
    }

    public static IEnumerable<IGlobalStatistic> GetStatistics()
    {
        foreach (var group in statistics.Values)
        {
            foreach (var stat in group.Values)
            {
                yield return stat;
            }
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
        }
    }
}
