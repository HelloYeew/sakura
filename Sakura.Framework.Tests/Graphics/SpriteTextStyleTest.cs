// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// How <see cref="SpriteText"/> colors its glyphs, read off the vertices it hands its draw node, with a real font.
/// </summary>
[TestFixture]
public class SpriteTextStyleTest
{
    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;
    private ManualClock manual = null!;
    private FontRoot root = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        store = new RendererFontStore(new HeadlessRenderer(textureManager));

        var fonts = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources")
            .GetStorageForDirectory("Fonts");

        store.AddFont(fonts, "Comfortaa-Regular.ttf", alias: "Sprite");

        manual = new ManualClock { CurrentTime = 1000 };
        root = new FontRoot { Size = new Vector2(800, 600), Clock = new FramedClock(manual) };
        root.Load();
        root.CompleteLoad();
        root.Provide(store);
    }

    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    private static FontUsage usage => FontUsage.Default.With(family: "Sprite", size: 40f);

    private SpriteText add(string content = "HAMBURG")
    {
        var sprite = new SpriteText { Text = content, Font = usage };
        root.Add(sprite);
        frame();
        return sprite;
    }

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    private DrawNode node(int buffer = 0) => ((ContainerDrawNode)root.GenerateDrawNodeSubtree(buffer)).Children[^1];

    /// <summary>
    /// Headless glyphs have textures to draw, or everything here would be testing the zero-area stand-ins for spaces.
    /// </summary>
    [Test]
    public void GlyphsAreRealHere()
    {
        var shaped = store.Shape(usage, "HAMBURG", 1f);

        Assert.Multiple(() =>
        {
            Assert.That(shaped.Glyphs.Count, Is.EqualTo(7));
            Assert.That(shaped.Glyphs.All(g => g.Texture != null), "every letter has a texture");
            Assert.That(shaped.BoundingBox.X, Is.GreaterThan(100), "and takes up room");
        });
    }

    /// <summary>
    /// A color's own alpha counts, as it does on every other drawable: it used to be dropped, so such text drew opaque.
    /// </summary>
    [Test]
    public void AColorsOwnAlphaCounts()
    {
        SpriteText sprite = add();
        sprite.Color = Color.White.WithAlpha(0.5f);
        frame();

        Assert.That(node().Vertices[0].Color.W, Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public void AColorsOwnAlphaAndAFadeMultiply()
    {
        SpriteText sprite = add();
        sprite.Color = Color.White.WithAlpha(0.5f);
        frame();

        sprite.Alpha = 0.5f;
        frame();

        Assert.That(node().Vertices[0].Color.W, Is.EqualTo(0.25f).Within(0.01f));
    }

    [Test]
    public void AFlatColorIsTheSameEverywhere()
    {
        add().Color = Color.Red;
        frame();

        DrawNode drawn = node();

        for (int i = 0; i < drawn.VertexCount; i++)
            Assert.That(drawn.Vertices[i].Color, Is.EqualTo(new Vector4(1, 0, 0, 1)), $"Vertex {i}");
    }

    #region Letter spacing

    private float width(string content, float spacing = 0) => store.Shape(usage, content, 1f).BoundingBox.X + (store.Shape(usage, content, 1f).Glyphs.Count - 1) * spacing;

    [Test]
    public void LetterSpacingAddsRoomBetweenGlyphs()
    {
        SpriteText sprite = add();
        float before = sprite.ContentSize.X;

        sprite.LetterSpacing = 10;

        Assert.Multiple(() =>
        {
            Assert.That(sprite.ContentSize.X, Is.EqualTo(before + 6 * 10).Within(0.01f), "six gaps in seven letters, none after the last");
            Assert.That(sprite.Size.X, Is.EqualTo(before + 6 * 10).Within(1f), "and it's that much bigger for layout");
        });
    }

    [Test]
    public void EachGlyphMovesByItsGapsSoFar()
    {
        SpriteText sprite = add();
        float[] before = Enumerable.Range(0, 7).Select(i => node().Vertices[i * 4].Position.X).ToArray();

        sprite.LetterSpacing = 10;
        frame();

        for (int i = 0; i < 7; i++)
            Assert.That(node().Vertices[i * 4].Position.X, Is.EqualTo(before[i] + i * 10).Within(0.01f), $"Glyph {i}");
    }

    [Test]
    public void NegativeSpacingDrawsThemCloser()
    {
        SpriteText sprite = add();
        float before = sprite.ContentSize.X;

        sprite.LetterSpacing = -2;

        Assert.That(sprite.ContentSize.X, Is.EqualTo(before - 12).Within(0.01f));
    }

    /// <summary>
    /// Shaping is cached and shared between sprites showing the same text: one's spacing mustn't reach the other.
    /// </summary>
    [Test]
    public void SpacingIsItsOwn()
    {
        SpriteText spaced = add();
        SpriteText plain = add();
        float plainWidth = plain.ContentSize.X;

        spaced.LetterSpacing = 10;
        frame();

        Assert.That(plain.ContentSize.X, Is.EqualTo(plainWidth));
    }

    /// <summary>
    /// A caret goes where the glyphs are.
    /// </summary>
    [Test]
    public void CharacterPositionsFollowIt()
    {
        SpriteText sprite = add();
        float third = sprite.GetCharacterPosition(3).X;
        float end = sprite.GetCharacterPosition(7).X;

        sprite.LetterSpacing = 10;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(sprite.GetCharacterPosition(3).X, Is.EqualTo(third + 30).Within(0.01f));
            Assert.That(sprite.GetCharacterPosition(7).X, Is.EqualTo(end + 60).Within(0.01f), "the end, past the last glyph");
        });
    }

    [Test]
    public void TruncationCountsIt()
    {
        const string content = "The quick brown fox jumps over the lazy dog";

        SpriteText sprite = add(content);
        sprite.MaxWidth = width(content) + 5;
        sprite.Truncate = true;
        Assert.That(sprite.IsTruncated, Is.False, "it fits as it is");

        sprite.LetterSpacing = 5;

        Assert.Multiple(() =>
        {
            Assert.That(sprite.IsTruncated, Is.True, "not with room between its letters");
            Assert.That(sprite.ContentSize.X, Is.LessThanOrEqualTo(sprite.MaxWidth));
        });
    }

    [Test]
    public void ASpaceGetsItToo()
    {
        SpriteText sprite = add("A B");
        float before = sprite.ContentSize.X;

        sprite.LetterSpacing = 10;

        Assert.That(sprite.ContentSize.X, Is.EqualTo(before + 20).Within(0.01f));
    }

    [Test]
    public void OneGlyphHasNoGaps()
    {
        SpriteText sprite = add("A");
        float before = sprite.ContentSize.X;

        sprite.LetterSpacing = 10;

        Assert.That(sprite.ContentSize.X, Is.EqualTo(before).Within(0.01f));
    }

    #endregion

    #region Gradients

    [Test]
    public void AGradientRunsAcrossTheWholeText()
    {
        SpriteText sprite = add();
        sprite.ColorInfo = ColorInfo.GradientHorizontal(Color.Red, Color.Blue);
        frame();

        DrawNode drawn = node();
        float firstRed = drawn.Vertices[0].Color.X;
        float lastRed = drawn.Vertices[6 * 4 + 1].Color.X;

        Assert.Multiple(() =>
        {
            Assert.That(firstRed, Is.GreaterThan(0.9f), "the start is nearly all red");
            Assert.That(lastRed, Is.LessThan(0.1f), "the end nearly none");
            Assert.That(drawn.Vertices[3 * 4].Color.X, Is.InRange(0.3f, 0.7f), "the middle letter between them");
            Assert.That(drawn.Vertices[0].Color.X, Is.GreaterThan(drawn.Vertices[1].Color.X), "and it changes across one letter");
        });
    }

    [Test]
    public void AVerticalGradientRunsDownEachLetter()
    {
        SpriteText sprite = add();
        sprite.ColorInfo = ColorInfo.GradientVertical(Color.Red, Color.Blue);
        frame();

        DrawNode drawn = node();

        Assert.Multiple(() =>
        {
            Assert.That(drawn.Vertices[0].Color.X, Is.GreaterThan(drawn.Vertices[3].Color.X), "top redder than bottom");
            Assert.That(drawn.Vertices[0].Color.X, Is.EqualTo(drawn.Vertices[1].Color.X).Within(1e-5f), "the same across");
        });
    }

    /// <summary>
    /// A fade goes through the color-only path, which has to keep the gradient rather than flatten it.
    /// </summary>
    [Test]
    public void AGradientSurvivesAFade()
    {
        SpriteText sprite = add();
        sprite.ColorInfo = ColorInfo.GradientHorizontal(Color.Red, Color.Blue);
        frame();

        float before = node().Vertices[3 * 4].Color.X;

        sprite.Alpha = 0.5f;
        frame();

        DrawNode drawn = node();

        Assert.Multiple(() =>
        {
            Assert.That(drawn.Vertices[3 * 4].Color.X, Is.EqualTo(before).Within(1e-5f));
            Assert.That(drawn.Vertices[3 * 4].Color.W, Is.EqualTo(0.5f).Within(1e-5f));
        });
    }

    #endregion

    #region ColorAt

    [Test]
    public void ColorAtColorsEachCornerByWhereItIs()
    {
        SpriteText sprite = add();
        float end = sprite.ContentSize.X;

        // Red at the start, turning to blue by the end: a color for a place, whatever the letters.
        sprite.ColorAt = at => at.X < end / 2 ? Color.Red : Color.Blue;
        frame();

        DrawNode drawn = node();

        Assert.Multiple(() =>
        {
            Assert.That(drawn.Vertices[0].Color, Is.EqualTo(new Vector4(1, 0, 0, 1)), "the first letter");
            Assert.That(drawn.Vertices[6 * 4].Color, Is.EqualTo(new Vector4(0, 0, 1, 1)), "the last");
        });
    }

    [Test]
    public void ColorAtIsAskedInTheTextsOwnPixels()
    {
        var asked = new List<Vector2>();

        SpriteText sprite = add();
        sprite.Position = new Vector2(100, 100);
        sprite.LetterSpacing = 10;
        sprite.ColorAt = at =>
        {
            asked.Add(at);
            return Color.White;
        };
        frame();

        var glyphs = store.Shape(usage, "HAMBURG", 1f).Glyphs;

        Assert.Multiple(() =>
        {
            Assert.That(asked.Count, Is.GreaterThanOrEqualTo(28), "each corner of each of seven letters");
            Assert.That(asked[0].X, Is.EqualTo(glyphs[0].Position.X).Within(0.01f), "not moved by where it's placed");
            Assert.That(asked[4].X, Is.EqualTo(glyphs[1].Position.X + 10).Within(0.01f), "and with its spacing");
        });
    }

    [Test]
    public void ColorAtIsntAskedForSpaces()
    {
        int asked = 0;

        SpriteText sprite = add("A B");
        sprite.ColorAt = _ =>
        {
            asked++;
            return Color.White;
        };
        frame();

        Assert.That(asked, Is.EqualTo(2 * 4), "two letters' corners");
    }

    [Test]
    public void ColorAtsAlphaCounts()
    {
        SpriteText sprite = add();
        sprite.ColorAt = _ => Color.White.WithAlpha(0.5f);
        sprite.Alpha = 0.5f;
        frame();

        Assert.That(node().Vertices[0].Color.W, Is.EqualTo(0.25f).Within(0.01f));
    }

    [Test]
    public void SettingColorAtReachesItNextFrame()
    {
        SpriteText sprite = add();
        frame();

        sprite.ColorAt = _ => Color.Blue;
        frame();

        Assert.That(node().Vertices[0].Color, Is.EqualTo(new Vector4(0, 0, 1, 1)));
    }

    [Test]
    public void ColorAtTakesThePlaceOfItsColor()
    {
        SpriteText sprite = add();
        sprite.Color = Color.Red;
        sprite.ColorAt = _ => Color.Blue;
        frame();

        Assert.That(node().Vertices[0].Color, Is.EqualTo(new Vector4(0, 0, 1, 1)));

        sprite.ColorAt = null;
        frame();

        Assert.That(node().Vertices[0].Color, Is.EqualTo(new Vector4(1, 0, 0, 1)), "and gives it back");
    }

    /// <summary>
    /// How colors that move get there: asking again doesn't rebuild the glyphs, only rewrites their colors.
    /// </summary>
    [Test]
    public void InvalidatingColorAsksAgain()
    {
        Color current = Color.Red;

        SpriteText sprite = add();
        sprite.ColorAt = _ => current;
        frame();
        Assert.That(node().Vertices[0].Color.X, Is.EqualTo(1));

        current = Color.Blue;
        sprite.Invalidate(InvalidationFlags.Color);
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(node().Vertices[0].Color, Is.EqualTo(new Vector4(0, 0, 1, 1)));
            Assert.That(node(1).Vertices[0].Color, Is.EqualTo(new Vector4(0, 0, 1, 1)), "in another draw buffer too");
        });
    }

    #endregion

    #region Shadow

    [Test]
    public void AShadowIsTheTextAgainFirst()
    {
        SpriteText sprite = add();
        sprite.Position = new Vector2(100, 100);
        frame();

        Vector2 first = node().Vertices[0].Position;

        sprite.Shadow = true;
        sprite.ShadowOffset = new Vector2(3, 5);
        frame();

        DrawNode drawn = node();

        Assert.Multiple(() =>
        {
            Assert.That(drawn.VertexCount, Is.EqualTo(7 * 4 * 2), "twice the vertices");
            Assert.That(drawn.Vertices[0].Position.X, Is.EqualTo(first.X + 3).Within(0.01f), "the shadow's first, moved");
            Assert.That(drawn.Vertices[0].Position.Y, Is.EqualTo(first.Y + 5).Within(0.01f));
            Assert.That(drawn.Vertices[7 * 4].Position.X, Is.EqualTo(first.X).Within(0.01f), "then the text, where it was");
        });
    }

    [Test]
    public void AShadowHasItsOwnColorAndFades()
    {
        SpriteText sprite = add();
        sprite.Shadow = true;
        sprite.Color = Color.Red;
        sprite.ShadowColor = Color.Blue.WithAlpha(0.5f);
        frame();

        DrawNode drawn = node();

        Assert.Multiple(() =>
        {
            Assert.That(drawn.Vertices[0].Color.Z, Is.EqualTo(1), "blue");
            Assert.That(drawn.Vertices[0].Color.W, Is.EqualTo(0.5f).Within(0.01f), "with its alpha");
            Assert.That(drawn.Vertices[7 * 4].Color.X, Is.EqualTo(1), "and the text red");
        });

        sprite.Alpha = 0.5f;
        frame();

        Assert.That(node().Vertices[0].Color.W, Is.EqualTo(0.25f).Within(0.01f), "fading with the text");
    }

    [Test]
    public void AShadowTakesNoRoom()
    {
        SpriteText sprite = add();
        Vector2 before = sprite.Size;

        sprite.Shadow = true;
        sprite.ShadowOffset = new Vector2(30);
        frame();

        Assert.That(sprite.Size, Is.EqualTo(before));
    }

    /// <summary>
    /// What it draws is another matter: a mask mustn't cull a shadow still on screen.
    /// </summary>
    [Test]
    public void ItsRectangleHoldsTheShadow()
    {
        SpriteText sprite = add();
        RectangleF before = sprite.DrawRectangle;

        sprite.Shadow = true;
        sprite.ShadowOffset = new Vector2(30, -20);
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(sprite.DrawRectangle.Width, Is.EqualTo(before.Width + 30).Within(0.01f));
            Assert.That(sprite.DrawRectangle.Height, Is.EqualTo(before.Height + 20).Within(0.01f));
            Assert.That(sprite.DrawRectangle.Y, Is.EqualTo(before.Y - 20).Within(0.01f));
        });
    }

    [Test]
    public void AShadowTurnsWithTheText()
    {
        SpriteText sprite = add();
        sprite.Position = new Vector2(100, 100);
        sprite.Rotation = 90;
        sprite.Shadow = true;
        sprite.ShadowOffset = new Vector2(10, 0);
        frame();

        DrawNode drawn = node();
        Vector2 back = drawn.Vertices[0].Position;
        Vector2 front = drawn.Vertices[7 * 4].Position;

        Assert.Multiple(() =>
        {
            Assert.That(back.X - front.X, Is.EqualTo(0).Within(0.01f), "right, turned a quarter, is down");
            Assert.That(back.Y - front.Y, Is.EqualTo(10).Within(0.01f));
        });
    }

    [Test]
    public void ChangingOnlyItsOffsetMovesIt()
    {
        SpriteText sprite = add();
        sprite.Shadow = true;
        frame();
        float before = node().Vertices[0].Position.X;

        sprite.ShadowOffset = sprite.ShadowOffset + new Vector2(7, 0);
        frame();

        Assert.That(node().Vertices[0].Position.X, Is.EqualTo(before + 7).Within(0.01f));
    }

    [Test]
    public void ChangingOnlyItsColorReachesIt()
    {
        SpriteText sprite = add();
        sprite.Shadow = true;
        frame();

        sprite.ShadowColor = Color.Red;
        frame();

        Assert.That(node().Vertices[0].Color, Is.EqualTo(new Vector4(1, 0, 0, 1)));
    }

    [Test]
    public void TurningAShadowOffPutsTheVerticesBack()
    {
        SpriteText sprite = add();
        sprite.Shadow = true;
        frame();

        sprite.Shadow = false;
        frame();

        Assert.That(node().VertexCount, Is.EqualTo(7 * 4));
    }

    /// <summary>
    /// A space has no quads to draw in either copy, and the slots after it still line up.
    /// </summary>
    [Test]
    public void AShadowKeepsItsSlotsPastASpace()
    {
        SpriteText sprite = add("A B");
        sprite.Shadow = true;
        sprite.ShadowOffset = new Vector2(3, 0);
        frame();

        DrawNode drawn = node();

        Assert.Multiple(() =>
        {
            Assert.That(drawn.VertexCount, Is.EqualTo(3 * 4 * 2));
            Assert.That(drawn.Vertices[2 * 4].Position.X - drawn.Vertices[3 * 4 + 2 * 4].Position.X, Is.EqualTo(3).Within(0.01f), "the B's shadow against the B");
        });
    }

    /// <summary>
    /// In pixels, on the reference rasterizer (which samples a glyph's texture as white, so a glyph is its whole quad): the
    /// shadow shows where the text isn't, and the text is over it where they overlap.
    /// </summary>
    [Test]
    public void TheShadowIsDrawnBehindTheText()
    {
        var capture = new HeadlessRenderer(textureManager);
        capture.EnablePixelCapture(200, 100);

        var sprite = new SpriteText { Text = "H", Font = usage, Color = Color.White, Position = new Vector2(20, 10) };
        root.Add(sprite);
        sprite.Shadow = true;
        sprite.ShadowColor = Color.Red;
        sprite.ShadowOffset = new Vector2(3, 3);
        frame();

        Vector2 topLeft = node().Vertices[1 * 4 * 2 - 4].Position;
        Vector2 bottomRight = node().Vertices[1 * 4 * 2 - 2].Position;

        capture.Screen.Clear(new Vector4(0, 0, 0, 1));
        capture.SetRoot(root.GenerateDrawNodeSubtree(0));
        capture.Draw(new FramedClock(manual));

        PixelSurface screen = capture.Screen;

        Vector4 middle = screen[(int)((topLeft.X + bottomRight.X) / 2), (int)((topLeft.Y + bottomRight.Y) / 2)];
        Vector4 beyond = screen[(int)bottomRight.X + 1, (int)bottomRight.Y + 1];

        Assert.Multiple(() =>
        {
            Assert.That(middle.Y, Is.EqualTo(1).Within(0.01f), "white in the middle: the text's on top");
            Assert.That(beyond.X, Is.EqualTo(1).Within(0.01f), "red just past its corner: the shadow's showing");
            Assert.That(beyond.Y, Is.Zero.Within(0.01f));
        });
    }

    #endregion

    private partial class FontRoot : Container
    {
        public void Provide(IFontStore fontStore) => Cache(fontStore);
    }
}
