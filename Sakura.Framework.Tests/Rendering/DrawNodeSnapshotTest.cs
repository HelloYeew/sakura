// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// Regression tests for draw node snapshot semantics.
/// Related fix : https://github.com/HelloYeew/sakura/pull/104
/// </summary>
[TestFixture]
public class DrawNodeSnapshotTest
{
    private const float tolerance = 0.01f;

    private Container root = null!;
    private Container parent = null!;
    private ExposedBox box = null!;

    [OneTimeSetUp]
    public void InitializeLogger()
    {
        Logger.Initialize();
    }

    [OneTimeTearDown]
    public void ShutdownLogger()
    {
        Logger.Shutdown();
    }

    [SetUp]
    public void SetUp()
    {
        // expectation: parent maps the box to (110, 120) with size (100, 50).
        root = new Container
        {
            Size = new Vector2(800, 600)
        };
        parent = new Container
        {
            Position = new Vector2(100, 100),
            Size = new Vector2(400, 300)
        };
        box = new ExposedBox
        {
            Position = new Vector2(10, 20),
            Size = new Vector2(100, 50)
        };

        parent.Add(box);
        root.Add(parent);

        root.Load();
        root.LoadComplete();
    }

    /// <summary>
    /// Runs one update pass and generates the draw node tree into the given buffer,
    /// mirroring what <c>AppHost.PerformUpdate</c> does per frame.
    /// </summary>
    private DrawNode frame(int bufferIndex)
    {
        root.UpdateSubTree();
        return root.GenerateDrawNodeSubtree(bufferIndex);
    }

    private static DrawNode boxNode(DrawNode rootNode)
        => ((ContainerDrawNode)((ContainerDrawNode)rootNode).Children.Single()).Children.Single();

    private static void assertNodeAt(DrawNode node, float x, float y, float width, float height)
    {
        Assert.Multiple(() =>
        {
            Assert.That(node.DrawRectangle.X, Is.EqualTo(x).Within(tolerance));
            Assert.That(node.DrawRectangle.Y, Is.EqualTo(y).Within(tolerance));
            Assert.That(node.DrawRectangle.Width, Is.EqualTo(width).Within(tolerance));
            Assert.That(node.DrawRectangle.Height, Is.EqualTo(height).Within(tolerance));
        });
    }

    [Test]
    public void TestNodeIsExactSnapshotOfDrawable()
    {
        var node = boxNode(frame(0));

        assertNodeAt(node, 110, 120, 100, 50);

        // Every vertex must match the drawable's current vertices exactly — a snapshot,
        // not an interpolation of any previous state.
        Assert.That(node.Vertices, Has.Length.EqualTo(box.SourceVertices.Length));

        for (int i = 0; i < node.Vertices.Length; i++)
        {
            Assert.That(node.Vertices[i].Position.X, Is.EqualTo(box.SourceVertices[i].Position.X).Within(tolerance), $"Vertex {i} X");
            Assert.That(node.Vertices[i].Position.Y, Is.EqualTo(box.SourceVertices[i].Position.Y).Within(tolerance), $"Vertex {i} Y");
        }
    }

    [Test]
    public void TestPositionJumpIsReflectedImmediately()
    {
        // The "dropdown opens at a stale position for one frame" regression:
        // a discontinuous move must be fully visible on the very next generated node.
        frame(0);

        box.Position = new Vector2(200, 150);

        var node = boxNode(frame(1));
        assertNodeAt(node, 300, 250, 100, 50);
    }

    [Test]
    public void TestPositionJumpAfterIdlePeriodIsReflectedImmediately()
    {
        // Idle for several frames so all buffered nodes hold a long-lived clean state,
        // then jump. The stale state must not bleed into the new node in any buffer.
        for (int i = 0; i < 9; i++)
            frame(i % 3);

        box.Position = new Vector2(0, 0);

        var node = boxNode(frame(0));
        assertNodeAt(node, 100, 100, 100, 50);
    }

    [Test]
    public void TestAllBuffersConvergeAfterSingleInvalidation()
    {
        // Prime all three buffers with the initial state.
        frame(0);
        frame(1);
        frame(2);

        // A single invalidation, then three frames with no further changes.
        // Each buffer's node must catch up via the InvalidationID check even though
        // the drawable is clean by the time buffers 1 and 2 regenerate.
        box.Position = new Vector2(50, 60);

        var n0 = boxNode(frame(0));
        var n1 = boxNode(frame(1));
        var n2 = boxNode(frame(2));

        assertNodeAt(n0, 150, 160, 100, 50);
        assertNodeAt(n1, 150, 160, 100, 50);
        assertNodeAt(n2, 150, 160, 100, 50);
    }

    [Test]
    public void TestCleanNodeIsReusedWithoutReapplying()
    {
        var first = boxNode(frame(0));
        float xBefore = first.Vertices[0].Position.X;

        var second = boxNode(frame(0));

        Assert.That(second, Is.SameAs(first), "Clean drawables should reuse their buffered node");
        Assert.That(second.Vertices[0].Position.X, Is.EqualTo(xBefore).Within(tolerance));
    }

    [Test]
    public void TestNodeIsIsolatedFromLaterDrawableMutations()
    {
        // A node handed to the draw thread must never change until it is explicitly
        // reapplied. This guards against sharing the drawable's live vertex array
        // (which the update thread keeps mutating) instead of copying it.
        var staleNode = boxNode(frame(0));
        float xBefore = staleNode.Vertices[0].Position.X;
        float rectXBefore = staleNode.DrawRectangle.X;

        // The host moves on to another buffer while buffer 0 is being drawn.
        box.Position = new Vector2(250, 200);
        var freshNode = boxNode(frame(1));

        Assert.Multiple(() =>
        {
            // Buffer 0's node must still show the old state...
            Assert.That(staleNode.Vertices[0].Position.X, Is.EqualTo(xBefore).Within(tolerance));
            Assert.That(staleNode.DrawRectangle.X, Is.EqualTo(rectXBefore).Within(tolerance));

            // ...while buffer 1's node shows the new state.
            Assert.That(freshNode.DrawRectangle.X, Is.EqualTo(350).Within(tolerance));
            Assert.That(freshNode.DrawRectangle.Y, Is.EqualTo(300).Within(tolerance));
        });
    }

    [Test]
    public void TestParentMoveRepositionsChildNodeInSameFrame()
    {
        // Guards the update-order contract: when a parent's layout changes, its children
        // must be re-invalidated and recomputed within the same update pass, so the child's
        // node is never one frame behind its parent.
        frame(0);

        parent.Position = new Vector2(300, 50);

        var node = boxNode(frame(1));
        assertNodeAt(node, 310, 70, 100, 50);
    }

    [Test]
    public void TestParentResizeRepositionsAnchoredChildInSameFrame()
    {
        // An anchored child's screen position depends on the parent's size. Mirrors the
        // FpsGraph case (BottomRight-anchored content) where a parent size change must not
        // leave the child at a stale position for a frame.
        box.Anchor = Anchor.BottomRight;
        box.Origin = Anchor.BottomRight;
        box.Position = Vector2.Zero;

        frame(0);

        parent.Size = new Vector2(200, 200);

        var node = boxNode(frame(1));

        // Parent content area is now 200x200 at (100, 100); the box hugs its bottom-right.
        assertNodeAt(node, 200, 250, 100, 50);
    }

    [Test]
    public void TestAlphaChangeIsReflectedInNode()
    {
        var node = boxNode(frame(0));
        Assert.That(node.DrawAlpha, Is.EqualTo(1f).Within(tolerance));

        box.Alpha = 0.5f;

        node = boxNode(frame(1));
        Assert.That(node.DrawAlpha, Is.EqualTo(0.5f).Within(tolerance));
    }

    [Test]
    public void TestNodeInvalidationIdTracksDrawable()
    {
        var node = boxNode(frame(0));
        Assert.That(node.InvalidationID, Is.EqualTo(box.CurrentInvalidationId));

        box.Position = new Vector2(1, 1);
        Assert.That(node.InvalidationID, Is.Not.EqualTo(box.CurrentInvalidationId), "Invalidation should bump the drawable's id before regeneration");

        node = boxNode(frame(0));
        Assert.That(node.InvalidationID, Is.EqualTo(box.CurrentInvalidationId));
    }
}

/// <summary>
/// A <see cref="Box"/> exposing internal state needed to verify snapshot semantics.
/// </summary>
public partial class ExposedBox : Box
{
    public Vertex[] SourceVertices => Vertices;
    public long CurrentInvalidationId => DrawNodeInvalidationId;
}
