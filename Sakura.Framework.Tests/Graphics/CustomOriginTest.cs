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
/// <see cref="Drawable.CustomOrigin"/>: a drawable placed, scaled, sheared and turned about any point, and the layout that
/// reads where its origin is taking that into account.
/// </summary>
[TestFixture]
public class CustomOriginTest
{
    private const float tolerance = 1e-3f;

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

        root.Load();
        root.CompleteLoad();
    }

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    /// <summary>
    /// Where a point on a drawable, as a fraction of its size, is on screen.
    /// </summary>
    private static Vector2 onScreen(Drawable drawable, Vector2 point) => Vector2.Transform(point, drawable.ModelMatrix);

    private static void assertAt(Vector2 actual, float x, float y, string message)
    {
        Assert.That(actual.X, Is.EqualTo(x).Within(tolerance), $"{message} (x)");
        Assert.That(actual.Y, Is.EqualTo(y).Within(tolerance), $"{message} (y)");
    }

    [Test]
    public void ItsPointIsWhereItsPositionSays()
    {
        var box = new Box { Size = new Vector2(100, 50), Position = new Vector2(300, 200), CustomOrigin = new Vector2(0.5f, 0.8f) };
        root.Add(box);
        frame();

        assertAt(onScreen(box, new Vector2(0.5f, 0.8f)), 300, 200, "the point");
        assertAt(onScreen(box, Vector2.Zero), 250, 160, "its top-left, 50 and 40 up from it");
    }

    /// <summary>
    /// What it's for: turning and scaling leave that point where it is, and move everything else round it.
    /// </summary>
    [Test]
    public void TurningAndScalingLeaveItsPointWhereItIs([Values(0f, 45f, 90f, 217f)] float rotation, [Values(0.5f, 1f, 3f)] float scale)
    {
        var box = new Box
        {
            Size = new Vector2(100, 50),
            Position = new Vector2(300, 200),
            CustomOrigin = new Vector2(0.5f, 0.8f),
            Rotation = rotation,
            Scale = new Vector2(scale)
        };
        root.Add(box);
        frame();

        assertAt(onScreen(box, new Vector2(0.5f, 0.8f)), 300, 200, "the point");
    }

    [Test]
    public void TurningMovesEverythingElseRoundIt()
    {
        var box = new Box { Size = new Vector2(100, 100), Position = new Vector2(300, 200), CustomOrigin = new Vector2(0.5f, 1), Rotation = 90 };
        root.Add(box);
        frame();

        // Its top middle is 100 straight up from its bottom middle; a quarter turn clockwise puts that 100 to the right.
        assertAt(onScreen(box, new Vector2(0.5f, 0)), 400, 200, "its top middle");
    }

    [Test]
    public void ShearLeavesItsPointWhereItIs()
    {
        var box = new Box { Size = new Vector2(100, 100), Position = new Vector2(300, 200), CustomOrigin = new Vector2(0.25f, 0.75f), Shear = new Vector2(0.5f, 0) };
        root.Add(box);
        frame();

        assertAt(onScreen(box, new Vector2(0.25f, 0.75f)), 300, 200, "the point");
    }

    [Test]
    public void APointOutsideItIsAllowed()
    {
        var box = new Box { Size = new Vector2(10, 10), Position = new Vector2(300, 200), CustomOrigin = new Vector2(0.5f, 3), Rotation = 180 };
        root.Add(box);
        frame();

        assertAt(onScreen(box, new Vector2(0.5f, 3)), 300, 200, "the point");
        assertAt(onScreen(box, new Vector2(0.5f, 0)), 300, 230, "swung right over it, 30 below");
    }

    /// <summary>
    /// Each of <see cref="Origin"/>'s points, as a custom one, is the same drawable.
    /// </summary>
    [Test]
    public void TheOriginsPointsAreTheSameAsCustomOnes([Values] Anchor origin)
    {
        var named = new Box { Size = new Vector2(100, 50), Position = new Vector2(300, 200), Rotation = 30, Scale = new Vector2(1.5f), Origin = origin };
        var custom = new Box { Size = new Vector2(100, 50), Position = new Vector2(300, 200), Rotation = 30, Scale = new Vector2(1.5f), CustomOrigin = Drawable.GetAnchorOriginVector(origin) };

        root.Add(named);
        root.Add(custom);
        frame();

        assertAt(onScreen(custom, Vector2.Zero), onScreen(named, Vector2.Zero).X, onScreen(named, Vector2.Zero).Y, "top-left");
        assertAt(onScreen(custom, Vector2.One), onScreen(named, Vector2.One).X, onScreen(named, Vector2.One).Y, "bottom-right");
    }

    [Test]
    public void ItTakesThePlaceOfOriginUntilCleared()
    {
        var box = new Box { Size = new Vector2(100, 100), Position = new Vector2(300, 200), Origin = Anchor.BottomRight, CustomOrigin = Vector2.Zero };
        root.Add(box);
        frame();
        assertAt(onScreen(box, Vector2.Zero), 300, 200, "placed by its custom point");

        box.CustomOrigin = null;
        frame();
        assertAt(onScreen(box, Vector2.One), 300, 200, "back to its origin");
    }

    [Test]
    public void ChangingItMovesItNextFrame()
    {
        var box = new Box { Size = new Vector2(100, 100), Position = new Vector2(300, 200) };
        root.Add(box);
        frame();

        box.CustomOrigin = new Vector2(0.5f);
        frame();

        assertAt(onScreen(box, new Vector2(0.5f)), 300, 200, "the new point");
    }

    [Test]
    public void ItWorksInAnAnchoredAndScaledParent()
    {
        var parent = new Container { Size = new Vector2(200), Position = new Vector2(100, 100), Scale = new Vector2(2) };
        var box = new Box { Size = new Vector2(20), Anchor = Anchor.Centre, CustomOrigin = new Vector2(0.5f, 1), Rotation = 45 };
        parent.Add(box);
        root.Add(parent);
        frame();

        // The parent's middle: 100 in, twice over, from (100, 100).
        assertAt(onScreen(box, new Vector2(0.5f, 1)), 300, 300, "the point");
    }

    /// <summary>
    /// An auto-sized parent makes room for where it puts the box: 10 of its 100 lies before its point at (0.1, 0), so at
    /// position 10 it starts at 0 and reaches 90 further than its point.
    /// </summary>
    [Test]
    public void AnAutoSizedParentMakesRoomForIt()
    {
        var parent = new Container { AutoSizeAxes = Axes.Both };
        var box = new Box { Size = new Vector2(100, 40), Position = new Vector2(10, 0), CustomOrigin = new Vector2(0.1f, 0) };
        parent.Add(box);
        root.Add(parent);
        frame();

        Assert.That(parent.Size.X, Is.EqualTo(100).Within(tolerance));
        Assert.That(parent.Size.Y, Is.EqualTo(40).Within(tolerance));
    }

    /// <summary>
    /// It's the top-left shortcut laying out goes through by default: a custom point has to take it out of that.
    /// </summary>
    [Test]
    public void TopLeftAnchoredAndCustomIsNotTheShortcut()
    {
        var parent = new Container { AutoSizeAxes = Axes.Both };
        parent.Add(new Box { Size = new Vector2(100, 40), CustomOrigin = new Vector2(1, 1) });
        root.Add(parent);
        frame();

        Assert.That(parent.Size.X, Is.Zero.Within(tolerance), "it's wholly up and left of the anchor, so needs no room");
    }

    /// <summary>
    /// A flow puts a child's box in its slot, whatever point it's placed by.
    /// </summary>
    [Test]
    public void AFlowFillsItsSlotAllTheSame()
    {
        var flow = new FlowContainer { AutoSizeAxes = Axes.Both, Direction = FlowDirection.Horizontal, Spacing = new Vector2(10, 0) };
        var first = new Box { Size = new Vector2(50) };
        var second = new Box { Size = new Vector2(50), CustomOrigin = new Vector2(0.5f, 0.5f) };
        flow.Add(first);
        flow.Add(second);
        root.Add(flow);

        frame();
        frame();

        Assert.Multiple(() =>
        {
            assertAt(onScreen(second, Vector2.Zero), 60, 0, "in its slot");
            Assert.That(flow.Size.X, Is.EqualTo(110).Within(tolerance));
        });
    }

    [Test]
    public void AContainerTurnsItsChildrenAboutIt()
    {
        var parent = new Container { Size = new Vector2(100), Position = new Vector2(300, 200), CustomOrigin = new Vector2(0, 0), Rotation = 90 };
        var child = new Box { Size = new Vector2(10), Position = new Vector2(50, 0) };
        parent.Add(child);
        root.Add(parent);
        frame();

        assertAt(onScreen(child, Vector2.Zero), 300, 250, "50 along its top, now pointing down");
    }
}
