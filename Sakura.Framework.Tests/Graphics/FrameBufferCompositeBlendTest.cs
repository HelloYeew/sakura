// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// The compositing arithmetic test : rendering a subtree into an offscreen buffer and drawing that
/// buffer back must produce the same pixels as drawing the subtree straight to the target.
/// </summary>
[TestFixture]
public class FrameBufferCompositeBlendTest
{
    private const int surface_size = 8;

    // The worked example from the plan: two overlapping children, each at alpha 0.5 (an opaque child
    // under a container faded to 0.5), over an opaque background.
    private static readonly Vector4 child_one = new Vector4(1, 0, 0, 0.5f);
    private static readonly Vector4 child_two = new Vector4(0, 1, 0, 0.5f);
    private static readonly Vector4 background = new Vector4(0, 0, 1, 1);

    private HeadlessRenderer renderer = null!;

    [SetUp]
    public void SetUp()
    {
        renderer = new HeadlessRenderer(new HeadlessTextureManager());
        renderer.EnablePixelCapture(surface_size, surface_size);
    }

    /// <summary>
    /// The reading that matters: a buffered pass composited with <see cref="BlendingMode.Premultiplied"/>
    /// is pixel-identical to drawing the same content directly. That equivalence is what would let
    /// skip the offscreen pass without changing what anyone sees.
    /// </summary>
    [Test]
    public void PremultipliedCompositeMatchesDrawingDirectly()
    {
        var direct = drawDirectly();
        var buffered = drawThroughBuffer(BlendingMode.Premultiplied);

        assertSamePixel(buffered, direct, "a premultiplied composite must be indistinguishable from direct drawing");
    }

    /// <summary>
    /// The defect, asserted so it cannot come back quietly: compositing with
    /// <see cref="BlendingMode.Alpha"/> multiplies by alpha twice.
    /// </summary>
    [Test]
    public void AlphaCompositeDarkensTheResult()
    {
        var direct = drawDirectly();
        var buffered = drawThroughBuffer(BlendingMode.Alpha);

        // The overlap accumulated alpha 0.75 in the buffer, so every color channel contributed by the
        // buffer arrives scaled by 0.75 instead of 1.
        Assert.Multiple(() =>
        {
            Assert.That(buffered.X, Is.EqualTo(direct.X * 0.75f).Within(1e-5f));
            Assert.That(buffered.Y, Is.EqualTo(direct.Y * 0.75f).Within(1e-5f));

            // Blue comes from the background rather than through the buffer, so it is untouched — which is
            // why this reads as a color shift and not merely as "darker".
            Assert.That(buffered.Z, Is.EqualTo(direct.Z).Within(1e-5f));
            Assert.That(buffered.W, Is.EqualTo(direct.W).Within(1e-5f), "coverage is right either way; only RGB is wrong");
        });
    }

    /// <summary>
    /// The exact figures from the plan's table, pinned so the equivalence above cannot be satisfied by two
    /// paths that are equally wrong.
    /// </summary>
    [Test]
    public void TheWorkedExampleProducesItsDocumentedFigures()
    {
        Assert.Multiple(() =>
        {
            assertSamePixel(drawDirectly(), new Vector4(0.25f, 0.5f, 0.25f, 1f), "direct");
            assertSamePixel(drawThroughBuffer(BlendingMode.Premultiplied), new Vector4(0.25f, 0.5f, 0.25f, 1f), "buffered, premultiplied composite");
            assertSamePixel(drawThroughBuffer(BlendingMode.Alpha), new Vector4(0.1875f, 0.375f, 0.25f, 1f), "buffered, alpha composite");
        });
    }

    /// <summary>
    /// Fading a premultiplied composite requires premultiplying the <em>quad color</em> as well, not just
    /// choosing the mode.
    /// </summary>
    /// <remarks>
    /// The fragment shader computes <c>texColor *= v_Color</c> componentwise, so a quad alpha below 1 scales
    /// the sampled alpha but not the sampled RGB. Under a straight-alpha blend that is correct — the blend
    /// applies the source alpha to RGB itself. Under a premultiplied blend nothing else ever will, so
    /// leaving the quad color straight composites full-brightness content over half coverage: a fade that
    /// lightens instead of darkening. This is asserted separately from the end-to-end tests because it is
    /// the one part of the fix that is a property of the shader rather than of the blend equation.
    /// </remarks>
    [Test]
    public void FadingAPremultipliedCompositeNeedsAPremultipliedQuadColor()
    {
        // One opaque child, so the buffer is (1,0,0,1) and a half-opacity composite has one right answer:
        // half the child over half the background.
        var opaque = new Vector4(1, 0, 0, 1);
        var expected = new Vector4(0.5f, 0f, 0.5f, 1f);

        var premultipliedQuad = drawThroughBuffer(BlendingMode.Premultiplied, opaque, opaque, new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
        var straightQuad = drawThroughBuffer(BlendingMode.Premultiplied, opaque, opaque, new Vector4(1f, 1f, 1f, 0.5f));

        Assert.Multiple(() =>
        {
            assertSamePixel(premultipliedQuad, expected, "a premultiplied quad color fades correctly");

            // The straight quad keeps full-strength red while only covering half the pixel.
            Assert.That(straightQuad.X, Is.EqualTo(1f).Within(1e-5f), "a straight quad color does not darken with the fade");
        });
    }

    /// <summary>
    /// A fully opaque subtree composites correctly under either mode — alpha 1 applied twice is still 1.
    /// </summary>
    [Test]
    public void OpaqueContentIsUnaffectedByTheCompositeMode()
    {
        var opaque = new Vector4(1, 0, 0, 1);

        var withAlpha = drawThroughBuffer(BlendingMode.Alpha, opaque, opaque);
        var withPremultiplied = drawThroughBuffer(BlendingMode.Premultiplied, opaque, opaque);

        assertSamePixel(withAlpha, withPremultiplied, "an opaque buffer composites the same either way");
    }

    /// <summary>
    /// Draws both children straight onto the background, which is what a plain container does.
    /// </summary>
    private Vector4 drawDirectly()
    {
        var screen = renderer.Screen;
        screen.Clear(background);

        renderer.SetBlendMode(BlendingMode.Alpha);
        renderer.DrawQuads(quad(child_one), renderer.WhitePixel);
        renderer.DrawQuads(quad(child_two), renderer.WhitePixel);

        return centre(screen);
    }

    /// <summary>
    /// Renders both children into an offscreen buffer and composites that buffer onto the background with
    /// <paramref name="compositeMode"/> — the shape of <c>BufferedContainerDrawNode.Draw</c>.
    /// </summary>
    private Vector4 drawThroughBuffer(BlendingMode compositeMode, Vector4? one = null, Vector4? two = null, Vector4? quadColor = null)
    {
        var screen = renderer.Screen;
        screen.Clear(background);

        var rect = new RectangleF(0, 0, surface_size, surface_size);

        // 1:1 with the screen so nothing here depends on scaling or filtering.
        using var buffer = renderer.CreateFrameBuffer(surface_size, surface_size);

        // Transparent black, matching BufferedContainer.BackgroundColor's default.
        renderer.BindFrameBuffer(buffer, rect);
        renderer.SetBlendMode(BlendingMode.Alpha);
        renderer.DrawQuads(quad(one ?? child_one), renderer.WhitePixel);
        renderer.DrawQuads(quad(two ?? child_two), renderer.WhitePixel);
        renderer.UnbindFrameBuffer();

        renderer.SetBlendMode(compositeMode);
        renderer.DrawQuads(compositeQuad(rect, quadColor ?? new Vector4(1, 1, 1, 1)), buffer.Texture);

        return centre(screen);
    }

    /// <summary>
    /// A quad covering the whole surface in the given color. Both children cover everything, so every
    /// pixel is an overlap pixel and the assertions never land on an edge — the rasterizer models no
    /// partial coverage.
    /// </summary>
    private static SakuraVertex[] quad(Vector4 color)
    {
        return
        [
            vertex(0, 0, color, 0, 0),
            vertex(surface_size, 0, color, 1, 0),
            vertex(surface_size, surface_size, color, 1, 1),
            vertex(0, surface_size, color, 0, 1),
        ];
    }

    /// <summary>
    /// The composite quad, with the V-flipped texture coordinates
    /// <c>BufferedContainerDrawNode.fillQuad</c> emits.
    /// </summary>
    private static SakuraVertex[] compositeQuad(RectangleF rect, Vector4 color)
    {
        return
        [
            vertex(rect.X, rect.Y, color, 0, 1),
            vertex(rect.X + rect.Width, rect.Y, color, 1, 1),
            vertex(rect.X + rect.Width, rect.Y + rect.Height, color, 1, 0),
            vertex(rect.X, rect.Y + rect.Height, color, 0, 0),
        ];
    }

    private static SakuraVertex vertex(float x, float y, Vector4 color, float u, float v) => new SakuraVertex
    {
        Position = new Vector2(x, y),
        Color = color,
        TexCoords = new Vector2(u, v),
        ClipData = new Vector4(0, 0, -1, -1),
    };

    private static Vector4 centre(PixelSurface surface) => surface[surface_size / 2, surface_size / 2];

    private static void assertSamePixel(Vector4 actual, Vector4 expected, string because)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(1e-5f), $"{because}: red");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1e-5f), $"{because}: green");
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(1e-5f), $"{because}: blue");
            Assert.That(actual.W, Is.EqualTo(expected.W).Within(1e-5f), $"{because}: alpha");
        });
    }
}
