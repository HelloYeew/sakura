// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Nested auto-sizing containers must resolve in one frame; updating before children makes them settle
/// one level per frame.
/// </summary>
[TestFixture]
public class NestedAutoSizeTest
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

    /// <summary>
    /// Builds depth levels of auto-sizing containers around a fixed 50x20 box.
    /// </summary>
    private static Container nest(int depth, out Drawable leaf)
    {
        Drawable current = leaf = new Box
        {
            Size = new Vector2(50, 20)
        };

        for (int i = 0; i < depth; i++)
            current = new Container
            {
                AutoSizeAxes = Axes.Both,
                Child = current
            };

        return (Container)current;
    }

    [Test]
    public void TestSingleLevelResolvesImmediately()
    {
        var outer = nest(1, out _);
        root.Add(outer);
        root.Load();
        root.CompleteLoad();

        frame();

        Assert.That(outer.DrawSize, Is.EqualTo(new Vector2(50, 20)));
    }

    [Test]
    public void TestDeepChainResolvesInOneFrame([Values(2, 3, 5, 8)] int depth)
    {
        var outer = nest(depth, out _);
        root.Add(outer);
        root.Load();
        root.CompleteLoad();

        frame();

        Assert.That(outer.DrawSize, Is.EqualTo(new Vector2(50, 20)),
            $"A {depth}-deep auto-size chain must resolve in the first frame, not over {depth} of them.");
    }

    [Test]
    public void TestAutoSizeTracksALaterContentChange()
    {
        var outer = nest(4, out Drawable leaf);
        root.Add(outer);
        root.Load();
        root.CompleteLoad();

        frame();
        Assert.That(outer.DrawSize, Is.EqualTo(new Vector2(50, 20)));

        leaf.Size = new Vector2(120, 45);
        frame();

        Assert.That(outer.DrawSize, Is.EqualTo(new Vector2(120, 45)),
            "A resize deep inside must propagate out in the same frame.");
    }

    [Test]
    public void TestRelativeSizedChildIsStillIgnored()
    {
        // The guard against the circular case must survive the second pass: an auto-sizing parent
        // cannot take its size from a child that is sized relative to it.
        var outer = new Container
        {
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both },
                new Box { Size = new Vector2(70, 30) },
            },
        };

        root.Add(outer);
        root.Load();
        root.CompleteLoad();

        frame();
        frame();

        Assert.That(outer.DrawSize, Is.EqualTo(new Vector2(70, 30)),
            "The relative child contributes nothing; the fixed one decides the size.");
    }

    [Test]
    public void TestFlowOfAutoSizingChildrenResolvesInOneFrame()
    {
        var flow = new FlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 10),
        };

        for (int i = 0; i < 3; i++)
            flow.Add(new Container
            {
                AutoSizeAxes = Axes.Both,
                Child = new Box
                {
                    Size = new Vector2(40, 20)
                }
            });

        root.Add(flow);
        root.Load();
        root.CompleteLoad();

        frame();

        Assert.That(flow.DrawSize.Y, Is.EqualTo(80).Within(0.01f), "Three 20px rows plus two 10px gaps.");
        Assert.That(flow.DrawSize.X, Is.EqualTo(40).Within(0.01f));
    }
}
