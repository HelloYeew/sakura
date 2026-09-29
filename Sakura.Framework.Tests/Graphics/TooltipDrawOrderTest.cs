// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// A tooltip is only useful if it is drawn over the content it describes. Children sort ascending by
/// <see cref="Drawable.Depth"/> into back-to-front order, so "in front" means the highest depth —
/// the opposite of what the name suggests to anyone expecting a z-index.
/// </summary>
[TestFixture]
public class TooltipDrawOrderTest
{
    private ManualClock manual = null!;
    private Container root = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };
    }

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    [Test]
    public void TestHigherDepthSortsToTheFront()
    {
        var back = new Box
        {
            Size = new Vector2(10),
            Depth = 0
        };
        var front = new Box
        {
            Size = new Vector2(10),
            Depth = 10
        };

        root.Add(front);
        root.Add(back);
        root.Load();
        root.CompleteLoad();
        frame();

        var sorted = root.SortedChildren;

        Assert.That(sorted.Last(), Is.SameAs(front), "SortedChildren is back-to-front, so the last entry is drawn on top.");
        Assert.That(sorted.First(), Is.SameAs(back));
    }

    [Test]
    public void TestCursorAndTooltipUseTheSameConvention()
    {
        // Regression guard: these two both float above everything, and disagreeing about which
        // extreme means "front" is exactly how the tooltip ended up behind the UI.
        var cursor = new CursorContainer();
        var tooltip = new BasicTooltip();

        Assert.That(tooltip.Depth, Is.EqualTo(cursor.Depth),
            "Both float above content, so both must sit at the same end of the depth range.");
    }
}
