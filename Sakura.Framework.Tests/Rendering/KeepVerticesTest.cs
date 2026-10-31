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

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// <see cref="Drawable.KeepVertices"/>, and <see cref="PlotCanvas"/> using it: a parent's layout changing without moving a
/// drawable, as a mask growing over a graph does, builds nothing and copies nothing to the draw nodes.
/// </summary>
[TestFixture]
public class KeepVerticesTest
{
    private ManualClock manual = null!;
    private Container root = null!;
    private Container mask = null!;
    private CountedCanvas canvas = null!;

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

        canvas = new CountedCanvas { RelativeSizeAxes = Axes.None, Size = new Vector2(400, 100) };
        canvas.Box(new RectangleF(0, 0, 400, 100), Color.White);
        canvas.Dot(new Vector2(200, 50), 10, Color.Red);

        root.Add(mask = new Container { Size = new Vector2(100, 100), Masking = true, Child = canvas });

        frame();
        generateAll();
    }

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    private void generateAll()
    {
        for (int buffer = 0; buffer < 3; buffer++)
            root.GenerateDrawNodeSubtree(buffer);
    }

    private DrawNode nodeIn(int buffer) => ((ContainerDrawNode)((ContainerDrawNode)root.GenerateDrawNodeSubtree(buffer)).Children[0]).Children[0];

    [Test]
    public void AGrowingMaskBuildsNothingAndCopiesNothing()
    {
        int generated = canvas.Generated;
        int built = canvas.BuiltMarks;
        long version = canvas.VertexVersion;

        for (int width = 110; width < 400; width += 10)
        {
            mask.Width = width;
            frame();
            generateAll();
        }

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Generated, Is.GreaterThan(generated), "the mask's growth does reach it");
            Assert.That(canvas.BuiltMarks, Is.EqualTo(built), "and finds nothing to build");
            Assert.That(canvas.VertexVersion, Is.EqualTo(version), "and nothing for a draw node to copy");
        });
    }

    /// <summary>
    /// Nothing copied, every buffer still has them, with the state the mask changed.
    /// </summary>
    [Test]
    public void TheDrawNodesStillHaveThem()
    {
        int count = canvas.VertexCount;

        mask.Width = 300;
        frame();

        for (int buffer = 0; buffer < 3; buffer++)
        {
            DrawNode node = nodeIn(buffer);

            Assert.Multiple(() =>
            {
                Assert.That(node.VertexCount, Is.EqualTo(count), $"Buffer {buffer}");
                Assert.That(node.Vertices[0].Position, Is.EqualTo(canvas.Vertices[0].Position), $"Buffer {buffer}");
            });
        }
    }

    [Test]
    public void MovingItBuildsThem()
    {
        int built = canvas.BuiltMarks;
        long version = canvas.VertexVersion;

        mask.X = 50;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(canvas.BuiltMarks, Is.GreaterThan(built));
            Assert.That(canvas.VertexVersion, Is.GreaterThan(version));
            Assert.That(nodeIn(0).Vertices[0].Position.X, Is.EqualTo(50).Within(0.01f), "and the draw node gets them");
        });
    }

    /// <summary>
    /// A move too small to build for on its own is still built for once it adds up: it's measured from what was built.
    /// </summary>
    [Test]
    public void AVerySlowMoveStillGetsBuilt()
    {
        int built = canvas.BuiltMarks;

        for (int i = 1; i <= 10; i++)
        {
            mask.X = i * 0.004f;
            frame();
        }

        Assert.Multiple(() =>
        {
            Assert.That(canvas.BuiltMarks, Is.GreaterThan(built));
            Assert.That(canvas.Vertices[0].Position.X, Is.EqualTo(0.04f).Within(0.0101f), "never further behind than the tolerance");
        });
    }

    [Test]
    public void ScalingAParentBuildsThem()
    {
        int built = canvas.BuiltMarks;

        mask.Scale = new Vector2(2);
        frame();

        Assert.That(canvas.BuiltMarks, Is.GreaterThan(built));
    }

    [Test]
    public void ItsOwnSizeBuildsThem()
    {
        int built = canvas.BuiltMarks;

        canvas.Width = 500;
        frame();

        Assert.That(canvas.BuiltMarks, Is.GreaterThan(built));
    }

    /// <summary>
    /// Twice the size at half the scale is the same matrix, but its marks are in its own units, so they've moved.
    /// </summary>
    [Test]
    public void ASizeAndScaleThatCancelOutBuildThem()
    {
        int built = canvas.BuiltMarks;

        canvas.Size = new Vector2(800, 200);
        canvas.Scale = new Vector2(0.5f);
        frame();

        Assert.That(canvas.BuiltMarks, Is.GreaterThan(built));
    }

    [Test]
    public void NewMarksBuildThem()
    {
        int built = canvas.BuiltMarks;
        int count = canvas.VertexCount;

        canvas.Box(new RectangleF(0, 0, 10, 10), Color.Lime);
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(canvas.BuiltMarks, Is.GreaterThan(built));
            Assert.That(canvas.VertexCount, Is.EqualTo(count + 6));
            Assert.That(nodeIn(0).VertexCount, Is.EqualTo(count + 6), "and the draw node gets them");
        });
    }

    [Test]
    public void NewMarksUnderAGrowingMaskBuildThem()
    {
        int built = canvas.BuiltMarks;

        canvas.Box(new RectangleF(0, 0, 10, 10), Color.Lime);
        mask.Width = 300;
        frame();

        Assert.That(canvas.BuiltMarks, Is.GreaterThan(built));
    }

    [Test]
    public void ANewColorBuildsThem()
    {
        int built = canvas.BuiltMarks;

        canvas.Color = Color.White.WithAlpha(0.5f);
        mask.Width = 300;
        frame();

        Assert.That(canvas.BuiltMarks, Is.GreaterThan(built));
    }

    /// <summary>
    /// A fade and a growing mask together: the vertices are kept, and scaled, and the draw nodes get the new alpha.
    /// </summary>
    [Test]
    public void AFadeUnderAGrowingMaskScalesThem()
    {
        int built = canvas.BuiltMarks;
        long version = canvas.VertexVersion;

        canvas.Alpha = 0.5f;
        mask.Width = 300;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(canvas.BuiltMarks, Is.EqualTo(built));
            Assert.That(canvas.VertexVersion, Is.GreaterThan(version), "they moved, so there's something to copy");
            Assert.That(canvas.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f));
        });

        for (int buffer = 0; buffer < 3; buffer++)
            Assert.That(nodeIn(buffer).Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f), $"Buffer {buffer}");
    }

    /// <summary>
    /// A buffer that missed a change (it isn't generated every frame) still catches up: a version is per node.
    /// </summary>
    [Test]
    public void ABufferThatMissedAChangeCatchesUp()
    {
        canvas.Alpha = 0.5f;
        frame();
        root.GenerateDrawNodeSubtree(0);

        mask.Width = 300;
        frame();
        root.GenerateDrawNodeSubtree(0);

        Assert.That(nodeIn(1).Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f));
    }

    /// <summary>
    /// A fade with nothing to change in the vertices (going to nothing) has nothing to copy, and a draw at 0 draws nothing.
    /// </summary>
    [Test]
    public void FadingToNothingCopiesNothing()
    {
        canvas.AlwaysPresent = true;
        frame();
        long version = canvas.VertexVersion;

        canvas.Alpha = 0;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(canvas.VertexVersion, Is.EqualTo(version));
            Assert.That(nodeIn(0).DrawAlpha, Is.Zero);
        });
    }

    /// <summary>
    /// Built at nothing there's no alpha to scale from, so it builds when shown, under a growing mask or not.
    /// </summary>
    [Test]
    public void BuiltAtNothingItBuildsWhenShown()
    {
        var hidden = new CountedCanvas { RelativeSizeAxes = Axes.None, Size = new Vector2(100), Alpha = 0, AlwaysPresent = true };
        hidden.Box(new RectangleF(0, 0, 100, 100), Color.White);

        root.Add(new Container { Size = new Vector2(100), Masking = true, Child = hidden });
        frame();

        Assert.That(hidden.Vertices[0].Color.W, Is.Zero);

        int built = hidden.BuiltMarks;
        hidden.Alpha = 0.5f;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(hidden.BuiltMarks, Is.GreaterThan(built));
            Assert.That(hidden.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(1e-5f));
        });
    }

    /// <summary>
    /// Keeping is what a drawable asks for: one that doesn't builds every time, as they always have, and its draw nodes copy.
    /// </summary>
    [Test]
    public void ADrawableThatDoesNotAskStillBuilds()
    {
        var ring = new CountedRing { Size = new Vector2(100) };
        root.Add(new Container { Size = new Vector2(100), Masking = true, Child = ring });
        frame();

        Container holder = (Container)root.Children[^1];
        int built = ring.Built;
        long version = ring.VertexVersion;

        holder.Width = 200;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(ring.Built, Is.GreaterThan(built));
            Assert.That(ring.VertexVersion, Is.GreaterThan(version));
        });
    }

    /// <summary>
    /// A plain drawable's color still reaches its draw nodes, on the default path.
    /// </summary>
    [Test]
    public void ABoxsNewColorReachesItsDrawNodes()
    {
        var box = new Box { Size = new Vector2(10) };
        root.Add(box);
        frame();
        generateAll();

        box.Color = Color.Red;
        frame();

        for (int buffer = 0; buffer < 3; buffer++)
        {
            var rootNode = (ContainerDrawNode)root.GenerateDrawNodeSubtree(buffer);
            Assert.That(rootNode.Children[^1].Vertices[0].Color.Y, Is.Zero, $"Buffer {buffer}");
        }
    }

    private partial class CountedCanvas : PlotCanvas
    {
        /// <summary>
        /// How many times it was asked to generate its vertices.
        /// </summary>
        public int Generated;

        /// <summary>
        /// How many of those wrote them.
        /// </summary>
        public int BuiltMarks;

        protected override void GenerateVertices()
        {
            Generated++;

            // A marker: building writes over it, keeping leaves it.
            Vector2 first = Vertices[0].Position;
            Vertices[0].Position = new Vector2(float.NaN);

            base.GenerateVertices();

            if (float.IsNaN(Vertices[0].Position.X))
                Vertices[0].Position = first;
            else
                BuiltMarks++;
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
}
