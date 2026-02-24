// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;

namespace Sakura.Framework.Statistic;

public static class GlobalStatistics
{
    private static readonly Dictionary<string, Dictionary<string, IGlobalStatistic>> statistics = new();

    public static GlobalStatistic<T> Get<T>(string group, string name)
    {
        if (!statistics.TryGetValue(group, out var groupStats))
        {
            groupStats = new Dictionary<string, IGlobalStatistic>();
            statistics[group] = groupStats;
        }

        if (!groupStats.TryGetValue(name, out var stat))
        {
            stat = new GlobalStatistic<T>(group, name);
            groupStats[name] = stat;
        }

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
}
