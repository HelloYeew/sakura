// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Graphics;

[TestFixture]
public class FeatheredStripTest
{
    private static readonly Vector4 red = new Vector4(1, 0, 0, 0.8f);
    private static readonly Vector4 blue = new Vector4(0, 0, 1, 0.4f);

    [Test]
    public void AWideSectionFadesEitherSideOfItsEdges()
    {
        var section = new StripSection(new Vector2(0, 0), new Vector2(10, 0), 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(section.AOuter, Is.EqualTo(new Vector2(-0.5f, 0)));
            Assert.That(section.AInner, Is.EqualTo(new Vector2(0.5f, 0)));
            Assert.That(section.BInner, Is.EqualTo(new Vector2(9.5f, 0)));
            Assert.That(section.BOuter, Is.EqualTo(new Vector2(10.5f, 0)));
            Assert.That(section.Alpha, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Thinner than its feather it dims rather than drawing wider than it is: its inner points meet in the middle.
    /// </summary>
    [Test]
    public void AThinSectionDims()
    {
        var section = new StripSection(new Vector2(0, 0), new Vector2(0.5f, 0), 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(section.AInner, Is.EqualTo(new Vector2(0.25f, 0)));
            Assert.That(section.BInner, Is.EqualTo(new Vector2(0.25f, 0)));
            Assert.That(section.Alpha, Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void ASectionWithNoWidthIsClearAndHasNoNaN()
    {
        var section = new StripSection(new Vector2(3, 3), new Vector2(3, 3), 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(section.Alpha, Is.Zero);
            Assert.That(section.AOuter, Is.EqualTo(new Vector2(3, 3)));
            Assert.That(section.BOuter, Is.EqualTo(new Vector2(3, 3)));
        });
    }

    [Test]
    public void NoFeatherIsJustItsEdges()
    {
        var section = new StripSection(new Vector2(0, 0), new Vector2(0, 4), 0);

        Assert.Multiple(() =>
        {
            Assert.That(section.AOuter, Is.EqualTo(section.A));
            Assert.That(section.BOuter, Is.EqualTo(section.B));
            Assert.That(section.Alpha, Is.EqualTo(1));
        });
    }

    [Test]
    public void APlainPieceIsOneQuad()
    {
        var vertices = new Vertex[FeatheredStrip.VerticesPerPiece(false)];
        var from = new StripSection(new Vector2(0, 0), new Vector2(0, 10), 0);
        var to = new StripSection(new Vector2(20, 0), new Vector2(20, 10), 0);

        int end = FeatheredStrip.Write(vertices, 0, from, to, red, blue, false);

        Assert.Multiple(() =>
        {
            Assert.That(end, Is.EqualTo(6));
            Assert.That(vertices[0].Position, Is.EqualTo(from.A));
            Assert.That(vertices[1].Position, Is.EqualTo(from.B));
            Assert.That(vertices[2].Position, Is.EqualTo(to.B));
            Assert.That(vertices[4].Position, Is.EqualTo(to.A));
            Assert.That(vertices[0].Color, Is.EqualTo(red), "starts in the one color");
            Assert.That(vertices[2].Color, Is.EqualTo(blue), "ends in the other");
        });
    }

    /// <summary>
    /// A smooth piece is three quads, clear at its outer points and solid inside them.
    /// </summary>
    [Test]
    public void ASmoothPieceIsClearAtItsEdges()
    {
        var vertices = new Vertex[FeatheredStrip.VerticesPerPiece(true) + 3];
        var from = new StripSection(new Vector2(0, 0), new Vector2(0, 10), 0.5f);
        var to = new StripSection(new Vector2(20, 0), new Vector2(20, 10), 0.5f);

        int end = FeatheredStrip.Write(vertices, 3, from, to, red, true);

        Assert.Multiple(() =>
        {
            Assert.That(end, Is.EqualTo(21), "from where it was told to start");
            Assert.That(vertices[3].Position, Is.EqualTo(from.AOuter));
            Assert.That(vertices[3].Color.W, Is.Zero, "clear outside");
            Assert.That(vertices[4].Position, Is.EqualTo(from.AInner));
            Assert.That(vertices[4].Color.W, Is.EqualTo(red.W), "solid inside");
            Assert.That(vertices[3].Color.X, Is.EqualTo(red.X), "its color all the way out, so the edge doesn't darken");
            Assert.That(vertices[20].Color.W, Is.EqualTo(red.W), "and solid through to the far side");
        });
    }

    [Test]
    public void AThinPieceDimsItsMiddle()
    {
        var vertices = new Vertex[FeatheredStrip.VerticesPerPiece(true)];
        var from = new StripSection(new Vector2(0, 0), new Vector2(0, 0.5f), 0.5f);
        var to = new StripSection(new Vector2(20, 0), new Vector2(20, 0.5f), 0.5f);

        FeatheredStrip.Write(vertices, 0, from, to, red, true);

        Assert.That(vertices[1].Color.W, Is.EqualTo(red.W * 0.5f).Within(1e-6f));
    }

    [Test]
    public void ItWritesNoFurtherThanItSays()
    {
        var vertices = new Vertex[FeatheredStrip.VerticesPerPiece(true)];
        var from = new StripSection(new Vector2(0, 0), new Vector2(0, 10), 0.5f);
        var to = new StripSection(new Vector2(20, 0), new Vector2(20, 10), 0.5f);

        Assert.DoesNotThrow(() => FeatheredStrip.Write(vertices, 0, from, to, red, true));
        Assert.Throws<IndexOutOfRangeException>(() => FeatheredStrip.Write(vertices, 1, from, to, red, true), "a span too short isn't written past");
    }
}
