// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// The drawable side of vertex storage: how many vertices a drawable declares, and whether growing and
/// shrinking that count reallocates. <see cref="DrawNode"/> snapshots exactly the declared count, so a
/// drawable whose array is larger than its count must not leak the stale tail into the draw.
/// </summary>
[TestFixture]
public class SourceVertexStorageTest
{
    private Container root = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        root = new Container { Size = new Vector2(800, 600) };
        root.Load();
        root.CompleteLoad();
    }

    private DrawNode nodeFor(Drawable drawable)
    {
        root.Add(drawable);
        drawable.Load();
        drawable.CompleteLoad();

        root.UpdateSubTree();

        return ((ContainerDrawNode)root.GenerateDrawNodeSubtree(0)).Children.Last();
    }

    /// <summary>
    /// A <see cref="Line"/> is two triangles, so its node must carry six vertices — not the four a plain
    /// drawable declares.
    /// </summary>
    [Test]
    public void ALineSnapshotsSixVertices()
    {
        var line = new Line
        {
            StartPoint = new Vector2(0, 0),
            EndPoint = new Vector2(100, 100),
            Thickness = 4
        };

        var node = nodeFor(line);

        Assert.That(node.VertexCount, Is.EqualTo(6));
    }

    /// <summary>
    /// A <see cref="Triangle"/> is one triangle, so three vertices.
    /// </summary>
    [Test]
    public void ATriangleSnapshotsThreeVertices()
    {
        var triangle = new Triangle { Size = new Vector2(50, 50) };

        var node = nodeFor(triangle);

        Assert.That(node.VertexCount, Is.EqualTo(3));
    }

    /// <summary>
    /// The drawable-side equivalent of the draw node's grow-only storage: a <see cref="Path"/> losing
    /// segments and regaining them must not reallocate. It used to assign a right-sized array on any change,
    /// so an oscillating vertex count allocated in both directions.
    /// </summary>
    [Test]
    public void SourceVertexStorageGrowsButNeverShrinks()
    {
        var path = new Path { Thickness = 2 };

        for (int i = 0; i < 6; i++)
            path.AddVertex(new Vector2(i * 10, i * 10));

        var node = nodeFor(path);
        int grownCount = node.VertexCount;

        Assert.That(grownCount, Is.EqualTo((6 - 1) * 6));

        // Shrink. The node's count must follow the source down...
        path.ClearVertices();

        for (int i = 0; i < 3; i++)
            path.AddVertex(new Vector2(i * 10, i * 10));

        root.UpdateSubTree();
        node = ((ContainerDrawNode)root.GenerateDrawNodeSubtree(0)).Children.Last();

        Assert.That(node.VertexCount, Is.EqualTo((3 - 1) * 6), "the count follows the source, not the capacity");

        // ...and back up to a size already seen.
        path.ClearVertices();

        for (int i = 0; i < 6; i++)
            path.AddVertex(new Vector2(i * 10, i * 10));

        root.UpdateSubTree();
        node = ((ContainerDrawNode)root.GenerateDrawNodeSubtree(0)).Children.Last();

        Assert.That(node.VertexCount, Is.EqualTo(grownCount));
    }

    /// <summary>
    /// A drawable that sizes its array exactly, rather than opting into grow-only storage, must keep working
    /// untouched — its count falls back to the array's length. Several drawables outside this repository do
    /// exactly that, so the fallback is the compatibility guarantee, not an implementation detail.
    /// </summary>
    [Test]
    public void ADrawableThatSizesItsArrayExactlyStillReportsTheRightCount()
    {
        var exact = new ExactlySizedDrawable(7);

        var node = nodeFor(exact);

        Assert.That(node.VertexCount, Is.EqualTo(7));
    }

    private partial class ExactlySizedDrawable : Drawable
    {
        private readonly int count;

        public ExactlySizedDrawable(int count)
        {
            this.count = count;
            Size = new Vector2(10, 10);
        }

        protected internal override VertexTopology Topology => VertexTopology.Triangles;

        protected override void GenerateVertices()
        {
            // Deliberately the old pattern: assign a right-sized array and never touch SetVertexCount.
            if (Vertices.Length != count)
                Vertices = new Vertex[count];

            for (int i = 0; i < count; i++)
                Vertices[i] = new Vertex { Color = new Vector4(1, 1, 1, 1) };
        }
    }

    /// <summary>
    /// Whatever a drawable declares, the vertices reaching the node must be the ones it wrote — not the
    /// zero-initialized contents of an array nobody filled, which would be invisible (alpha 0) rather than
    /// obviously broken.
    /// </summary>
    [Test]
    public void ALinesVerticesAreActuallyPopulated()
    {
        var line = new Line
        {
            StartPoint = new Vector2(0, 0),
            EndPoint = new Vector2(100, 100),
            Thickness = 4
        };

        var node = nodeFor(line);

        bool anyVisible = false;

        for (int i = 0; i < node.Vertices.Length; i++)
        {
            if (node.Vertices[i].Color.W > 0)
                anyVisible = true;
        }

        Assert.That(anyVisible, Is.True, "every vertex reaching the node had alpha 0, so the line draws nothing");
    }
}
