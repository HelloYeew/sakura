// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using Sakura.Framework.Graphics.Colors;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Statistic;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public partial class TestGlobalStatisticsDisplay : TestScene
{
    private DebugWindowLayer layer = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add the window layer", () =>
        {
            Clear();
            Add(layer = new DebugWindowLayer { Depth = float.MaxValue - 20 });
        });
    }

    [Test]
    public void TestDisplay()
    {
        AddStep("Open", () => layer.Toggle(() => new GlobalStatisticsDisplay()));
        AddWaitStep("Let it populate", 200);

        AddAssert("Window is open", () => layer.IsOpen<GlobalStatisticsDisplay>());
        AddAssert("Rows were built", () => countTextRows(window()) > 0);
    }

    [Test]
    public void TestPicksUpNewStatistics()
    {
        AddStep("Open", () => layer.Toggle(() => new GlobalStatisticsDisplay()));
        AddWaitStep("Let it populate", 200);

        int before = 0;

        AddStep("Register a statistic", () =>
        {
            before = countTextRows(window());
            GlobalStatistics.Get<int>("DW2 Test", "A Number").Value = 42;
        });

        AddWaitStep("Let the refresh tick", 300);

        AddAssert("The new group was added", () => countTextRows(window()) > before);

        AddStep("Clean up", () => GlobalStatistics.Remove("DW2 Test", "A Number"));
    }

    [Test]
    public void TestCloseDetaches()
    {
        AddStep("Open", () => layer.Toggle(() => new GlobalStatisticsDisplay()));
        AddWaitStep("Let it populate", 200);

        AddStep("Close", () => layer.Toggle(() => new GlobalStatisticsDisplay()));

        AddUntilStep("Nothing left in the layer once the close animation ends", () => layer.Children.Count == 0);

        AddStep("Reopen", () => layer.Toggle(() => new GlobalStatisticsDisplay()));

        AddAssert("Rows survived the close", () => countTextRows(window()) > 0);
    }

    /// <summary>
    /// Ninety-odd statistics across fourteen groups is not something you read, so the window has a
    /// filter. It matches a statistic's name or its group, so a group name brings everything under it.
    /// </summary>
    [Test]
    public void TestSearchFiltersRows()
    {
        AddStep("Register two statistics in their own group", () =>
        {
            GlobalStatistics.Get<int>("DW2 Filter", "Needle Count").Value = 1;
            GlobalStatistics.Get<int>("DW2 Filter", "Something Else").Value = 2;
        });

        AddStep("Open", () => layer.Toggle(() => new GlobalStatisticsDisplay()));
        AddWaitStep("Let it populate", 200);

        AddAssert("Both are showing", () => shownRowNames().Contains("Needle Count") && shownRowNames().Contains("Something Else"));

        AddStep("Search by name", () => window().SearchText.Value = "needle");

        AddUntilStep("Only the match survives", () =>
        {
            var shown = shownRowNames();
            return shown.Contains("Needle Count") && !shown.Contains("Something Else");
        });

        AddStep("Search by group instead", () => window().SearchText.Value = "dw2 filter");

        AddUntilStep("A group name brings everything under it", () =>
        {
            var shown = shownRowNames();
            return shown.Contains("Needle Count") && shown.Contains("Something Else");
        });

        AddStep("Search for nothing", () => window().SearchText.Value = "no statistic is called this");

        AddUntilStep("Nothing is showing", () => shownRowNames().Count == 0);

        AddStep("Clear the search", () => window().SearchText.Value = string.Empty);

        AddUntilStep("Everything is back", () => shownRowNames().Contains("Something Else"));

        AddStep("Clean up", () =>
        {
            GlobalStatistics.Remove("DW2 Filter", "Needle Count");
            GlobalStatistics.Remove("DW2 Filter", "Something Else");
        });
    }

    /// <summary>
    /// Rows were already put back into alphabetical order when one registered late; groups were not,
    /// so a group whose first statistic arrived later landed at the end and the window's layout
    /// depended on what you happened to do first.
    /// </summary>
    [Test]
    public void TestALateGroupIsPutInOrder()
    {
        AddStep("Open", () => layer.Toggle(() => new GlobalStatisticsDisplay()));
        AddWaitStep("Let it populate", 200);

        // "AAA" sorts before every group the framework registers, so if late groups were appended it
        // would land last rather than first.
        AddStep("Register a group that sorts first", () => GlobalStatistics.Get<int>("AAA Late Group", "Value").Value = 1);

        AddWaitStep("Let the refresh tick", 300);

        AddAssert("It went to the front, not the end", () => shownGroupNames().FirstOrDefault() == "AAA Late Group");
        AddAssert("Groups are in order", () => shownGroupNames().SequenceEqual(shownGroupNames().OrderBy(g => g, System.StringComparer.Ordinal)));

        AddStep("Clean up", () => GlobalStatistics.Remove("AAA Late Group", "Value"));
    }

    /// <summary>
    /// A cumulative counter is only readable against a baseline — mark, do the thing, read what
    /// accumulated. The baseline is a snapshot the window keeps, never a reset of the statistic: these
    /// are global and shared, and a viewer zeroing one would corrupt every other reader.
    /// </summary>
    [Test]
    public void TestBaselineReportsWhatAccumulated()
    {
        GlobalStatistic<int> counter = null!;

        AddStep("Register a counter with a value", () =>
        {
            counter = GlobalStatistics.Get<int>("DW2 Baseline", "Accumulated");
            counter.Value = 1000;
        });

        AddStep("Open", () => layer.Toggle(() => new GlobalStatisticsDisplay()));
        AddWaitStep("Let it populate", 200);

        AddAssert("The value is showing plainly", () => valueTextFor("Accumulated")?.Text == "1,000");

        AddStep("Mark the baseline", () => window().ToggleBaseline());
        AddWaitStep("Let the refresh tick", 300);

        AddAssert("Nothing has moved, so no delta is shown", () => deltaTextFor("Accumulated")?.Alpha == 0);

        AddStep("Accumulate some more", () => counter.Value = 1056);
        AddWaitStep("Let the refresh tick", 300);

        AddAssert("The value itself is untouched", () => counter.Value == 1056 && valueTextFor("Accumulated")?.Text == "1,056");

        AddAssert("The delta sits alongside it, in its own colour", () =>
            deltaTextFor("Accumulated") is { } delta
            && delta.Text == "+56"
            && delta.Alpha == 1
            && delta.Color != valueTextFor("Accumulated")!.Color
            && delta.X < 0);

        var roseColor = default(Color);

        AddStep("Remember the colour of a rise", () => roseColor = deltaTextFor("Accumulated")!.Color);

        AddStep("Fall back below the baseline", () => counter.Value = 900);
        AddWaitStep("Let the refresh tick", 300);

        AddAssert("A fall reads as one, and not in the colour of a rise", () =>
            deltaTextFor("Accumulated") is { } delta
            && delta.Text == "-100"
            && delta.Color != roseColor);

        AddStep("Back above it", () => counter.Value = 1056);
        AddWaitStep("Let the refresh tick", 300);

        AddStep("Drop the baseline", () => window().ToggleBaseline());
        AddWaitStep("Let the refresh tick", 300);

        AddAssert("Back to a plain value", () => valueTextFor("Accumulated")?.Text == "1,056" && deltaTextFor("Accumulated")?.Alpha == 0);

        AddStep("Clean up", () => GlobalStatistics.Remove("DW2 Baseline", "Accumulated"));
    }

    /// <summary>
    /// The reason this is a snapshot and not a reset. Host's version strings are written once during
    /// <c>App.Load</c> and never again, so anything that zeroed the registry would leave them reading
    /// "null" until the app restarted.
    /// </summary>
    [Test]
    public void TestBaselineLeavesWriteOnceStatisticsAlone()
    {
        GlobalStatistic<string> version = null!;

        AddStep("Register a write-once statistic", () =>
        {
            version = GlobalStatistics.Get<string>("DW2 Identity", "Version");
            version.Value = "1.2.3";
        });

        AddStep("Open", () => layer.Toggle(() => new GlobalStatisticsDisplay()));
        AddWaitStep("Let it populate", 200);

        AddStep("Mark the baseline", () => window().ToggleBaseline());
        AddWaitStep("Let the refresh tick", 300);

        AddAssert("It still reads its version", () => version.Value == "1.2.3" && valueTextFor("Version")?.Text == "1.2.3");
        AddAssert("A non-numeric statistic gets no delta", () => version.NumericValue == null && deltaTextFor("Version")?.Alpha == 0);

        AddStep("Clean up", () => GlobalStatistics.Remove("DW2 Identity", "Version"));
    }

    /// <summary>
    /// A value that just moved is lit and decays back to grey; one that keeps moving never finishes
    /// decaying and stays lit. That is what makes "which of these is actually live" the first thing
    /// visible in a wall of identical numbers.
    /// </summary>
    [Test]
    public void TestChangedValuesAreLit()
    {
        GlobalStatistic<int> counter = null!;

        AddStep("Register a still counter", () =>
        {
            counter = GlobalStatistics.Get<int>("DW2 Live", "Ticks");
            counter.Value = 0;
        });

        AddStep("Open", () => layer.Toggle(() => new GlobalStatisticsDisplay()));
        AddWaitStep("Let it populate", 200);

        AddStep("Move it", () => counter.Value = 99);

        // Brightness rather than an exact colour: Color.White is a named colour and compares unequal
        // to the identical value the decay lerp builds, which says nothing about what is on screen.
        AddUntilStep("Its value lights up", () => valueTextFor("Ticks")?.Color.R == 255);

        AddWaitStep("Leave it alone", 1500);

        AddAssert("It decays back to grey", () => valueTextFor("Ticks") is { } text && text.Color.R < 255);

        AddStep("Clean up", () => GlobalStatistics.Remove("DW2 Live", "Ticks"));
    }

    /// <summary>
    /// A per-frame counter is zeroed and re-accumulated every frame, so the difference between two
    /// samples of it is noise about which frame each was taken in, not something that accumulated. The
    /// baseline stays out of its way; a gauge does get one, because "the heap grew 40 MB since I
    /// marked" is exactly what a baseline is for.
    /// </summary>
    [Test]
    public void TestBaselineSkipsPerFrameCounters()
    {
        GlobalStatistic<int> perFrame = null!;
        GlobalStatistic<int> gauge = null!;

        AddStep("Register one of each", () =>
        {
            perFrame = GlobalStatistics.Get<int>("DW2 Kinds", "Per Frame Work", StatisticKind.PerFrame);
            perFrame.Accumulator = 10;
            perFrame.CompleteFrame();

            gauge = GlobalStatistics.Get<int>("DW2 Kinds", "A Reading");
            gauge.Value = 10;
        });

        AddStep("Open", () => layer.Toggle(() => new GlobalStatisticsDisplay()));
        AddWaitStep("Let it populate", 200);

        AddAssert("The per-frame one says which frame it means", () => valueTextFor("Per Frame Work")?.Text == "10 /frame");

        AddStep("Mark the baseline", () => window().ToggleBaseline());
        AddWaitStep("Let the refresh tick", 300);

        AddStep("Move them both", () =>
        {
            perFrame.Accumulator = 40;
            perFrame.CompleteFrame();
            gauge.Value = 40;
        });

        AddWaitStep("Let the refresh tick", 300);

        AddAssert("The gauge reports what it grew by", () => deltaTextFor("A Reading")?.Text == "+30");
        AddAssert("The per-frame counter reports nothing", () => deltaTextFor("Per Frame Work")?.Alpha == 0);

        AddStep("Clean up", () =>
        {
            GlobalStatistics.Remove("DW2 Kinds", "Per Frame Work");
            GlobalStatistics.Remove("DW2 Kinds", "A Reading");
        });
    }

    private GlobalStatisticsDisplay window() => layer.OpenWindows.OfType<GlobalStatisticsDisplay>().Single();

    /// <summary>
    /// The names of the group headings currently in the window, in display order. A heading is the
    /// bold size-20 text at the top of each group flow.
    /// </summary>
    private System.Collections.Generic.List<string> shownGroupNames() =>
        collect<SpriteText>(window()).Where(t => t.Font.Size > 16).Select(t => t.Text).ToList();

    /// <summary>
    /// The statistic names currently in the window. Rows are name-left/value-right pairs inside their
    /// own container, so the name is the first text of a two-text row.
    /// </summary>
    private System.Collections.Generic.List<string> shownRowNames() =>
        statRows().Select(r => r.Name).ToList();

    private SpriteText? valueTextFor(string statName) =>
        statRows().Where(r => r.Name == statName).Select(r => r.Value).FirstOrDefault();

    private SpriteText? deltaTextFor(string statName) =>
        statRows().Where(r => r.Name == statName).Select(r => r.Delta).FirstOrDefault();

    private System.Collections.Generic.List<(string Name, SpriteText Value, SpriteText Delta)> statRows()
    {
        var rows = new System.Collections.Generic.List<(string, SpriteText, SpriteText)>();

        foreach (var container in collect<Container>(window()))
        {
            var texts = container.Children.OfType<SpriteText>().ToList();

            // A row is its name, its value and its delta, in that order. Nothing in the window's
            // chrome has that shape.
            if (texts.Count == 3 && texts[0].Anchor == Anchor.TopLeft && texts[1].Anchor == Anchor.TopRight && texts[2].Anchor == Anchor.TopRight)
                rows.Add((texts[0].Text, texts[1], texts[2]));
        }

        return rows;
    }

    private static System.Collections.Generic.List<T> collect<T>(Drawable drawable) where T : Drawable
    {
        var found = new System.Collections.Generic.List<T>();

        void walk(Drawable d)
        {
            if (d is T match)
                found.Add(match);

            if (d is Container container)
            {
                foreach (var child in container.Children)
                    walk(child);
            }
        }

        walk(drawable);
        return found;
    }

    private static int countTextRows(Drawable drawable)
    {
        int count = drawable is SpriteText ? 1 : 0;

        if (drawable is Container container)
        {
            foreach (var child in container.Children)
                count += countTextRows(child);
        }

        return count;
    }
}
