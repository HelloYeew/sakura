// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering.Batches;
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
