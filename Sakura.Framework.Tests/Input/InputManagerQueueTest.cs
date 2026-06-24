// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Input;

[TestFixture]
public class InputManagerQueueTest
{
    private ManualClock manual = null!;
    private Container root = null!;
    private InputManager manager = null!;

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

        root.Load();
        root.LoadComplete();

        manager = new InputManager();
    }

    private void settle()
    {
        for (int i = 0; i < 3; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }
    }

    [Test]
    public void TestPositionalQueueIsFrontToBack()
    {
        var back = new TestPiece
        {
            Position = new Vector2(100, 100),
            Size = new Vector2(200)
        };
        var front = new TestPiece
        {
            Position = new Vector2(100, 100),
            Size = new Vector2(200)
        };
        root.Add(back);
        root.Add(front);
        settle();

        manager.BuildQueues(root, new Vector2(150, 150));

        Assert.Multiple(() =>
        {
            Assert.That(manager.PositionalInputQueue, Does.Contain(front));
            Assert.That(manager.PositionalInputQueue, Does.Contain(back));
            Assert.That(indexOf(manager.PositionalInputQueue, front),
                Is.LessThan(indexOf(manager.PositionalInputQueue, back)),
                "Front-most drawable must precede the one behind it.");
            Assert.That(indexOf(manager.PositionalInputQueue, front),
                Is.LessThan(indexOf(manager.PositionalInputQueue, root)),
                "Children precede their parent in the positional queue.");
        });
    }

    [Test]
    public void TestDepthOrdersFrontToBack()
    {
        var low = new TestPiece
        {
            Position = new Vector2(0, 0),
            Size = new Vector2(400),
            Depth = -10
        };
        var high = new TestPiece
        {
            Position = new Vector2(0, 0),
            Size = new Vector2(400),
            Depth = 10
        };
        root.Add(low);
        root.Add(high);
        settle();

        manager.BuildQueues(root, new Vector2(50, 50));

        Assert.That(indexOf(manager.PositionalInputQueue, high),
            Is.LessThan(indexOf(manager.PositionalInputQueue, low)),
            "Higher Depth is front-most and must come first in the positional queue.");
    }

    [Test]
    public void TestPositionalQueueFiltersByPoint()
    {
        var left = new TestPiece
        {
            Position = new Vector2(0, 0),
            Size = new Vector2(100)
        };
        var right = new TestPiece
        {
            Position = new Vector2(300, 0),
            Size = new Vector2(100)
        };
        root.Add(left);
        root.Add(right);
        settle();

        manager.BuildQueues(root, new Vector2(350, 50));

        Assert.Multiple(() =>
        {
            Assert.That(manager.PositionalInputQueue, Does.Contain(right), "The drawable under the point is queued.");
            Assert.That(manager.PositionalInputQueue, Does.Not.Contain(left), "A drawable the point misses is excluded.");
        });
    }

    [Test]
    public void TestPositionalOptOutExcludesDrawableAndSubtree()
    {
        var optedOut = new OptOutPositional
        {
            Position = new Vector2(0, 0),
            Size = new Vector2(200)
        };
        var child = new TestPiece
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1)
        };
        optedOut.Add(child);
        root.Add(optedOut);
        settle();

        manager.BuildQueues(root, new Vector2(50, 50));

        Assert.Multiple(() =>
        {
            Assert.That(manager.PositionalInputQueue, Does.Not.Contain(optedOut),
                "A drawable with HandlePositionalInput = false is excluded.");
            Assert.That(manager.PositionalInputQueue, Does.Not.Contain(child),
                "Its subtree is excluded too (traversal stops).");
            // It remains in the non-positional queue, since the opt-outs are independent.
            Assert.That(manager.NonPositionalInputQueue, Does.Contain(optedOut));
        });
    }

    [Test]
    public void TestNonPositionalQueueIgnoresPositionAndIncludesAll()
    {
        // A drawable far from the point is still in the non-positional queue (keys are non-positional).
        var offscreen = new TestPiece { Position = new Vector2(700, 500), Size = new Vector2(50) };
        root.Add(offscreen);
        settle();

        manager.BuildQueues(root, new Vector2(10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(manager.NonPositionalInputQueue, Does.Contain(offscreen));
            Assert.That(manager.NonPositionalInputQueue, Does.Contain(root));
            Assert.That(manager.PositionalInputQueue, Does.Not.Contain(offscreen),
                "But it is absent from the positional queue, since the point misses it.");
        });
    }

    [Test]
    public void TestNonPositionalOptOutExcludesDrawableAndSubtree()
    {
        var optedOut = new OptOutNonPositional { Size = new Vector2(50) };
        var child = new TestPiece { Size = new Vector2(10) };
        optedOut.Add(child);
        root.Add(optedOut);
        settle();

        manager.BuildQueues(root, new Vector2(5, 5));

        Assert.Multiple(() =>
        {
            Assert.That(manager.NonPositionalInputQueue, Does.Not.Contain(optedOut));
            Assert.That(manager.NonPositionalInputQueue, Does.Not.Contain(child),
                "A non-positional opt-out removes its whole subtree from the queue.");
        });
    }

    [Test]
    public void TestNullRootProducesEmptyQueues()
    {
        manager.BuildQueues(null!, Vector2.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(manager.NonPositionalInputQueue, Is.Empty);
            Assert.That(manager.PositionalInputQueue, Is.Empty);
        });
    }

    private static int indexOf(System.Collections.Generic.IReadOnlyList<Drawable> queue, Drawable target)
    {
        for (int i = 0; i < queue.Count; i++)
        {
            if (ReferenceEquals(queue[i], target))
                return i;
        }

        return -1;
    }

    private partial class TestPiece : Container
    {
    }

    private partial class OptOutPositional : Container
    {
        public override bool HandlePositionalInput => false;
    }

    private partial class OptOutNonPositional : Container
    {
        public override bool HandleNonPositionalInput => false;
    }
}
