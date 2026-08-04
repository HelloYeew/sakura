// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Statistics;

[TestFixture]
public class GlobalStatisticsOrderingTest
{
    private readonly List<IGlobalStatistic> registered = new List<IGlobalStatistic>();

    private const string group_a = "ZZTestGroupA";
    private const string group_b = "ZZTestGroupB";

    private GlobalStatistic<int> register(string group, string name)
    {
        var stat = GlobalStatistics.Get<int>(group, name);
        registered.Add(stat);
        return stat;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var stat in registered)
            GlobalStatistics.Remove(stat);

        registered.Clear();
    }

    [Test]
    public void NamesAreOrderedWithinAGroup()
    {
        register(group_a, "Zebra");
        register(group_a, "Apple");
        register(group_a, "Mango");
        register(group_a, "Cherry");
        register(group_a, "Banana");
        register(group_a, "Durian");

        Assert.That(namesIn(group_a), Is.EqualTo(new[] { "Apple", "Banana", "Cherry", "Durian", "Mango", "Zebra" }));
    }

    [Test]
    public void GroupsAreOrderedToo()
    {
        register(group_b, "Only");
        register(group_a, "Only");

        var groups = new List<string>();

        foreach (var stat in GlobalStatistics.GetStatistics())
        {
            if ((stat.Group == group_a || stat.Group == group_b) && !groups.Contains(stat.Group))
                groups.Add(stat.Group);
        }

        Assert.That(groups, Is.EqualTo(new[] { group_a, group_b }));
    }

    /// <summary>
    /// The case the overlay actually hits: a statistic registers lazily, long after its group first appeared,
    /// because the class holding it was only just touched. It has to land in its alphabetical place.
    /// </summary>
    [Test]
    public void AStatisticRegisteredLaterIsStillOrdered()
    {
        register(group_a, "Apple");
        register(group_a, "Banana");
        register(group_a, "Cherry");
        register(group_a, "Zebra");

        Assert.That(namesIn(group_a), Is.EqualTo(new[] { "Apple", "Banana", "Cherry", "Zebra" }));

        register(group_a, "Durian");
        register(group_a, "Mango");

        Assert.That(namesIn(group_a), Is.EqualTo(new[] { "Apple", "Banana", "Cherry", "Durian", "Mango", "Zebra" }));
    }

    [Test]
    public void RemovingAStatisticKeepsTheRestOrdered()
    {
        register(group_a, "Apple");
        register(group_a, "Banana");
        var mango = register(group_a, "Mango");
        register(group_a, "Cherry");
        register(group_a, "Durian");
        register(group_a, "Zebra");

        GlobalStatistics.Remove(mango);
        registered.Remove(mango);

        Assert.That(namesIn(group_a), Is.EqualTo(new[] { "Apple", "Banana", "Cherry", "Durian", "Zebra" }));
    }

    /// <summary>
    /// The whole point of caching the order: reading it must not allocate, because the overlay reads it every
    /// frame. The previous implementation walked <c>ConcurrentDictionary.Values</c> per group per read, which
    /// builds a snapshot collection each time — a 7-minute profile attributed over 15 MB to that.
    /// </summary>
    [Test]
    public void RepeatedReadsDoNotAllocate()
    {
        register(group_a, "Apple");
        register(group_a, "Zebra");

        int seen = 0;
        long allocated = -1;

        // The registry is process-wide, so any other statistic registering — another fixture, a static
        // initializer on another thread — invalidates the cache and costs this loop one rebuild. That is
        // correct behavior, not the regression being guarded against, so a clean run is retried for rather
        // than the assertion being loosened: a read that allocated *per call* could never produce a zero.
        for (int attempt = 0; attempt < 5 && allocated != 0; attempt++)
        {
            // Prime, so the measured loop is the steady state rather than the rebuild.
            foreach (var stat in GlobalStatistics.GetStatistics())
                seen += stat.Name.Length;

            long before = GC.GetTotalAllocatedBytes(precise: true);

            for (int i = 0; i < 1000; i++)
            {
                foreach (var stat in GlobalStatistics.GetStatistics())
                    seen += stat.Name.Length;
            }

            allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        }

        Assert.That(seen, Is.GreaterThan(0));
        Assert.That(allocated, Is.Zero, $"1000 reads allocated {allocated} bytes even after retries");
    }

    private static List<string> namesIn(string group)
    {
        var names = new List<string>();

        foreach (var stat in GlobalStatistics.GetStatistics())
        {
            if (stat.Group == group)
                names.Add(stat.Name);
        }

        return names;
    }
}
