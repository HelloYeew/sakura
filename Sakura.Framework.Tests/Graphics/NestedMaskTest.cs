// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// What a mask inside another one clips to. A vertex carries one shape, so the other mask goes along as straight edges:
/// a shape cut by the one around it stays the shape it is, rather than growing its corners where it was cut.
/// </summary>
[TestFixture]
public class ClipStackNestingTest
{
    private readonly ClipStack clips = new ClipStack();

    [SetUp]
    public void SetUp() => clips.Reset();

    /// <summary>
    /// A mask from its box.
    /// </summary>
    private void push(float left, float top, float right, float bottom, float radius = 0, float shear = 0, MarginPadding fade = default) =>
        clips.Push(new Vector2((left + right) / 2, (top + bottom) / 2), new Vector2((right - left) / 2, (bottom - top) / 2), shear, radius, fade);

    private void assertShape(float left, float top, float right, float bottom, float radius, float shear = 0)
    {
        var clip = clips.Current;

        Assert.Multiple(() =>
        {
            Assert.That(clip.ClipData, Is.EqualTo(new Vector4((left + right) / 2, (top + bottom) / 2, (right - left) / 2, (bottom - top) / 2)), "its box");
            Assert.That(clip.Radius, Is.EqualTo(radius), "its corners");
            Assert.That(clip.ShearX, Is.EqualTo(shear), "its slant");
        });
    }

    private void assertNoCuts() => Assert.That(clips.Current.FadeLength, Is.EqualTo(new Vector4(0, 0, 0, 0)), "nothing cut");

    [Test]
    public void AMaskOnItsOwnIsJustItself()
    {
        push(10, 20, 90, 80, 12);

        assertShape(10, 20, 90, 80, 12);
        assertNoCuts();
    }

    /// <summary>
    /// The card in the scroll: its corners stay where they are, off the bottom, and the scroll's edge cuts it straight.
    /// </summary>
    [Test]
    public void ARoundedMaskCutByTheOneAroundItKeepsItsShape()
    {
        push(0, 0, 100, 100);
        push(10, 60, 90, 140, 12);

        assertShape(10, 60, 90, 140, 12);

        Assert.Multiple(() =>
        {
            Assert.That(clips.Current.FadeEdges.W, Is.EqualTo(100.5f), "a pixel's anti-aliasing, centered on the scroll's bottom");
            Assert.That(clips.Current.FadeLength.W, Is.EqualTo(1));
            Assert.That(clips.Current.FadeLength.X + clips.Current.FadeLength.Y + clips.Current.FadeLength.Z, Is.Zero, "nothing else is cut");
        });
    }

    /// <summary>
    /// Each side, as the shader measures it: inward from the near sides, and inward from the far ones.
    /// </summary>
    [TestCase(-40, 10, 40, 90, -0.5f, 0, 0, 0, TestName = "Cut on the left")]
    [TestCase(10, -40, 90, 40, 0, -0.5f, 0, 0, TestName = "Cut at the top")]
    [TestCase(60, 10, 140, 90, 0, 0, 100.5f, 0, TestName = "Cut on the right")]
    [TestCase(10, 60, 90, 140, 0, 0, 0, 100.5f, TestName = "Cut at the bottom")]
    public void EachSideIsCutAtTheEdge(float left, float top, float right, float bottom, float l, float t, float r, float b)
    {
        push(0, 0, 100, 100);
        push(left, top, right, bottom, 8);

        Assert.Multiple(() =>
        {
            Assert.That(clips.Current.FadeEdges, Is.EqualTo(new Vector4(l, t, r, b)));
            Assert.That(clips.Current.FadeLength, Is.EqualTo(new Vector4(l != 0 ? 1 : 0, t != 0 ? 1 : 0, r != 0 ? 1 : 0, b != 0 ? 1 : 0)));
        });
    }

    /// <summary>
    /// The button on the devices page: a radius past half its height, in a sliver of it, used to bulge past the sliver.
    /// </summary>
    [Test]
    public void APillCutShorterThanItsRadiusKeepsItsShape()
    {
        push(0, 50, 120, 120);
        push(20, 30, 100, 70, 34);

        assertShape(20, 30, 100, 70, 34);
        Assert.That(clips.Current.FadeEdges.Y, Is.EqualTo(49.5f));
    }

    [Test]
    public void AMaskInsideTheOneAroundItIsLeftAlone()
    {
        push(0, 0, 100, 100);
        push(10, 10, 90, 90, 8);

        assertShape(10, 10, 90, 90, 8);
        assertNoCuts();
    }

    /// <summary>
    /// The cell in the card in the scroll: the scroll's cut reaches what's inside the card, which doesn't stick out of it.
    /// </summary>
    [Test]
    public void ACutReachesTheMasksInside()
    {
        push(0, 0, 100, 100);
        push(10, 60, 90, 140, 12);
        push(20, 70, 80, 130, 6);

        assertShape(20, 70, 80, 130, 6);
        Assert.That(clips.Current.FadeEdges.W, Is.EqualTo(100.5f));
    }

    [Test]
    public void TheTighterOfTwoCutsWins()
    {
        push(0, 0, 100, 100);
        push(10, 40, 90, 90, 12);
        push(20, 60, 80, 140, 6);

        Assert.That(clips.Current.FadeEdges.W, Is.EqualTo(90.5f), "the card's bottom, above the scroll's");

        clips.Reset();

        push(0, 0, 100, 100);
        push(10, 40, 90, 130, 12);
        push(20, 60, 80, 140, 6);

        Assert.That(clips.Current.FadeEdges.W, Is.EqualTo(100.5f), "the scroll's bottom, above the card's");
    }

    /// <summary>
    /// A cut at the edge a fade reaches nothing at takes nothing from the fade.
    /// </summary>
    [Test]
    public void ACutAtAFadingEdgeLeavesTheFade()
    {
        push(0, 0, 100, 100, fade: new MarginPadding { Bottom = 20 });
        push(10, 60, 90, 140, 12);

        Assert.Multiple(() =>
        {
            Assert.That(clips.Current.FadeEdges.W, Is.EqualTo(100));
            Assert.That(clips.Current.FadeLength.W, Is.EqualTo(20));
        });
    }

    /// <summary>
    /// A fade and a cut further in, on one side, share the side: zero at the cut, whole where the fade ends. It never shows
    /// more than either would.
    /// </summary>
    [Test]
    public void ACutInsideAFadeJoinsIt()
    {
        push(0, 0, 100, 100, fade: new MarginPadding { Top = 40 });
        push(0, 10, 100, 100, 12);
        push(20, 0, 80, 60, 6);

        Assert.Multiple(() =>
        {
            Assert.That(clips.Current.FadeEdges.Y, Is.EqualTo(9.5f));
            Assert.That(clips.Current.FadeLength.Y, Is.EqualTo(30.5f));
        });
    }

    /// <summary>
    /// A plain box reaching a rounded mask's corner (a header strip across a card): the card's the shape, and the box its edge.
    /// </summary>
    [Test]
    public void ABoxReachingARoundedMasksCornerKeepsTheCorner()
    {
        push(0, 0, 100, 100, 12);
        push(0, 0, 100, 50);

        assertShape(0, 0, 100, 100, 12);

        Assert.Multiple(() =>
        {
            Assert.That(clips.Current.FadeEdges.W, Is.EqualTo(50.5f));
            Assert.That(clips.Current.FadeLength, Is.EqualTo(new Vector4(0, 0, 0, 1)), "only its bottom is inside the card");
        });
    }

    /// <summary>
    /// The box's own sides inside the card are its edges there, whichever they are.
    /// </summary>
    [TestCase(40, 0, 100, 60, 39.5f, 0, 0, 60.5f, TestName = "A box at the top right, cut on the left and at the bottom")]
    [TestCase(0, 40, 60, 100, 0, 39.5f, 60.5f, 0, TestName = "A box at the bottom left, cut at the top and on the right")]
    public void ABoxInARoundedMasksCornerIsCutOnItsOwnSides(float left, float top, float right, float bottom, float l, float t, float r, float b)
    {
        push(0, 0, 100, 100, 12);
        push(left, top, right, bottom);

        assertShape(0, 0, 100, 100, 12);

        Assert.Multiple(() =>
        {
            Assert.That(clips.Current.FadeEdges, Is.EqualTo(new Vector4(l, t, r, b)));
            Assert.That(clips.Current.FadeLength, Is.EqualTo(new Vector4(l != 0 ? 1 : 0, t != 0 ? 1 : 0, r != 0 ? 1 : 0, b != 0 ? 1 : 0)));
        });
    }

    /// <summary>
    /// Corners past the top and bottom of a rounded mask, or past its sides, are gone: what's left of it is straight, so the
    /// mask around is the shape.
    /// </summary>
    [TestCase(5, -30, 95, 130, TestName = "Corners past the top and bottom")]
    [TestCase(-30, 5, 130, 95, TestName = "Corners past the sides")]
    public void ARoundedMaskWithItsCornersPastARoundedOneIsItsEdges(float left, float top, float right, float bottom)
    {
        push(0, 0, 100, 100, 12);
        push(left, top, right, bottom, 12);

        assertShape(0, 0, 100, 100, 12);
    }

    /// <summary>
    /// Any one corner showing is enough for it to be the shape.
    /// </summary>
    [TestCase(30, 30, 130, 130, TestName = "The top left one")]
    [TestCase(-30, 30, 70, 130, TestName = "The top right one")]
    [TestCase(30, -30, 130, 70, TestName = "The bottom left one")]
    [TestCase(-30, -30, 70, 70, TestName = "The bottom right one")]
    public void ARoundedMaskWithOneCornerShowingIsTheShape(float left, float top, float right, float bottom)
    {
        push(0, 0, 100, 100, 12);
        push(left, top, right, bottom, 12);

        assertShape(left, top, right, bottom, 12);
    }

    /// <summary>
    /// A scroll filling a rounded panel: the panel's the shape, with nothing to add.
    /// </summary>
    [Test]
    public void ABoxFillingARoundedMaskIsTheRoundedMask()
    {
        push(0, 0, 100, 100, 12);
        push(0, 0, 100, 100);

        assertShape(0, 0, 100, 100, 12);
        assertNoCuts();
    }

    /// <summary>
    /// Clear of the corners, the rounded mask has no say, and the box stays the shape: no fade around it is disturbed.
    /// </summary>
    [TestCase(20, 20, 80, 80, TestName = "Inside on every side")]
    [TestCase(0, 30, 100, 70, TestName = "A strip across the middle")]
    [TestCase(30, 0, 70, 100, TestName = "A strip down the middle")]
    public void ABoxClearOfARoundedMasksCornersIsItsOwnShape(float left, float top, float right, float bottom)
    {
        push(0, 0, 100, 100, 12);
        push(left, top, right, bottom);

        assertShape(left, top, right, bottom, 0);
        assertNoCuts();
    }

    /// <summary>
    /// A mask whose own edges are all outside the one around it adds nothing.
    /// </summary>
    [Test]
    public void ARoundedMaskWellOutsideTheOneAroundItAddsNothing()
    {
        push(0, 0, 100, 100, 12);
        push(-20, -20, 120, 120, 12);

        assertShape(0, 0, 100, 100, 12);
        assertNoCuts();
    }

    /// <summary>
    /// A rounded cover running off the foot of a card as rounded: its corners at the top are round the card's, and those at
    /// the bottom are off it, so it's the card's corners that show, at all four.
    /// </summary>
    [Test]
    public void ARoundedMaskRunningOffAnEquallyRoundedOneKeepsItsCorners()
    {
        push(0, 0, 100, 100, 12);
        push(0, 0, 100, 130, 12);

        assertShape(0, 0, 100, 100, 12);
        assertNoCuts();
    }

    /// <summary>
    /// One of its corners inside the card, where it shows: it's the shape, and the card only its edges.
    /// </summary>
    [Test]
    public void ARoundedMaskWithACornerShowingIsTheShape()
    {
        push(0, 0, 100, 100, 12);
        push(10, 0, 110, 130, 12);

        assertShape(10, 0, 110, 130, 12);
    }

    /// <summary>
    /// A slant anywhere inside the mask around shows.
    /// </summary>
    [Test]
    public void ASlantedMaskRunningOffARoundedOneIsTheShape()
    {
        push(0, 0, 100, 100, 12);
        push(-5, -5, 105, 105, 0, 0.1f);

        assertShape(-5, -5, 105, 105, 0, 0.1f);
    }

    /// <summary>
    /// A rounded and slanted one needs to be further out, for its corners too.
    /// </summary>
    [Test]
    public void ARoundedSlantedMaskNearARoundedOneIsTheShape()
    {
        push(0, 0, 100, 100, 12);
        push(-30, -30, 130, 130, 20, 0.1f);

        assertShape(-30, -30, 130, 130, 20, 0.1f);
    }

    /// <summary>
    /// Well outside it, it doesn't.
    /// </summary>
    [Test]
    public void ASlantedMaskWellOutsideARoundedOneAddsNothing()
    {
        push(0, 0, 100, 100, 12);
        push(-60, -60, 160, 160, 0, 0.1f);

        assertShape(0, 0, 100, 100, 12);
    }

    /// <summary>
    /// The same box, more rounded: its corners are the ones that show.
    /// </summary>
    [Test]
    public void AMoreRoundedMaskOverTheSameBoxIsTheShape()
    {
        push(0, 0, 100, 100, 4);
        push(0, 0, 100, 100, 40);

        assertShape(0, 0, 100, 100, 40);
    }

    /// <summary>
    /// A slant is measured from the height of the whole mask, so a cut one that lost height leaned wrong.
    /// </summary>
    [Test]
    public void ASlantedMaskCutByTheOneAroundItKeepsItsSlant()
    {
        push(0, 0, 200, 100);
        push(50, 60, 150, 140, 6, 0.2f);

        assertShape(50, 60, 150, 140, 6, 0.2f);
        Assert.That(clips.Current.FadeEdges.W, Is.EqualTo(100.5f));
    }

    /// <summary>
    /// A box inside a slanted mask but past its slanted side: the slant's the shape.
    /// </summary>
    [Test]
    public void ABoxReachingASlantedSideKeepsTheSlant()
    {
        push(40, 0, 160, 100, 0, 0.2f);
        push(25, 30, 100, 70);

        assertShape(40, 0, 160, 100, 0, 0.2f);
    }

    /// <summary>
    /// A box in the part of a slanted mask's bounds its slanted side crosses.
    /// </summary>
    [Test]
    public void ABoxWhereASlantedSideCrossesKeepsTheSlant()
    {
        push(40, 0, 160, 100, 0, 0.2f);
        push(35, 30, 100, 70);

        assertShape(40, 0, 160, 100, 0, 0.2f);
    }

    /// <summary>
    /// The bounds of a slanted mask reach past its box by its slant: a box there isn't outside it.
    /// </summary>
    [Test]
    public void ABoxInASlantedMasksReachIsntOutsideIt()
    {
        push(40, 0, 160, 100, 0, 0.2f);
        push(32, 30, 38, 70);

        assertShape(40, 0, 160, 100, 0, 0.2f);
    }

    /// <summary>
    /// Between a slanted mask's sides it has no say.
    /// </summary>
    [Test]
    public void ABoxBetweenASlantedMasksSidesIsItsOwnShape()
    {
        push(40, 0, 160, 100, 0, 0.2f);
        push(80, 30, 120, 70);

        assertShape(80, 30, 120, 70, 0);
    }

    /// <summary>
    /// Scrolled right out, it clips away everything: without the corners, which round a shape of no size into a circle.
    /// </summary>
    [Test]
    public void AMaskOutsideTheOneAroundItShowsNothing()
    {
        push(0, 0, 100, 100);
        push(10, 200, 90, 280, 30);

        var outside = clips.Current;

        Assert.Multiple(() =>
        {
            Assert.That(outside.ClipData.Z, Is.GreaterThan(0), "still a mask");
            Assert.That(outside.ClipData.Z, Is.LessThan(0.001f));
            Assert.That(outside.ClipData.W, Is.LessThan(0.001f));
            Assert.That(outside.Radius, Is.Zero);
            Assert.That(outside.ClipData.X, Is.LessThan(-1000), "nowhere near anything");
        });

        push(20, 210, 80, 270, 30);

        Assert.That(clips.Current.ClipData, Is.EqualTo(outside.ClipData), "and nor does what's inside it");
    }

    /// <summary>
    /// Off any side of the one around it.
    /// </summary>
    [TestCase(110, 10, 190, 90, TestName = "Off the right")]
    [TestCase(-190, 10, -110, 90, TestName = "Off the left")]
    [TestCase(10, 110, 90, 190, TestName = "Off the bottom")]
    [TestCase(10, -190, 90, -110, TestName = "Off the top")]
    public void AMaskOffEachSideShowsNothing(float left, float top, float right, float bottom)
    {
        push(0, 0, 100, 100);
        push(left, top, right, bottom, 8);

        Assert.That(clips.Current.ClipData, Is.EqualTo(ClipState.Nothing.ClipData));
    }

    [Test]
    public void PoppingRestoresWhatWasCut()
    {
        push(0, 0, 100, 100);
        push(10, 60, 90, 140, 12);
        clips.Pop();

        assertShape(0, 0, 100, 100, 0);
        assertNoCuts();
    }
}

/// <summary>
/// Masks inside masks in pixels, on Vulkan (see <see cref="PixelCapture"/>). The reference rasterizer doesn't mask, so it
/// has no fixture here. The rest of what this depends on is the clip each vertex is given, the same on every backend.
/// </summary>
[TestFixture(PixelCapture.VULKAN)]
public class NestedMaskTest
{
    private const int width = 120;
    private const int height = 120;

    private static readonly Vector4 background = new Vector4(0, 0, 0, 1);

    private readonly string rendererKind;

    private ManualClock manual = null!;
    private Container root = null!;
    private PixelCapture capture = null!;

    private float tolerance => 0.01f + capture.Quantization;

    public NestedMaskTest(string rendererKind)
    {
        this.rendererKind = rendererKind;
    }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Logger.Initialize();
        capture = PixelCapture.Create(rendererKind, width, height);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        capture?.Dispose();
        Logger.Shutdown();
    }

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container
        {
            Size = new Vector2(width, height),
            Clock = new FramedClock(manual)
        };

        root.Load();
        root.CompleteLoad();
    }

    private PixelSurface render()
    {
        for (int i = 0; i < 3; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }

        capture.Begin(background);
        capture.Renderer.SetRoot(root.GenerateDrawNodeSubtree(0));
        capture.Renderer.Draw(new FramedClock(manual));

        return capture.End();
    }

    private static Box whiteFill() => new Box { RelativeSizeAxes = Axes.Both, Color = Color.White };

    private static Container mask(Vector2 position, Vector2 size, params Drawable[] children) => new Container
    {
        Position = position,
        Size = size,
        Masking = true,
        Children = children
    };

    private static Container rounded(Vector2 position, Vector2 size, float radius, params Drawable[] children) => new Container
    {
        Position = position,
        Size = size,
        Masking = true,
        CornerRadius = radius,
        Children = children
    };

    private void assertWhite(PixelSurface screen, int x, int y, string what) => Assert.That(screen[x, y].X, Is.EqualTo(1f).Within(tolerance), $"{what}, at ({x}, {y})");

    private void assertBlack(PixelSurface screen, int x, int y, string what) => Assert.That(screen[x, y].X, Is.EqualTo(0f).Within(tolerance), $"{what}, at ({x}, {y})");

    /// <summary>
    /// The card scrolled half out: straight across where it's cut, its own corners still round at its other end.
    /// </summary>
    [Test]
    public void ARoundedCardIsCutStraightAcross()
    {
        root.Add(mask(Vector2.Zero, new Vector2(width, 60), rounded(new Vector2(20, 20), new Vector2(80, 80), 16, whiteFill())));

        var screen = render();

        Assert.Multiple(() =>
        {
            assertWhite(screen, 20, 59, "the card's left edge, right down to the cut");
            assertWhite(screen, 99, 59, "the card's right edge, right down to the cut");
            assertWhite(screen, 60, 59, "the middle, down to the cut");
            assertBlack(screen, 60, 60, "past the cut");
            assertBlack(screen, 20, 20, "its top left corner is still round");
            assertBlack(screen, 99, 20, "its top right corner is still round");
            assertWhite(screen, 60, 20, "the middle of its top edge");
        });
    }

    /// <summary>
    /// The Calibrate button: cut to a sliver shorter than its radius, it stays in the mask rather than bulging past it.
    /// </summary>
    [Test]
    public void APillCutToASliverStaysInTheMask()
    {
        root.Add(mask(new Vector2(0, 50), new Vector2(width, 70), rounded(new Vector2(20, -20), new Vector2(80, 40), 34, whiteFill())));

        var screen = render();

        Assert.Multiple(() =>
        {
            for (int row = 25; row < 50; row += 4)
                assertBlack(screen, 60, row, "above the mask");

            assertBlack(screen, 60, 49, "the row above the mask");
            assertWhite(screen, 60, 50, "the row inside the mask");
            assertWhite(screen, 60, 69, "the pill's own bottom");
            assertBlack(screen, 60, 70, "past the pill");
        });
    }

    /// <summary>
    /// The advice under the Timing figures: a pill too long and too low for its card is cut by the card, in a scroll too.
    /// </summary>
    [Test]
    public void APillOverflowingACardIsCutByTheCard()
    {
        root.Add(mask(Vector2.Zero, new Vector2(width, height),
            rounded(new Vector2(10, 20), new Vector2(90, 50), 8,
                rounded(new Vector2(10, 35), new Vector2(120, 30), 15, whiteFill()))));

        var screen = render();

        Assert.Multiple(() =>
        {
            assertWhite(screen, 60, 69, "the card's last row");
            assertBlack(screen, 60, 70, "under the card");
            assertBlack(screen, 60, 80, "well under it, where the pill would reach");
            assertWhite(screen, 99, 60, "the card's last column");
            assertBlack(screen, 100, 60, "right of the card");
        });
    }

    /// <summary>
    /// On a fraction of a pixel, the edge it's cut at is the same edge as a plain box gets from the same mask, so a card and
    /// the text beside it are cut alike.
    /// </summary>
    [Test]
    public void ACutEdgeIsTheSameAsAPlainBoxInTheSameMask()
    {
        root.Add(mask(new Vector2(0, 30.5f), new Vector2(width, 40.25f),
            new Box { Position = new Vector2(10, -20), Size = new Vector2(40, 80), Color = Color.White },
            rounded(new Vector2(70, -20), new Vector2(40, 80), 6, whiteFill())));

        var screen = render();

        Assert.Multiple(() =>
        {
            for (int row = 28; row < 74; row++)
                Assert.That(screen[90, row].X, Is.EqualTo(screen[30, row].X).Within(tolerance), $"row {row}");
        });

        Assert.That(screen[30, 30].X, Is.GreaterThan(0.2f).And.LessThan(0.8f), "the test's edge really is on a fraction of a pixel");
    }

    /// <summary>
    /// A panel with rounded corners and a scroll filling it: what's in the scroll follows the panel's corners.
    /// </summary>
    [Test]
    public void AMaskFillingARoundedPanelFollowsItsCorners()
    {
        root.Add(rounded(new Vector2(10), new Vector2(100), 20, mask(Vector2.Zero, new Vector2(100), whiteFill())));

        var screen = render();

        Assert.Multiple(() =>
        {
            assertBlack(screen, 10, 10, "top left corner");
            assertBlack(screen, 109, 10, "top right corner");
            assertBlack(screen, 10, 109, "bottom left corner");
            assertBlack(screen, 109, 109, "bottom right corner");
            assertWhite(screen, 60, 10, "top edge");
            assertWhite(screen, 60, 60, "middle");
        });
    }

    /// <summary>
    /// A header strip in a card: rounded at the card's corners, and straight along its own bottom.
    /// </summary>
    [Test]
    public void AStripAcrossARoundedCardFollowsItsCorners()
    {
        root.Add(rounded(new Vector2(10), new Vector2(100), 20, mask(Vector2.Zero, new Vector2(100, 40), whiteFill())));

        var screen = render();

        Assert.Multiple(() =>
        {
            assertBlack(screen, 10, 10, "the card's corner");
            assertWhite(screen, 60, 10, "the strip's top");
            assertWhite(screen, 10, 49, "the strip's bottom left, straight");
            assertWhite(screen, 109, 49, "the strip's bottom right, straight");
            assertBlack(screen, 60, 50, "under the strip");
        });
    }

    /// <summary>
    /// A cover running off the foot of a card as rounded: the card's corners, all four, not square ones where it's cut.
    /// </summary>
    [Test]
    public void ARoundedMaskRunningOffARoundedCardKeepsItsCorners()
    {
        root.Add(rounded(new Vector2(10), new Vector2(100), 20, rounded(Vector2.Zero, new Vector2(100, 140), 20, whiteFill())));

        var screen = render();

        Assert.Multiple(() =>
        {
            assertBlack(screen, 10, 10, "top left");
            assertBlack(screen, 109, 10, "top right");
            assertBlack(screen, 10, 109, "bottom left");
            assertBlack(screen, 109, 109, "bottom right");
            assertWhite(screen, 60, 109, "its foot, in the middle");
            assertBlack(screen, 60, 110, "past the card");
        });
    }

    /// <summary>
    /// A cell in a card in a scroll, as the Lanes card has: all of it cut by the scroll.
    /// </summary>
    [Test]
    public void AMaskInAMaskInAMaskIsCutByTheOutermost()
    {
        root.Add(mask(Vector2.Zero, new Vector2(width, 60),
            rounded(new Vector2(10, 10), new Vector2(100, 100), 12,
                rounded(new Vector2(10, 10), new Vector2(80, 80), 8, whiteFill()))));

        var screen = render();

        Assert.Multiple(() =>
        {
            assertWhite(screen, 20, 59, "the cell's left edge, right down to the cut");
            assertWhite(screen, 99, 59, "the cell's right edge, right down to the cut");
            assertBlack(screen, 60, 60, "past the cut");
            assertBlack(screen, 20, 20, "the cell's own corner");
        });
    }

    /// <summary>
    /// What the report was of, in the real thing: a scroll, scrolled.
    /// </summary>
    [Test]
    public void ACardInAScrollIsCutWhereItsScrolledTo()
    {
        ScrollableContainer scroll;

        root.Add(scroll = new ScrollableContainer
        {
            Size = new Vector2(width, 60),
            Direction = ScrollDirection.Vertical,
            Child = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Children = new Drawable[]
                {
                    rounded(Vector2.Zero, new Vector2(80, 80), 16, whiteFill()),
                    new Container { Size = new Vector2(10, 100) }
                }
            }
        });

        render();
        scroll.ScrollTo(new Vector2(0, 30), false);

        var scrolled = render();

        Assert.Multiple(() =>
        {
            assertWhite(scrolled, 0, 0, "its left edge at the top of the scroll");
            assertWhite(scrolled, 79, 0, "its right edge at the top of the scroll");
            assertBlack(scrolled, 80, 0, "right of it");
            assertWhite(scrolled, 40, 49, "its bottom edge, scrolled up");
            assertBlack(scrolled, 40, 50, "under it");
            assertBlack(scrolled, 0, 49, "its own bottom corner, still round");
        });
    }

    /// <summary>
    /// What a mask shows isn't changed by being cut, whatever it is. The rows it has left are the rows it had.
    /// </summary>
    [TestCaseSource(nameof(cutShapes))]
    public void CuttingChangesNothingThatsLeft(string name, Func<Drawable> shape)
    {
        root.Add(mask(Vector2.Zero, new Vector2(width, height), shape()));
        var whole = copyRows(render());

        root.Clear();
        root.Add(mask(Vector2.Zero, new Vector2(width, 60), shape()));
        var cut = render();

        Assert.Multiple(() =>
        {
            for (int row = 0; row < 60; row++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (Math.Abs(cut[x, row].X - whole[x, row]) > tolerance)
                    {
                        Assert.Fail($"{name}: at ({x}, {row}) it was {whole[x, row]} and is now {cut[x, row].X}");
                        return;
                    }
                }
            }

            for (int x = 0; x < width; x++)
                Assert.That(cut[x, 60].X, Is.EqualTo(0f).Within(tolerance), $"{name}: nothing past the cut, at {x}");
        });
    }

    private static float[,] copyRows(PixelSurface screen)
    {
        float[,] rows = new float[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                rows[x, y] = screen[x, y].X;
        }

        return rows;
    }

    private static IEnumerable<TestCaseData> cutShapes()
    {
        yield return new TestCaseData("rounded", (Func<Drawable>)(() => rounded(new Vector2(20, 20), new Vector2(80, 80), 16, whiteFill()))).SetName("A rounded mask");
        yield return new TestCaseData("pill", (Func<Drawable>)(() => rounded(new Vector2(20, 30), new Vector2(80, 50), 25, whiteFill()))).SetName("A pill");

        yield return new TestCaseData("circle", (Func<Drawable>)(() => new Circle
        {
            Position = new Vector2(20, 20),
            Size = new Vector2(80),
            Color = Color.White
        })).SetName("A circle");

        yield return new TestCaseData("sheared", (Func<Drawable>)(() => new Container
        {
            Position = new Vector2(30, 20),
            Size = new Vector2(60, 80),
            Masking = true,
            CornerRadius = 8,
            Shear = new Vector2(0.2f, 0),
            Child = whiteFill()
        })).SetName("A slanted mask");

        yield return new TestCaseData("bordered", (Func<Drawable>)(() => new Container
        {
            Position = new Vector2(20, 20),
            Size = new Vector2(80),
            Masking = true,
            CornerRadius = 12,
            BorderThickness = 4,
            BorderColor = Color.White,
            Child = new Box { RelativeSizeAxes = Axes.Both, Color = Color.Gray }
        })).SetName("A bordered mask");

        yield return new TestCaseData("nested", (Func<Drawable>)(() => rounded(new Vector2(10, 10), new Vector2(100, 100), 12,
            rounded(new Vector2(10, 10), new Vector2(80, 80), 8, whiteFill())))).SetName("A rounded mask in a rounded mask");
    }
}
