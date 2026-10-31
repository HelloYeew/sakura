// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// <see cref="ParticleField"/> in pixels and vertices, on the CPU reference rasterizer and on Vulkan (see
/// <see cref="PixelCapture"/>).
/// </summary>
[TestFixture(PixelCapture.HEADLESS)]
[TestFixture(PixelCapture.VULKAN)]
public class ParticleFieldTest
{
    private const int surface = 64;

    private readonly string rendererKind;
    private DrawablePixels pixels = null!;
    private ParticleField field = null!;

    public ParticleFieldTest(string rendererKind)
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
        pixels.Root.Add(field = new ParticleField());
    }

    private static Particle piece(float x, float y, float width = 8, float height = 8, float rotation = 0, Color? color = null, float alpha = 1) =>
        new Particle(new Vector2(x, y), new Vector2(width, height), rotation, color ?? Color.White, alpha);

    private bool filled(PixelSurface drawn, int x, int y) => drawn[x, y].X > 1 - pixels.Tolerance;

    private bool empty(PixelSurface drawn, int x, int y) => drawn[x, y].X < pixels.Tolerance;

    [Test]
    public void APieceGoesWhereItsMiddleIs()
    {
        field.Add(piece(16, 16));

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, 16, 16), "its middle");
            Assert.That(filled(drawn, 13, 13), "inside its size");
            Assert.That(empty(drawn, 22, 16), "past its side");
            Assert.That(empty(drawn, 40, 40), "elsewhere");
        });
    }

    [Test]
    public void APieceTurnsClockwiseAboutItsMiddle()
    {
        // Long and thin, turned an eighth of a turn: it runs down to the right, y pointing down.
        field.Add(piece(32, 32, width: 40, height: 4, rotation: MathF.PI / 4));

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(filled(drawn, 40, 40), "down to the right");
            Assert.That(filled(drawn, 24, 24), "up to the left");
            Assert.That(empty(drawn, 40, 24), "not up to the right");
            Assert.That(empty(drawn, 50, 32), "not along where it was");
        });
    }

    [Test]
    public void EachPieceHasItsOwnTint()
    {
        field.Add(piece(16, 16, color: Color.Red));
        field.Add(piece(48, 48, color: Color.Lime));

        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(drawn[16, 16].X, Is.EqualTo(1).Within(pixels.Tolerance));
            Assert.That(drawn[16, 16].Y, Is.Zero.Within(pixels.Tolerance));
            Assert.That(drawn[48, 48].X, Is.Zero.Within(pixels.Tolerance));
            Assert.That(drawn[48, 48].Y, Is.EqualTo(1).Within(pixels.Tolerance));
        });
    }

    /// <summary>
    /// A piece's own alpha, its tint's, the field's color's and the field's fade all count.
    /// </summary>
    [Test]
    public void TheAlphasMultiply()
    {
        field.Add(piece(32, 32, color: Color.White.WithAlpha(0.5f), alpha: 0.5f));
        field.Color = Color.White.WithAlpha(0.5f);
        field.Alpha = 0.5f;

        Assert.That(pixels.Render()[32, 32].X, Is.EqualTo(0.0625f).Within(0.01f));
    }

    [Test]
    public void ClearLeavesNothing()
    {
        field.Add(piece(16, 16));
        pixels.Render();

        field.Clear();
        var drawn = pixels.Render();

        Assert.Multiple(() =>
        {
            Assert.That(field.Count, Is.Zero);
            Assert.That(field.VertexCount, Is.Zero);
            Assert.That(field.DrawRectangle.Width, Is.Zero);
            Assert.That(empty(drawn, 16, 16));
        });
    }

    /// <summary>
    /// A piece added any time gets its vertices the next frame, its parent never told (its size hasn't changed).
    /// </summary>
    [Test]
    public void APieceAddedLaterIsDrawn([Values] bool masked)
    {
        pixels.Reset();

        var holder = new Container { RelativeSizeAxes = Axes.Both, Masking = masked };
        holder.Add(field = new ParticleField());
        pixels.Root.Add(holder);

        for (int i = 0; i < 3; i++)
            pixels.Frame();

        field.Add(piece(16, 16));
        pixels.Frame();

        Assert.Multiple(() =>
        {
            Assert.That(field.Count, Is.EqualTo(1));
            Assert.That(field.VertexCount, Is.EqualTo(6));
            Assert.That(field.DrawRectangle.Width, Is.EqualTo(8).Within(0.01f));
        });
    }

    [Test]
    public void ItsRectangleHoldsEveryPiece()
    {
        field.Add(piece(10, 10, 4, 4));
        field.Add(piece(50, 40, 4, 4));
        pixels.Frame();

        Assert.Multiple(() =>
        {
            Assert.That(field.DrawRectangle.X, Is.EqualTo(8).Within(0.01f));
            Assert.That(field.DrawRectangle.Y, Is.EqualTo(8).Within(0.01f));
            Assert.That(field.DrawRectangle.Width, Is.EqualTo(44).Within(0.01f));
            Assert.That(field.DrawRectangle.Height, Is.EqualTo(34).Within(0.01f));
        });
    }

    [Test]
    public void ItReadsPartOfATexture()
    {
        var whole = new HeadlessRenderer(new HeadlessTextureManager()).WhitePixel;

        field.Texture = new Texture(whole.BackendTexture!, new RectangleF(0.25f, 0.5f, 0.5f, 0.25f));
        field.Add(piece(16, 16));
        pixels.Frame();

        Assert.Multiple(() =>
        {
            Assert.That(field.Vertices[0].TexCoords, Is.EqualTo(new Vector2(0.25f, 0.5f)), "top left");
            Assert.That(field.Vertices[2].TexCoords, Is.EqualTo(new Vector2(0.75f, 0.75f)), "bottom right");
        });
    }

    [Test]
    public void AFieldWithNoSizeDrawsNothing()
    {
        pixels.Reset();
        pixels.Root.Add(field = new ParticleField { RelativeSizeAxes = Axes.None, Size = Vector2.Zero });
        field.Add(piece(16, 16));
        pixels.Frame();

        Assert.That(field.VertexCount, Is.Zero);
    }

    [Test]
    public void ItTakesNoInput() => Assert.That(field.HandlePositionalInput, Is.False);
}
