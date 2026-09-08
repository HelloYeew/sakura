// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
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

        AddAssert("Nothing left in the layer", () => layer.Children.Count == 0);

        AddStep("Reopen", () => layer.Toggle(() => new GlobalStatisticsDisplay()));

        AddAssert("Rows survived the close", () => countTextRows(window()) > 0);
    }

    private GlobalStatisticsDisplay window() => layer.OpenWindows.OfType<GlobalStatisticsDisplay>().Single();

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
