// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering.Direct3D11;
using Sakura.Framework.Graphics.Rendering.Vertex;

namespace Sakura.Framework.Tests.Rendering;

[TestFixture]
public class RingCursorTest
{
    [Test]
    public void FirstWrite_Discards()
    {
        var cursor = new RingCursor(10, 40);

        Assert.That(cursor.Allocate(3, out bool discard), Is.EqualTo(0));
        Assert.That(discard, Is.True, "a new buffer's first map must be a discard");
    }

    [Test]
    public void WritesThatFit_FollowEachOther_WithoutDiscarding()
    {
        var cursor = new RingCursor(10, 40);
        cursor.Allocate(3, out _);

        Assert.That(cursor.Allocate(4, out bool second), Is.EqualTo(3));
        Assert.That(second, Is.False);

        Assert.That(cursor.Allocate(3, out bool third), Is.EqualTo(7));
        Assert.That(third, Is.False, "exactly filling the ring still fits");
    }

    [Test]
    public void WriteThatDoesNotFit_DiscardsAndStartsAtTheFront()
    {
        var cursor = new RingCursor(10, 40);
        cursor.Allocate(8, out _);

        Assert.That(cursor.Allocate(3, out bool discard), Is.EqualTo(0));
        Assert.That(discard, Is.True);
    }

    [Test]
    public void FramesCarryOn_FromWhereTheLastOneStopped()
    {
        var cursor = new RingCursor(10, 40);
        cursor.Allocate(4, out _);
        cursor.EndFrame();

        Assert.That(cursor.Allocate(4, out bool discard), Is.EqualTo(4));
        Assert.That(discard, Is.False);
    }

    /// <summary>
    /// A frame that needs less than the ring wraps at most once, wherever the last frame left off.
    /// </summary>
    [Test]
    public void FramesSmallerThanTheRing_DoNotGrowIt()
    {
        var cursor = new RingCursor(10, 40);

        for (int frame = 0; frame < 5; frame++)
        {
            cursor.Allocate(4, out _);
            cursor.Allocate(4, out _);

            Assert.That(cursor.EndFrame(), Is.False);
        }

        Assert.That(cursor.Capacity, Is.EqualTo(10));
    }

    [Test]
    public void FramesLargerThanTheRing_GrowIt()
    {
        var cursor = new RingCursor(10, 40);
        bool grew = false;

        for (int frame = 0; frame < 3 && !grew; frame++)
        {
            cursor.Allocate(6, out _);
            cursor.Allocate(6, out _);
            grew = cursor.EndFrame();
        }

        Assert.That(grew, Is.True);
        Assert.That(cursor.Capacity, Is.EqualTo(20));
    }

    [Test]
    public void TwoWrapsInAFrame_DoublesTheRing()
    {
        var cursor = new RingCursor(10, 40);
        cursor.Allocate(6, out _);

        cursor.Allocate(6, out _); // wrap 1
        cursor.Allocate(6, out _); // wrap 2

        Assert.That(cursor.EndFrame(), Is.True);
        Assert.That(cursor.Capacity, Is.EqualTo(20));

        Assert.That(cursor.Allocate(1, out bool discard), Is.EqualTo(0));
        Assert.That(discard, Is.True, "the recreated buffer's first map must be a discard");
    }

    [Test]
    public void TheFirstDiscard_IsNotAWrap()
    {
        var cursor = new RingCursor(10, 40);
        cursor.Allocate(6, out _); // the new buffer's discard
        cursor.Allocate(6, out _); // wrap 1

        Assert.That(cursor.EndFrame(), Is.False);
    }

    [Test]
    public void Growth_StopsAtTheMaximum()
    {
        var cursor = new RingCursor(10, 25);

        for (int frame = 0; frame < 4; frame++)
        {
            for (int i = 0; i < 8; i++)
                cursor.Allocate(cursor.Capacity / 2 + 1, out _);

            cursor.EndFrame();
        }

        Assert.That(cursor.Capacity, Is.EqualTo(25));
    }

    [Test]
    public void WriteLargerThanTheRing_Throws()
    {
        var cursor = new RingCursor(10, 40);

        Assert.That(() => cursor.Allocate(11, out _), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>
    /// A tiny ring driven through thousands of writes of every size: no write may run past the end, or
    /// land on anything written since the last discard, which a draw still in flight may be reading.
    /// A mistake here shows up on screen as stretched triangles, never as an error.
    /// </summary>
    [Test]
    public void TinyRing_NeverOverwritesAnythingSinceTheLastDiscard()
    {
        var random = new Random(1234);
        var cursor = new RingCursor(7, 7);
        var written = new List<(int Start, int End)>();

        for (int i = 0; i < 10_000; i++)
        {
            int count = random.Next(1, cursor.Capacity + 1);
            int start = cursor.Allocate(count, out bool discard);
            int end = start + count;

            if (discard)
                written.Clear();

            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            Assert.That(end, Is.LessThanOrEqualTo(cursor.Capacity));

            foreach (var (otherStart, otherEnd) in written)
                Assert.That(start >= otherEnd || end <= otherStart, $"write {i} [{start}, {end}) overlaps [{otherStart}, {otherEnd})");

            written.Add((start, end));

            if (random.Next(8) == 0)
                cursor.EndFrame();
        }
    }

    /// <summary>
    /// The vertex ring places vertices by the managed size of the struct, and the input layout steps
    /// through them by <see cref="Vertex.Size"/>. A draw's base vertex only lands on its first vertex
    /// when the two agree.
    /// </summary>
    [Test]
    public void VertexStride_MatchesTheManagedSize()
    {
        Assert.That(Unsafe.SizeOf<Vertex>(), Is.EqualTo(Vertex.Size));
    }
}
