// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// <see cref="PlotCanvas"/>'s marks in pixels and vertices, on the CPU reference rasterizer and on Vulkan (see
/// <see cref="PixelCapture"/>).
/// </summary>
[TestFixture(PixelCapture.HEADLESS)]
[TestFixture(PixelCapture.VULKAN)]
public class PlotCanvasTest
{
    private const int surface = 100;

    private readonly string rendererKind;
    private DrawablePixels pixels = null!;
    private PlotCanvas canvas = null!;

    public PlotCanvasTest(string rendererKind)
    {
        this.rendererKind = rendererKind;
    }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Logger.Initialize();
        pixels = new DrawablePixels(rendererKind, surface, surface);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        pixels?.Dispose();
        Logger.Shutdown();
    }

    [SetUp]
    public void SetUp()
    {
        pixels.Reset();
        pixels.Root.Add(canvas = new PlotCanvas());
    }

    private bool filled(PixelSurface drawn, int x, int y) => drawn[x, y].X > 1 - pixels.Tolerance;

    private bool empty(PixelSurface drawn, int x, int y) => drawn[x, y].X < pixels.Tolerance;

    [Test]
    public void ABoxFillsItsRectangle()
    {
        canvas.Box(new RectangleF(20, 30, 40, 20), Color.White);

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, 40, 40), "inside");
            Assert.That(empty(drawn, 70, 40), "past its right");
            Assert.That(empty(drawn, 40, 55), "below it");
            Assert.That(canvas.VertexCount, Is.EqualTo(6));
        });
    }

    /// <summary>
    /// More than a batch holds (a timing graph's dots, a few thousand of them), and each piece in its place: joined up as
    /// quads past the first flush, pieces at either side would draw across the middle.
    /// </summary>
    [Test]
    public void MoreThanABatchHoldsStaysWhereItIs()
    {
        for (int i = 0; i < 6000; i++)
        {
            float x = i % 2 == 0 ? 10 : 80;
            canvas.Triangle(new Vector2(x, 20), new Vector2(x + 10, 20), new Vector2(x, 30), Color.White);
        }

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(canvas.VertexCount, Is.EqualTo(18000), "past the 12000 a batch holds");
            Assert.That(filled(drawn, 12, 22), "the pieces on the left");
            Assert.That(filled(drawn, 82, 22), "the pieces on the right");

            for (int y = 0; y < surface; y += 5)
            {
                Assert.That(empty(drawn, 50, y), $"nothing between them, at {y}");
                Assert.That(empty(drawn, 60, y), $"nothing between them, at {y}");
            }

            Assert.That(empty(drawn, 12, 40), "nothing under them");
        });
    }

    [Test]
    public void ATriangleFillsItsThreePoints()
    {
        canvas.Triangle(new Vector2(20, 20), new Vector2(80, 20), new Vector2(20, 80), Color.White);

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, 30, 30), "inside");
            Assert.That(empty(drawn, 70, 70), "across its long edge");
            Assert.That(canvas.VertexCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void AnAreaFillsDownToItsBaseline()
    {
        canvas.Area(new[] { new Vector2(10, 40), new Vector2(50, 40), new Vector2(90, 20) }, 80, Color.White);

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, 30, 60), "under its line");
            Assert.That(empty(drawn, 30, 30), "over its line");
            Assert.That(filled(drawn, 80, 35), "under where its line rises");
            Assert.That(empty(drawn, 30, 90), "past its baseline");
            Assert.That(empty(drawn, 5, 60), "before it starts");
            Assert.That(canvas.VertexCount, Is.EqualTo(2 * 6));
        });
    }

    [Test]
    public void ALineRunsThroughItsPoints()
    {
        canvas.Line(new[] { new Vector2(10, 50), new Vector2(50, 50), new Vector2(50, 90) }, 4, Color.White);

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, 30, 50), "along its first stretch");
            Assert.That(filled(drawn, 50, 70), "along its second");
            Assert.That(empty(drawn, 30, 42), "off to one side");
            Assert.That(empty(drawn, 70, 50), "not on past the corner");
            Assert.That(canvas.VertexCount, Is.EqualTo(2 * 18));
        });
    }

    [Test]
    public void ADashedLineLeavesGaps()
    {
        canvas.Line(new Vector2(0, 50), new Vector2(100, 50), 4, Color.White, 10, 10);

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, 5, 50), "its first dash");
            Assert.That(empty(drawn, 15, 50), "its first gap");
            Assert.That(filled(drawn, 25, 50), "its second dash");
            Assert.That(empty(drawn, 95, 50), "its last gap");
        });
    }

    /// <summary>
    /// The pattern carries on along the line past a corner, rather than starting again there.
    /// </summary>
    [Test]
    public void ADashCarriesOnRoundACorner()
    {
        // 15 along the first stretch leaves the second starting 5 into a gap, so its first 5 are clear and the next 10 a dash.
        canvas.Line(new[] { new Vector2(0, 40), new Vector2(15, 40), new Vector2(15, 90) }, 4, Color.White, 10, 10);

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, 5, 40), "the first dash");
            Assert.That(empty(drawn, 12, 40), "a gap up to the corner");
            Assert.That(empty(drawn, 15, 43), "the same gap past it");
            Assert.That(filled(drawn, 15, 50), "the second dash");
        });
    }

    /// <summary>
    /// A dash pattern ending short leaves vertices over, which draw nothing.
    /// </summary>
    [Test]
    public void DashesKeepTheVertexListWhole()
    {
        canvas.Line(new Vector2(0, 50), new Vector2(100, 50), 2, Color.White, 3, 4);
        pixels.Frame();

        Assert.That(canvas.VertexCount % 3, Is.Zero);

        // Whatever wasn't written is all zeroes, so the triangles it makes have no area.
        for (int i = 0; i < canvas.VertexCount; i++)
            Assert.That(float.IsNaN(canvas.Vertices[i].Color.W), Is.False);
    }

    [Test]
    public void ADotIsRoundWithin()
    {
        canvas.Dot(new Vector2(50, 50), 10, Color.White);

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, 50, 50), "its middle");
            Assert.That(filled(drawn, 56, 50), "inside it");
            Assert.That(empty(drawn, 62, 50), "past its radius");
            Assert.That(empty(drawn, 58, 58), "past its radius on the diagonal, inside its square");
        });
    }

    [Test]
    public void ARingIsHollow()
    {
        canvas.Ring(new Vector2(50, 50), 20, 4, Color.White);

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, 70, 50), "on its line");
            Assert.That(filled(drawn, 50, 30), "round it");
            Assert.That(empty(drawn, 50, 50), "its middle");
            Assert.That(empty(drawn, 80, 50), "past it");
        });
    }

    /// <summary>
    /// Rounder the bigger it's drawn: dots and rings take their segments from their size on screen, between 8 and 48.
    /// </summary>
    [Test]
    public void BiggerOnScreenIsRounder()
    {
        canvas.Dot(new Vector2(50, 50), 1, Color.White);
        pixels.Frame();
        Assert.That(canvas.VertexCount, Is.EqualTo(9 * 8), "the fewest");

        canvas.Clear();
        canvas.Dot(new Vector2(50, 50), 10, Color.White);
        pixels.Frame();
        Assert.That(canvas.VertexCount, Is.EqualTo(9 * 25), "10 × 2.5");

        canvas.Scale = new Vector2(1.2f);
        pixels.Frame();
        Assert.That(canvas.VertexCount, Is.EqualTo(9 * 30), "and more when scaled up");

        canvas.Scale = new Vector2(4);
        pixels.Frame();
        Assert.That(canvas.VertexCount, Is.EqualTo(9 * 48), "the most");
    }

    /// <summary>
    /// Smaller than its feather, a dot dims rather than drawing bigger than it is.
    /// </summary>
    [Test]
    public void ATinyDotDims()
    {
        canvas.Dot(new Vector2(50, 50), 0.25f, Color.White);
        pixels.Frame();

        Assert.That(canvas.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f));
    }

    [Test]
    public void EachMarkHasItsOwnColor()
    {
        canvas.Box(new RectangleF(0, 0, 40, 40), Color.Red);
        canvas.Box(new RectangleF(60, 60, 40, 40), Color.Lime.WithAlpha(0.5f));

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(drawn[20, 20].X, Is.EqualTo(1).Within(pixels.Tolerance));
            Assert.That(drawn[20, 20].Y, Is.Zero.Within(pixels.Tolerance));
            Assert.That(drawn[80, 80].X, Is.Zero.Within(pixels.Tolerance));
            Assert.That(drawn[80, 80].Y, Is.EqualTo(0.5f).Within(0.01f));
        });
    }

    /// <summary>
    /// Later marks draw over earlier ones.
    /// </summary>
    [Test]
    public void MarksDrawInOrder()
    {
        canvas.Box(new RectangleF(0, 0, 100, 100), Color.Red);
        canvas.Box(new RectangleF(40, 40, 20, 20), Color.Lime);

        var pixel = pixels.Render()[50, 50];

        Assert.Multiple(() =>
        {
            Assert.That(pixel.Y, Is.EqualTo(1).Within(pixels.Tolerance));
            Assert.That(pixel.X, Is.Zero.Within(pixels.Tolerance));
        });
    }

    [Test]
    public void ItsFadeAndColorAlphaApplyToEveryMark()
    {
        canvas.Box(new RectangleF(0, 0, 100, 100), Color.White);
        canvas.Color = Color.White.WithAlpha(0.5f);
        canvas.Alpha = 0.5f;

        Assert.That(pixels.Render()[50, 50].X, Is.EqualTo(0.25f).Within(0.01f));
    }

    [Test]
    public void ClearLeavesNothing()
    {
        canvas.Box(new RectangleF(0, 0, 100, 100), Color.White);
        canvas.Dot(new Vector2(50, 50), 10, Color.White);
        Assert.That(canvas.Count, Is.EqualTo(2));
        pixels.Render();

        canvas.Clear();
        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Count, Is.Zero);
            Assert.That(canvas.VertexCount, Is.Zero);
            Assert.That(canvas.DrawRectangle.Width, Is.Zero);
            Assert.That(empty(drawn, 50, 50));
        });
    }

    [Test]
    public void AMarkAddedLaterIsDrawn()
    {
        for (int i = 0; i < 3; i++)
            pixels.Frame();

        canvas.Box(new RectangleF(40, 40, 20, 20), Color.White);

        Assert.That(filled(pixels.Render(), 50, 50));
    }

    [Test]
    public void TooFewPointsDrawNothing()
    {
        canvas.Line(Array.Empty<Vector2>(), 2, Color.White);
        canvas.Line(new[] { new Vector2(0, 0) }, 2, Color.White);
        canvas.Area(new[] { new Vector2(0, 0) }, 50, Color.White);
        pixels.Frame();

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Count, Is.Zero);
            Assert.That(canvas.VertexCount, Is.Zero);
        });
    }

    [Test]
    public void ItsRectangleHoldsItsMarks()
    {
        canvas.Box(new RectangleF(10, 20, 10, 10), Color.White);
        canvas.Box(new RectangleF(70, 50, 10, 10), Color.White);
        pixels.Frame();

        Assert.Multiple(() =>
        {
            Assert.That(canvas.DrawRectangle.X, Is.EqualTo(10).Within(0.01f));
            Assert.That(canvas.DrawRectangle.Y, Is.EqualTo(20).Within(0.01f));
            Assert.That(canvas.DrawRectangle.Width, Is.EqualTo(70).Within(0.01f));
            Assert.That(canvas.DrawRectangle.Height, Is.EqualTo(40).Within(0.01f));
        });
    }

    /// <summary>
    /// Its points are in its own units, its size: it being a different size doesn't move them.
    /// </summary>
    [Test]
    public void PointsAreInItsOwnUnits()
    {
        pixels.Reset();
        pixels.Root.Add(canvas = new PlotCanvas { RelativeSizeAxes = Axes.None, Position = new Vector2(10, 10), Size = new Vector2(50) });
        canvas.Box(new RectangleF(10, 10, 10, 10), Color.White);

        var drawn = pixels.Render();
        Assert.That(filled(drawn, 25, 25), "at 10 in, from where it's placed");

        canvas.Size = new Vector2(90);
        drawn = pixels.Render();
        Assert.That(filled(drawn, 25, 25), "still there, a bigger canvas");
        Assert.That(empty(drawn, 45, 45), "and no bigger itself");
    }

    [Test]
    public void ACanvasWithNoSizeDrawsNothing()
    {
        pixels.Reset();
        pixels.Root.Add(canvas = new PlotCanvas { RelativeSizeAxes = Axes.None, Size = Vector2.Zero });
        canvas.Dot(new Vector2(5, 5), 2, Color.White);
        pixels.Frame();

        Assert.That(canvas.VertexCount, Is.Zero);
    }

    [Test]
    public void ItTakesNoInput() => Assert.That(canvas.HandlePositionalInput, Is.False);
}
