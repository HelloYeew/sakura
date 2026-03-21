// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Sakura.Framework.Statistic;

public static class GlobalStatistics
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IGlobalStatistic>> statistics = new();

    public static GlobalStatistic<T> Get<T>(string group, string name)
    {
        var groupStats = statistics.GetOrAdd(group, _ => new ConcurrentDictionary<string, IGlobalStatistic>());
        var stat = groupStats.GetOrAdd(name, _ => new GlobalStatistic<T>(group, name));

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
