// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Statistic;

/// <summary>
/// Publishes garbage-collection counts and pause times to <see cref="GlobalStatistics"/>.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class GCStatistics
{
    private static readonly GlobalStatistic<int> stat_gen0 = GlobalStatistics.Get<int>("GC", "Gen 0 Collections");
    private static readonly GlobalStatistic<int> stat_gen1 = GlobalStatistics.Get<int>("GC", "Gen 1 Collections");
    private static readonly GlobalStatistic<int> stat_gen2 = GlobalStatistics.Get<int>("GC", "Gen 2 Collections");
    private static readonly GlobalStatistic<long> stat_total_pause_ms = GlobalStatistics.Get<long>("GC", "Total Pause (ms)");
    private static readonly GlobalStatistic<double> stat_last_pause_ms = GlobalStatistics.Get<double>("GC", "Last Pause (ms)");
    private static readonly GlobalStatistic<double> stat_max_pause_ms = GlobalStatistics.Get<double>("GC", "Max Pause (ms)");
    private static readonly GlobalStatistic<long> stat_heap_bytes = GlobalStatistics.Get<long>("GC", "Managed Heap (bytes)");
    private static readonly GlobalStatistic<long> stat_committed_bytes = GlobalStatistics.Get<long>("GC", "Committed (bytes)");

    private static double maxPauseMs;

    /// <summary>
    /// Refreshes the counters. Cheap, but not free (<see cref="GC.GetGCMemoryInfo()"/> copies a struct
    /// with a pause-duration buffer), so call it on a throttle rather than every frame.
    /// </summary>
    public static void Update()
    {
        stat_gen0.Value = GC.CollectionCount(0);
        stat_gen1.Value = GC.CollectionCount(1);
        stat_gen2.Value = GC.CollectionCount(2);

        stat_total_pause_ms.Value = (long)GC.GetTotalPauseDuration().TotalMilliseconds;
        stat_heap_bytes.Value = GC.GetTotalMemory(false);

        var info = GC.GetGCMemoryInfo();
        stat_committed_bytes.Value = info.TotalCommittedBytes;

        var pauses = info.PauseDurations;

        if (pauses.Length > 0)
        {
            double last = pauses[0].TotalMilliseconds;
            stat_last_pause_ms.Value = last;

            for (int i = 0; i < pauses.Length; i++)
            {
                double pause = pauses[i].TotalMilliseconds;
                if (pause > maxPauseMs)
                    maxPauseMs = pause;
            }

            stat_max_pause_ms.Value = maxPauseMs;
        }
    }

    /// <summary>
    /// Resets the running maximum, so a measurement run isn't skewed by a startup spike.
    /// </summary>
    public static void ResetPeaks()
    {
        maxPauseMs = 0;
        stat_max_pause_ms.Value = 0;
    }
}
