// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// <see cref="Drawable.UpdateVertexAlpha"/>: a fade of a drawable with many vertices scales their alpha rather than building
/// them again, and comes to the same result.
/// </summary>
[TestFixture]
public class VertexAlphaTest
{
    private ManualClock manual = null!;
    private Container root = null!;
    private Mesh mesh = null!;

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

        root.Add(mesh = new Mesh { Size = new Vector2(100) });
        frame();
    }

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    [Test]
    public void AFadeScalesThemWithoutBuildingThem()
    {
        int built = mesh.Built;

        mesh.Alpha = 0.5f;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Built, Is.EqualTo(built), "no new vertices");
            Assert.That(mesh.DrawAlpha, Is.EqualTo(0.5f));
            Assert.That(mesh.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(mesh.Vertices[1].Color.W, Is.EqualTo(0.125f).Within(1e-5f), "a vertex with less of its own keeps its share");
            Assert.That(mesh.Vertices[2].Color.W, Is.Zero, "a clear one stays clear");
        });
    }

    [Test]
    public void AParentsFadeDoesTheSame()
    {
        var parent = new Container { Size = new Vector2(200) };
        var inner = new Mesh { Size = new Vector2(100) };
        parent.Add(inner);
        root.Add(parent);
        frame();
        int built = inner.Built;

        parent.Alpha = 0.25f;
        frame();
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(inner.Built, Is.EqualTo(built));
            Assert.That(inner.Vertices[0].Color.W, Is.EqualTo(0.25f).Within(1e-5f));
        });
    }

    /// <summary>
    /// Scaled again and again it comes back to what it was: 8-bit output can't tell what's left over.
    /// </summary>
    [Test]
    public void ItComesBackAfterAWholeFade()
    {
        for (int cycle = 0; cycle < 20; cycle++)
        {
            for (int step = 100; step >= 1; step--)
            {
                mesh.Alpha = step / 100f;
                frame();
            }

            for (int step = 1; step <= 100; step++)
            {
                mesh.Alpha = step / 100f;
                frame();
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Vertices[0].Color.W, Is.EqualTo(1).Within(1e-3f));
            Assert.That(mesh.Vertices[1].Color.W, Is.EqualTo(0.25f).Within(1e-3f));
        });
    }

    /// <summary>
    /// Faded to nothing, its vertices are left to scale back from. (Hidden, a draw never reads them.)
    /// </summary>
    [Test]
    public void FadedToNothingAndBackBuildsNothing()
    {
        int built = mesh.Built;

        mesh.Alpha = 0;
        frame();
        Assert.That(mesh.DrawAlpha, Is.Zero);

        mesh.Alpha = 0.75f;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Built, Is.EqualTo(built));
            Assert.That(mesh.DrawAlpha, Is.EqualTo(0.75f));
            Assert.That(mesh.Vertices[0].Color.W, Is.EqualTo(0.75f).Within(1e-5f));
        });
    }

    /// <summary>
    /// Kept present through a fade to nothing, it's still not zeroed.
    /// </summary>
    [Test]
    public void AlwaysPresentFadedToNothingAndBackBuildsNothing()
    {
        mesh.AlwaysPresent = true;
        frame();
        int built = mesh.Built;

        mesh.Alpha = 0;
        frame();
        Assert.That(mesh.DrawAlpha, Is.Zero);

        mesh.Alpha = 0.75f;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Built, Is.EqualTo(built));
            Assert.That(mesh.Vertices[0].Color.W, Is.EqualTo(0.75f).Within(1e-5f));
        });
    }

    /// <summary>
    /// Built while it was at nothing, there's nothing in its vertices to scale, so being shown builds them.
    /// </summary>
    [Test]
    public void BuiltAtNothingItBuildsWhenShown()
    {
        var inner = new Mesh { Size = new Vector2(100), Alpha = 0, AlwaysPresent = true };
        root.Add(inner);
        frame();
        Assert.That(inner.Vertices[0].Color.W, Is.Zero);
        int built = inner.Built;

        inner.Alpha = 0.5f;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(inner.Built, Is.EqualTo(built + 1));
            Assert.That(inner.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f));
        });
    }

    [Test]
    public void ANewColorBuildsThem()
    {
        int built = mesh.Built;

        mesh.Color = Color.Red;
        frame();

        Assert.That(mesh.Built, Is.EqualTo(built + 1));
    }

    [Test]
    public void AFadeAfterANewColorIsAFadeAgain()
    {
        mesh.Color = Color.Red;
        frame();
        int built = mesh.Built;

        mesh.Alpha = 0.5f;
        frame();

        Assert.That(mesh.Built, Is.EqualTo(built));
    }

    /// <summary>
    /// A change to its color and its fade in one frame is built with both.
    /// </summary>
    [Test]
    public void ANewColorAndAFadeTogether()
    {
        mesh.Color = Color.White.WithAlpha(0.5f);
        mesh.Alpha = 0.5f;
        frame();

        Assert.That(mesh.Vertices[0].Color.W, Is.EqualTo(0.25f).Within(1e-2f));
    }

    /// <summary>
    /// Moved, it builds them, at the fade it's at; and a fade after that scales from there.
    /// </summary>
    [Test]
    public void BuiltPartWayThroughAFade()
    {
        mesh.Alpha = 0.5f;
        frame();

        mesh.Position = new Vector2(10, 0);
        frame();
        Assert.That(mesh.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f));

        int built = mesh.Built;
        mesh.Alpha = 1;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Built, Is.EqualTo(built));
            Assert.That(mesh.Vertices[0].Color.W, Is.EqualTo(1).Within(1e-5f));
        });
    }

    [Test]
    public void EveryDrawBufferGetsIt()
    {
        for (int buffer = 0; buffer < 3; buffer++)
            root.GenerateDrawNodeSubtree(buffer);

        mesh.Alpha = 0.5f;
        frame();

        for (int buffer = 0; buffer < 3; buffer++)
        {
            DrawNode node = ((ContainerDrawNode)root.GenerateDrawNodeSubtree(buffer)).Children[0];

            Assert.Multiple(() =>
            {
                Assert.That(node.DrawAlpha, Is.EqualTo(0.5f), $"Buffer {buffer}");
                Assert.That(node.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f), $"Buffer {buffer}");
            });
        }
    }

    [Test]
    public void APathFadesAndRecolorsLikeIt()
    {
        var path = new Path { Thickness = 4 };
        path.AddVertex(new Vector2(0, 0));
        path.AddVertex(new Vector2(100, 0));
        path.AddVertex(new Vector2(100, 100));
        root.Add(path);
        frame();

        path.Alpha = 0.5f;
        frame();
        Assert.That(path.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f));

        path.Color = Color.Red;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(path.Vertices[0].Color.X, Is.EqualTo(1).Within(1e-5f));
            Assert.That(path.Vertices[0].Color.Y, Is.Zero);
            Assert.That(path.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f));
        });
    }

    [Test]
    public void RingArcAndPlotCanvasAndParticleFieldFadeWithoutBuilding()
    {
        var ring = new CountedRing { Size = new Vector2(100) };

        var canvas = new CountedCanvas { RelativeSizeAxes = Axes.None, Size = new Vector2(100) };
        canvas.Dot(new Vector2(50, 50), 10, Color.White);

        var field = new CountedField { RelativeSizeAxes = Axes.None, Size = new Vector2(100) };
        field.Add(new Particle(new Vector2(50, 50), new Vector2(10), 0, Color.White, 1));

        root.AddRange(new Drawable[] { ring, canvas, field });
        frame();

        int ringBuilt = ring.Built, canvasBuilt = canvas.Built, fieldBuilt = field.Built;

        ring.Alpha = canvas.Alpha = field.Alpha = 0.5f;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(ring.Built, Is.EqualTo(ringBuilt));
            Assert.That(canvas.Built, Is.EqualTo(canvasBuilt));
            Assert.That(field.Built, Is.EqualTo(fieldBuilt));
            Assert.That(ring.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(canvas.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(field.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f));
        });
    }

    /// <summary>
    /// Three vertices: one with all of the drawable's alpha, one a quarter, and one clear.
    /// </summary>
    private partial class Mesh : Drawable
    {
        public int Built;

        protected internal override VertexTopology Topology => VertexTopology.Triangles;

        protected override void UpdateDrawColor() => UpdateVertexAlpha();

        protected override void GenerateVertices()
        {
            Built++;
            SetVertexCount(3);

            float alpha = DrawAlpha * (Color.A / 255f);

            Vertices[0] = new SakuraVertex { Color = new Vector4(1, 1, 1, alpha) };
            Vertices[1] = new SakuraVertex { Color = new Vector4(1, 1, 1, alpha * 0.25f) };
            Vertices[2] = new SakuraVertex { Color = new Vector4(1, 1, 1, 0) };
        }
    }

    private partial class CountedRing : RingArc
    {
        public int Built;

        protected override void GenerateVertices()
        {
            Built++;
            base.GenerateVertices();
        }
    }

    private partial class CountedCanvas : PlotCanvas
    {
        public int Built;

        protected override void GenerateVertices()
        {
            Built++;
            base.GenerateVertices();
        }
    }

    private partial class CountedField : ParticleField
    {
        public int Built;

        protected override void GenerateVertices()
        {
            Built++;
            base.GenerateVertices();
        }
    }
}
