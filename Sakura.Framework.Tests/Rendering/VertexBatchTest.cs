// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering.Batches;
using Sakura.Framework.Maths;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// A batch flushing when it's full: an indexed backend draws a flush from <see cref="VertexBatch.Indices"/> where
/// <see cref="VertexBatch.HasNonQuad"/> says so, and from a fixed pattern of quads where it doesn't.
/// </summary>
[TestFixture]
public class VertexBatchTest
{
    private const int capacity = 12;

    private VertexBatch batch = null!;
    private readonly List<(bool HasNonQuad, int Vertices, int Indices)> flushes = new List<(bool, int, int)>();

    [SetUp]
    public void SetUp()
    {
        flushes.Clear();
        batch = new VertexBatch(capacity, () =>
        {
            flushes.Add((batch.HasNonQuad, batch.VertexCount, batch.IndexCount));
            batch.Reset();
        });
    }

    /// <summary>
    /// A list longer than the batch goes over several flushes, and is a triangle list in all of them. Read as quads, the
    /// part after the first flush joins unrelated triangles up.
    /// </summary>
    [Test]
    public void ATriangleListStaysOneInEveryFlush()
    {
        batch.AddRange(new SakuraVertex[30], 0f);

        bool lastIsNonQuad = batch.HasNonQuad;
        int lastVertices = batch.VertexCount;

        Assert.Multiple(() =>
        {
            Assert.That(flushes, Has.Count.EqualTo(2));

            foreach (var flush in flushes)
            {
                Assert.That(flush.HasNonQuad, "a flush along the way");
                Assert.That(flush.Vertices, Is.EqualTo(capacity));
                Assert.That(flush.Indices, Is.EqualTo(capacity));
            }

            Assert.That(lastIsNonQuad, "what's left over");
            Assert.That(lastVertices, Is.EqualTo(6));
        });
    }

    /// <summary>
    /// A list doesn't carry over past its last flush: quads alone go back to being quads.
    /// </summary>
    [Test]
    public void QuadsAfterAFlushAreQuadsAgain()
    {
        batch.AddRange(new SakuraVertex[12], 0f);
        batch.AddQuad(new SakuraVertex[4], 0f);

        Assert.Multiple(() =>
        {
            Assert.That(flushes, Has.Count.EqualTo(1));
            Assert.That(flushes[0].HasNonQuad);
            Assert.That(batch.HasNonQuad, Is.False);
        });
    }

    /// <summary>
    /// A list that fits never flushes.
    /// </summary>
    [Test]
    public void AListThatFitsDoesntFlush()
    {
        batch.AddRange(new SakuraVertex[12], 0f);

        Assert.Multiple(() =>
        {
            Assert.That(flushes, Is.Empty);
            Assert.That(batch.HasNonQuad);
        });
    }

    /// <summary>
    /// Quads alone write no indices, as the backend draws them from its fixed pattern. A list arriving after them needs
    /// the batch's own indices, so the quads already there get theirs then, in the same pattern.
    /// </summary>
    [Test]
    public void QuadsGetTheirIndicesWhenAListArrives()
    {
        batch.AddQuad(new SakuraVertex[4], 0f);
        batch.AddQuad(new SakuraVertex[4], 0f);
        batch.AddRange(new SakuraVertex[3], 0f);

        Assert.That(batch.Indices.ToArray(), Is.EqualTo(new uint[] { 0, 1, 2, 2, 3, 0, 4, 5, 6, 6, 7, 4, 8, 9, 10 }));
    }

    /// <summary>
    /// A quad after a list, in the same flush, writes its indices as it's added.
    /// </summary>
    [Test]
    public void AQuadAfterAListWritesItsIndices()
    {
        batch.AddRange(new SakuraVertex[3], 0f);
        batch.AddQuad(new SakuraVertex[4], 0f);

        Assert.That(batch.Indices.ToArray(), Is.EqualTo(new uint[] { 0, 1, 2, 3, 4, 5, 5, 6, 3 }));
    }

    /// <summary>
    /// Quads alone still count the indices the backend draws, though they write none.
    /// </summary>
    [Test]
    public void QuadsAloneCountTheirIndices()
    {
        batch.AddQuad(new SakuraVertex[4], 0f);
        batch.AddQuad(new SakuraVertex[4], 0f);

        Assert.Multiple(() =>
        {
            Assert.That(batch.IndexCount, Is.EqualTo(12));
            Assert.That(batch.HasNonQuad, Is.False);
        });
    }

    /// <summary>
    /// A vertex takes its position, color and texture coordinate from the node, and its texture slot and clip from the
    /// batch, whatever the node held in those.
    /// </summary>
    [Test]
    public void AVertexTakesItsOwnFieldsFromTheNodeAndTheRestFromTheBatch()
    {
        var node = new SakuraVertex
        {
            Position = new Vector2(1, 2),
            Color = new Vector4(0.1f, 0.2f, 0.3f, 0.4f),
            TexCoords = new Vector2(0.5f, 0.75f),
            TexIndex = 9,
            ClipData = new Vector4(9, 9, 9, 9),
            ClipShearX = 9,
            ClipRadius = 9,
            ClipFadeEdges = new Vector4(9, 9, 9, 9),
            ClipFadeLength = new Vector4(9, 9, 9, 9)
        };
        var clip = new Vector4(10, 20, 30, 40);

        batch.AddQuad(new[] { node, node, node, node }, 3f, clip, 0.25f, 4f);

        var written = batch.Vertices[0];

        Assert.Multiple(() =>
        {
            Assert.That(written.Position, Is.EqualTo(node.Position));
            Assert.That(written.Color, Is.EqualTo(node.Color));
            Assert.That(written.TexCoords, Is.EqualTo(node.TexCoords));
            Assert.That(written.TexIndex, Is.EqualTo(3f));
            Assert.That(written.ClipData, Is.EqualTo(clip));
            Assert.That(written.ClipShearX, Is.EqualTo(0.25f));
            Assert.That(written.ClipRadius, Is.EqualTo(4f));
            Assert.That(written.ClipFadeEdges, Is.EqualTo(default(Vector4)));
            Assert.That(written.ClipFadeLength, Is.EqualTo(default(Vector4)));
        });
    }

    /// <summary>
    /// An empty list says nothing about what's there.
    /// </summary>
    [Test]
    public void AnEmptyListLeavesQuadsAlone()
    {
        batch.AddQuad(new SakuraVertex[4], 0f);
        batch.AddRange(ReadOnlySpan<SakuraVertex>.Empty, 0f);

        Assert.That(batch.HasNonQuad, Is.False);
    }
}
