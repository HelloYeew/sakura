// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Statistics;

[TestFixture]
public class GlobalStatisticMetadataTest
{
    [TearDown]
    public void TearDown()
    {
        foreach (string name in new[] { "Plain", "Heap", "Pause", "Callback", "Load", "Rate", "Frame Work", "Described", "Version", "Accumulating" })
            GlobalStatistics.Remove("Metadata Test", name);
    }

    [Test]
    public void DefaultsToAnUnlabelledGauge()
    {
        var stat = GlobalStatistics.Get<int>("Metadata Test", "Plain");

        Assert.Multiple(() =>
        {
            Assert.That(stat.Kind, Is.EqualTo(StatisticKind.Gauge));
            Assert.That(stat.Unit, Is.EqualTo(StatisticUnit.None));
        });

        stat.Value = 1234;
        Assert.That(stat.DisplayValue, Is.EqualTo("1,234"));
    }

    [Test]
    public void BytesAreScaledToSomethingReadable()
    {
        var stat = GlobalStatistics.Get<long>("Metadata Test", "Heap", StatisticKind.Gauge, StatisticUnit.Bytes);

        using (Assert.EnterMultipleScope())
        {
            stat.Value = 512;
            Assert.That(stat.DisplayValue, Is.EqualTo("512 B"));

            stat.Value = 2048;
            Assert.That(stat.DisplayValue, Is.EqualTo("2.0 KB"));

            stat.Value = 5 * 1024 * 1024;
            Assert.That(stat.DisplayValue, Is.EqualTo("5.0 MB"));

            stat.Value = 1234567890;
            Assert.That(stat.DisplayValue, Is.EqualTo("1.15 GB"));
        }
    }

    [Test]
    public void TimeAndRateUnitsAreLabelled()
    {
        using (Assert.EnterMultipleScope())
        {
            var ms = GlobalStatistics.Get<double>("Metadata Test", "Pause", StatisticKind.Gauge, StatisticUnit.Milliseconds);
            ms.Value = 12.345;
            Assert.That(ms.DisplayValue, Is.EqualTo("12.35 ms"));

            var us = GlobalStatistics.Get<long>("Metadata Test", "Callback", StatisticKind.Gauge, StatisticUnit.Microseconds);
            us.Value = 850;
            Assert.That(us.DisplayValue, Is.EqualTo("850 µs"));

            var pct = GlobalStatistics.Get<double>("Metadata Test", "Load", StatisticKind.Gauge, StatisticUnit.Percent);
            pct.Value = 3.5;
            Assert.That(pct.DisplayValue, Is.EqualTo("3.50%"));

            var hz = GlobalStatistics.Get<double>("Metadata Test", "Rate", StatisticKind.Gauge, StatisticUnit.Hertz);
            hz.Value = 60;
            Assert.That(hz.DisplayValue, Is.EqualTo("60 Hz"));
        }
    }

    /// <summary>
    /// The distinction the window could not previously show: a per-frame count and a lifetime total
    /// rendered as the same right-aligned number and meant entirely different things.
    /// </summary>
    [Test]
    public void PerFrameCountsSaySo()
    {
        var perFrame = GlobalStatistics.Get<int>("Metadata Test", "Frame Work", StatisticKind.PerFrame);
        perFrame.Accumulator = 142;
        perFrame.CompleteFrame();

        Assert.That(perFrame.DisplayValue, Is.EqualTo("142 /frame"));

        var total = GlobalStatistics.Get<int>("Metadata Test", "Plain", StatisticKind.Cumulative);
        total.Value = 142;

        Assert.That(total.DisplayValue, Is.EqualTo("142"), "a lifetime total reads as the number it is");
    }

    /// <summary>
    /// A statistic is commonly fetched from several places — every renderer backend declares
    /// "Renderer/Draw Calls" — and which runs first is not something any of them controls. So the
    /// description applies on any call, and a default never overwrites one already given.
    /// </summary>
    [Test]
    public void DescriptionSticksWhicheverCallSiteRunsFirst()
    {
        var undescribed = GlobalStatistics.Get<long>("Metadata Test", "Described");

        Assert.That(undescribed.Kind, Is.EqualTo(StatisticKind.Gauge));

        var described = GlobalStatistics.Get<long>("Metadata Test", "Described", StatisticKind.Cumulative, StatisticUnit.Bytes);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(described, Is.SameAs(undescribed), "still the same statistic");
            Assert.That(described.Kind, Is.EqualTo(StatisticKind.Cumulative), "a later call describes it");
            Assert.That(described.Unit, Is.EqualTo(StatisticUnit.Bytes));
        }

        var plain = GlobalStatistics.Get<long>("Metadata Test", "Described");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plain.Kind, Is.EqualTo(StatisticKind.Cumulative), "a bare call must not undo it");
            Assert.That(plain.Unit, Is.EqualTo(StatisticUnit.Bytes));
        }
    }

    [Test]
    public void NonNumericValuesAreLeftAlone()
    {
        var version = GlobalStatistics.Get<string>("Metadata Test", "Version", StatisticKind.Gauge, StatisticUnit.Bytes);
        version.Value = "1.2.3";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(version.NumericValue, Is.Null);
            Assert.That(version.DisplayValue, Is.EqualTo("1.2.3"), "a unit on a string must not try to scale it");
        }
    }

    /// <summary>
    /// A per-frame counter is accumulated inside the draw thread's frame and read from the update
    /// thread, so a reader taking the running total would land wherever that frame had got to and come
    /// back low by an arbitrary amount — indistinguishable on screen from a real drop. The producer
    /// accumulates somewhere of its own and publishes at its frame boundary; readers use
    /// <see cref="GlobalStatistic{T}.Value"/> as they do for every other kind.
    /// </summary>
    [Test]
    public void PerFrameCountersOnlyReportWholeFrames()
    {
        var stat = GlobalStatistics.Get<int>("Metadata Test", "Accumulating", StatisticKind.PerFrame);

        // Frame one, in progress: nothing has completed, so there is nothing to report.
        stat.Accumulator++;
        stat.Accumulator++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stat.Accumulator, Is.EqualTo(2), "the producer's running total is its own to see");
            Assert.That(stat.Value, Is.EqualTo(0), "a reader mid-frame must not see a partial count");
            Assert.That(stat.DisplayValue, Is.EqualTo("0 /frame"));
        }

        stat.Accumulator++;
        stat.CompleteFrame();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stat.Value, Is.EqualTo(3), "the completed frame's whole total");
            Assert.That(stat.Accumulator, Is.EqualTo(0), "and accumulation restarts");
            Assert.That(stat.DisplayValue, Is.EqualTo("3 /frame"));
        }

        // Part-way through the next frame, a reader still sees the last whole one.
        stat.Accumulator++;

        Assert.That(stat.Value, Is.EqualTo(3), "one frame stale beats mid-frame wrong");

        stat.CompleteFrame();
        Assert.That(stat.Value, Is.EqualTo(1));
    }

    /// <summary>
    /// Publishing is only for the kind that needs it. A gauge or a lifetime total is meaningful at
    /// every instant, so a producer writes straight to <see cref="GlobalStatistic{T}.Value"/> and
    /// never touches the accumulator.
    /// </summary>
    [Test]
    public void EveryOtherKindReportsItsValueDirectly()
    {
        var gauge = GlobalStatistics.Get<int>("Metadata Test", "Plain");
        gauge.Value = 7;

        var total = GlobalStatistics.Get<int>("Metadata Test", "Described", StatisticKind.Cumulative);
        total.Value = 9;

        Assert.Multiple(() =>
        {
            Assert.That(gauge.Value, Is.EqualTo(7));
            Assert.That(total.Value, Is.EqualTo(9));
        });
    }
}
