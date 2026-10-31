// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// The reference rasterizer takes a triangle list as what it is, three vertices a triangle, as the real backends do: read as
/// quads, as it used to be, a drawable of triangles draws the wrong shape.
/// </summary>
[TestFixture]
public class HeadlessTriangleTest
{
    private const int size = 20;

    private static SakuraVertex vertex(float x, float y, float alpha = 1) => new SakuraVertex
    {
        Position = new Vector2(x, y),
        Color = new Vector4(1, 1, 1, alpha)
    };

    private static PixelSurface draw(params SakuraVertex[] vertices)
    {
        var renderer = new HeadlessRenderer(new HeadlessTextureManager());
        renderer.EnablePixelCapture(size, size);
        renderer.Screen.Clear(new Vector4(0, 0, 0, 1));

        renderer.DrawVertices(vertices, renderer.WhitePixel);

        return renderer.Screen;
    }

    [Test]
    public void ATriangleCoversOnlyItsOwnSide()
    {
        var pixels = draw(vertex(0, 0), vertex(20, 0), vertex(0, 20));

        Assert.Multiple(() =>
        {
            Assert.That(pixels[5, 5].X, Is.EqualTo(1).Within(1e-5f), "inside");
            Assert.That(pixels[15, 15].X, Is.Zero, "across its long edge");
        });
    }

    [Test]
    public void EachThreeVerticesAreATriangle()
    {
        var pixels = draw(
            vertex(0, 0), vertex(9, 0), vertex(0, 9),
            vertex(20, 20), vertex(11, 20), vertex(20, 11));

        Assert.Multiple(() =>
        {
            Assert.That(pixels[2, 2].X, Is.EqualTo(1).Within(1e-5f), "the first");
            Assert.That(pixels[17, 17].X, Is.EqualTo(1).Within(1e-5f), "the second");
            Assert.That(pixels[10, 10].X, Is.Zero, "nothing between them");
        });
    }

    [Test]
    public void ColorsInterpolateAcrossIt()
    {
        var pixels = draw(vertex(0, 0, 0), vertex(20, 0, 1), vertex(20, 20, 1), vertex(20, 20, 1), vertex(0, 20, 0), vertex(0, 0, 0));

        Assert.Multiple(() =>
        {
            Assert.That(pixels[1, 10].X, Is.EqualTo(1.5f / size).Within(1e-5f));
            Assert.That(pixels[18, 10].X, Is.EqualTo(18.5f / size).Within(1e-5f));
        });
    }

    [Test]
    public void AnUnfinishedTriangleIsNotDrawn() =>
        Assert.That(draw(vertex(0, 0), vertex(20, 0))[1, 1].X, Is.Zero);
}
