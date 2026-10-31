// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// <see cref="RingArc"/> in pixels and vertices, on the CPU reference rasterizer and on Vulkan (see <see cref="PixelCapture"/>).
/// </summary>
[TestFixture(PixelCapture.HEADLESS)]
[TestFixture(PixelCapture.VULKAN)]
public class RingArcTest
{
    private const int surface = 64;

    // The middle of the band of a ring filling the surface with an inner radius of 0.5, every 45° clockwise from 12 o'clock.
    private const int middle = 32;
    private const int band = 24;
    private const int diagonal = 17;

    private readonly string rendererKind;
    private DrawablePixels pixels = null!;

    public RingArcTest(string rendererKind)
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
    public void SetUp() => pixels.Reset();

    private RingArc addRing(float start = 0, float progress = 1)
    {
        var ring = new RingArc
        {
            Size = new Vector2(surface),
            InnerRadius = 0.5f,
            Start = start,
            Progress = progress,
            Color = Color.White
        };

        pixels.Root.Add(ring);
        return ring;
    }

    private bool filled(PixelSurface drawn, int x, int y) => drawn[x, y].X > 1 - pixels.Tolerance;

    private bool empty(PixelSurface drawn, int x, int y) => drawn[x, y].X < pixels.Tolerance;

    [Test]
    public void AFullRingGoesAllTheWayRound()
    {
        addRing();

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, middle, middle - band), "12 o'clock");
            Assert.That(filled(drawn, middle + band, middle), "3 o'clock");
            Assert.That(filled(drawn, middle, middle + band), "6 o'clock");
            Assert.That(filled(drawn, middle - band, middle), "9 o'clock");
            Assert.That(empty(drawn, middle, middle), "its hole");
            Assert.That(empty(drawn, 2, 2), "outside it");
        });
    }

    [Test]
    public void ItFillsClockwiseFromTwelve()
    {
        addRing(progress: 0.25f);

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, middle + diagonal, middle - diagonal), "up to the right");
            Assert.That(empty(drawn, middle - diagonal, middle - diagonal), "not up to the left");
            Assert.That(empty(drawn, middle + diagonal, middle + diagonal), "not yet down to the right");
        });
    }

    [Test]
    public void ItRunsFromStartToProgress()
    {
        addRing(start: 0.5f, progress: 0.75f);

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, middle - diagonal, middle + diagonal), "down to the left");
            Assert.That(empty(drawn, middle + diagonal, middle - diagonal), "before where it starts");
            Assert.That(empty(drawn, middle - diagonal, middle - diagonal), "past where it ends");
        });
    }

    [Test]
    public void NoInnerRadiusIsAPie()
    {
        RingArc ring = addRing();
        ring.InnerRadius = 0;

        Assert.That(filled(pixels.Render(), middle, middle));
    }

    [Test]
    public void ItsColorAndAlphaShow()
    {
        RingArc ring = addRing();
        ring.Color = Color.Red;
        ring.Alpha = 0.5f;

        var pixel = pixels.Render()[middle, middle - band];

        Assert.Multiple(() =>
        {
            Assert.That(pixel.X, Is.EqualTo(0.5f).Within(pixels.Tolerance));
            Assert.That(pixel.Y, Is.Zero.Within(pixels.Tolerance));
        });
    }

    /// <summary>
    /// Its top edge halfway through a pixel's top half: the reference rasterizer reads the feather exactly at the pixel's
    /// centre, 0.25 inside an edge that fades from 0.5 outside to 0.5 inside.
    /// </summary>
    [Test]
    public void SmoothEdgesFadeOverAPixelAndPlainOnesStep()
    {
        if (!pixels.IsReferenceRasterizer)
            Assert.Ignore("A GPU samples coverage in its own way, so this reads the reference rasterizer only.");

        const float top = 4.25f;

        var ring = new RingArc
        {
            Position = new Vector2(4, top),
            Size = new Vector2(56),
            InnerRadius = 0.5f,
            Color = Color.White
        };
        pixels.Root.Add(ring);

        float plain = pixels.Render()[32, 4].X;

        ring.SmoothEdges = true;
        var smooth = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo(1), "a plain edge is in or out");
            Assert.That(smooth[32, 4].X, Is.EqualTo(0.75f).Within(0.03f), "a smooth one is part way through its fade");
            Assert.That(smooth[32, 3].X, Is.Zero, "and has none just beyond it");
        });
    }

    [Test]
    public void ItUsesVerticesForWhatItDraws()
    {
        RingArc ring = addRing();
        pixels.Frame();
        Assert.That(ring.VertexCount, Is.EqualTo(160 * 6), "a whole ring");

        ring.Progress = 0.5f;
        pixels.Frame();
        Assert.That(ring.VertexCount, Is.EqualTo(80 * 6), "half");

        ring.SmoothEdges = true;
        pixels.Frame();
        Assert.That(ring.VertexCount, Is.EqualTo(80 * 18), "half, three times over for its edges");
    }

    [Test]
    public void AnArcWithNothingToDrawIsGoneUntilItHasOneAgain()
    {
        RingArc ring = addRing(0.5f, 0.5f);
        pixels.Frame();

        Assert.Multiple(() =>
        {
            Assert.That(ring.VertexCount, Is.Zero, "starting where it ends");
            Assert.That(ring.DrawRectangle.Width, Is.Zero);
        });

        ring.Start = 0.7f;
        pixels.Frame();
        Assert.That(ring.VertexCount, Is.Zero, "starting past where it ends");

        ring.Start = 0.1f;
        pixels.Frame();
        Assert.That(ring.VertexCount, Is.EqualTo(64 * 6));
    }

    [Test]
    public void ItsRectangleIsWhatItDraws()
    {
        var ring = new RingArc { Position = new Vector2(8, 4), Size = new Vector2(40), InnerRadius = 0.5f };
        pixels.Root.Add(ring);
        pixels.Frame();

        Assert.Multiple(() =>
        {
            Assert.That(ring.DrawRectangle.X, Is.EqualTo(8).Within(0.01f));
            Assert.That(ring.DrawRectangle.Y, Is.EqualTo(4).Within(0.01f));
            Assert.That(ring.DrawRectangle.Width, Is.EqualTo(40).Within(0.01f));
            Assert.That(ring.DrawRectangle.Height, Is.EqualTo(40).Within(0.01f));
        });
    }

    [Test]
    public void StartAndProgressAreClamped()
    {
        RingArc ring = addRing();
        ring.Start = -1;
        ring.Progress = 2;

        Assert.Multiple(() =>
        {
            Assert.That(ring.Start, Is.Zero);
            Assert.That(ring.Progress, Is.EqualTo(1));
        });
    }
}
