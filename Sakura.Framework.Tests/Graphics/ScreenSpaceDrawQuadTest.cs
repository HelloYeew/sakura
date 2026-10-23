// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests for <see cref="Drawable.ScreenSpaceDrawQuad"/>: a drawable's own box in screen space, corner by corner, through
/// its own and every inherited transform, where <see cref="Drawable.DrawRectangle"/> is only the box around it.
/// </summary>
[TestFixture]
public class ScreenSpaceDrawQuadTest
{
    private const float tolerance = 0.01f;

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

    private void settle()
    {
        for (int i = 0; i < 3; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }
    }

    private static void assertCorner(Vector2 actual, Vector2 expected, string corner)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(tolerance), $"{corner} X");
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(tolerance), $"{corner} Y");
    }

    [Test]
    public void TestAxisAlignedQuadIsTheDrawRectangle()
    {
        var box = new Box { Position = new Vector2(100, 50), Size = new Vector2(200, 80) };
        root.Add(box);
        settle();

        Quad quad = box.ScreenSpaceDrawQuad;

        Assert.Multiple(() =>
        {
            assertCorner(quad.TopLeft, new Vector2(100, 50), "top-left");
            assertCorner(quad.TopRight, new Vector2(300, 50), "top-right");
            assertCorner(quad.BottomRight, new Vector2(300, 130), "bottom-right");
            assertCorner(quad.BottomLeft, new Vector2(100, 130), "bottom-left");
            Assert.That(quad.BoundingRectangle, Is.EqualTo(box.DrawRectangle));
        });
    }

    [Test]
    public void TestRotatedQuadKeepsItsCorners()
    {
        // 100x100 around its centre at (200, 200), a quarter turn clockwise: the top-left corner comes round to the
        // top-right of the screen box, and so on.
        var box = new Box
        {
            Position = new Vector2(200, 200),
            Size = new Vector2(100),
            Origin = Anchor.Centre,
            Rotation = 90
        };
        root.Add(box);
        settle();

        Quad quad = box.ScreenSpaceDrawQuad;

        Assert.Multiple(() =>
        {
            assertCorner(quad.TopLeft, new Vector2(250, 150), "top-left");
            assertCorner(quad.TopRight, new Vector2(250, 250), "top-right");
            assertCorner(quad.BottomRight, new Vector2(150, 250), "bottom-right");
            assertCorner(quad.BottomLeft, new Vector2(150, 150), "bottom-left");
        });
    }

    [Test]
    public void TestShearedQuadIsNotItsBoundingBox()
    {
        // A slanted row, as in a sheared panel: the top edge stays put and the bottom is shifted left by half the height.
        var row = new Box
        {
            Position = new Vector2(100, 100),
            Size = new Vector2(200, 40),
            Shear = new Vector2(-0.5f, 0)
        };
        root.Add(row);
        settle();

        Quad quad = row.ScreenSpaceDrawQuad;

        Assert.Multiple(() =>
        {
            assertCorner(quad.TopLeft, new Vector2(100, 100), "top-left");
            assertCorner(quad.TopRight, new Vector2(300, 100), "top-right");
            assertCorner(quad.BottomRight, new Vector2(280, 140), "bottom-right");
            assertCorner(quad.BottomLeft, new Vector2(80, 140), "bottom-left");

            // The box around it is wider than the row by the lean.
            Assert.That(row.DrawRectangle.Width, Is.EqualTo(220).Within(tolerance));
        });
    }

    [Test]
    public void TestQuadFollowsInheritedTransforms()
    {
        // A box in a scaled, rotated container: the quad has to come through both, and agree with ToScreenSpace.
        var child = new Box { Position = new Vector2(10, 20), Size = new Vector2(30, 40) };
        var parent = new Container
        {
            Position = new Vector2(400, 300),
            Size = new Vector2(100),
            Scale = new Vector2(2),
            Rotation = 30,
            Child = child
        };
        root.Add(parent);
        settle();

        Quad quad = child.ScreenSpaceDrawQuad;
        Vector2 size = child.DrawSize;

        Assert.Multiple(() =>
        {
            assertCorner(quad.TopLeft, child.ToScreenSpace(Vector2.Zero), "top-left");
            assertCorner(quad.TopRight, child.ToScreenSpace(new Vector2(size.X, 0)), "top-right");
            assertCorner(quad.BottomRight, child.ToScreenSpace(size), "bottom-right");
            assertCorner(quad.BottomLeft, child.ToScreenSpace(new Vector2(0, size.Y)), "bottom-left");

            // Scaled by 2, the sides keep the child's proportions: 60 by 80 on screen, at an angle.
            Assert.That(Vector2.Distance(quad.TopLeft, quad.TopRight), Is.EqualTo(60).Within(tolerance));
            Assert.That(Vector2.Distance(quad.TopLeft, quad.BottomLeft), Is.EqualTo(80).Within(tolerance));
        });
    }

    [Test]
    public void TestContainerHasAQuad()
    {
        var container = new Container { Position = new Vector2(50, 60), Size = new Vector2(120, 90) };
        root.Add(container);
        settle();

        Quad quad = container.ScreenSpaceDrawQuad;

        Assert.Multiple(() =>
        {
            assertCorner(quad.TopLeft, new Vector2(50, 60), "top-left");
            assertCorner(quad.BottomRight, new Vector2(170, 150), "bottom-right");
        });
    }
}
