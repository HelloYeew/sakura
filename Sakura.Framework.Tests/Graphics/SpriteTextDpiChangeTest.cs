// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Text must be re-rasterized when a window moves to a display with a different pixel density.
/// Otherwise, glyphs rendered for 1x are stretched on a 2x display and appear soft.
/// </summary>
/// <remarks>
/// Tests that a DPI change updates the font store scale and triggers text remeasurement.
/// </remarks>
[TestFixture]
public class SpriteTextDpiChangeTest
{
    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;
    private DependencyContainer dependencies = null!;

    private const string sample = "The quick brown fox";

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        store = new RendererFontStore(new HeadlessRenderer(textureManager));

        var fonts = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources")
            .GetStorageForDirectory("Fonts");

        store.AddFont(fonts, "Comfortaa-Regular.ttf", alias: "Sprite");

        dependencies = new DependencyContainer();
        dependencies.CacheAs<IFontStore>(store);
    }

    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    private static FontUsage usage => FontUsage.Default.With(family: "Sprite", size: 16f);

    private SpriteText sprite()
    {
        var textSprite = new SpriteText { Text = sample, Font = usage };
        DependencyActivator.Inject(textSprite, dependencies);
        return textSprite;
    }

    /// <summary>
    /// Shaping work actually performed — a cache miss that reached FreeType and HarfBuzz.
    /// </summary>
    private static long textShapes => GlobalStatistics.Get<long>("Fonts", "Text Shapes").Value;

    /// <summary>
    /// Requests the store answered from its cache. Rises whenever a sprite asks and is served.
    /// </summary>
    private static long shapeHits => GlobalStatistics.Get<long>("Fonts", "Shape Cache Hits").Value;

    [Test]
    public void MovingToADenserDisplayReshapesTheText()
    {
        var text = sprite();

        // Forces the first layout at 1x and settles the version the sprite recorded.
        _ = text.ContentSize;
        text.Update();
        _ = text.ContentSize;

        long shapesBefore = textShapes;

        store.SetDpiScale(2.0f);
        text.Update();

        // Reading the size runs the layout, which is where the re-shape would happen.
        _ = text.ContentSize;

        Assert.That(textShapes, Is.GreaterThan(shapesBefore),
            "the sprite kept its 1x layout after the display density doubled, so its glyphs are half-resolution");
    }

    [Test]
    public void ReshapingKeepsTheSameLogicalSize()
    {
        var text = sprite();

        _ = text.ContentSize;
        text.Update();
        var sizeAt1X = text.ContentSize;

        store.SetDpiScale(2.0f);
        text.Update();
        var sizeAt2X = text.ContentSize;

        // Glyphs are rasterised at twice the pixels and then divided back down, so the *layout* is the
        // same size in logical units and nothing around the text moves. A sprite that grew here would
        // reflow every parent on a display change, which is a worse bug than the soft text.
        //
        // The tolerance is a pixel: hinting and the integer pixel size FreeType is given mean the two
        // rasterisations are not required to agree exactly, only imperceptibly.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sizeAt2X.X, Is.EqualTo(sizeAt1X.X).Within(1.0f));
            Assert.That(sizeAt2X.Y, Is.EqualTo(sizeAt1X.Y).Within(1.0f));
        }
    }

    [Test]
    public void MovingBackToTheOriginalDisplayRemeasuresFromCache()
    {
        var text = sprite();

        _ = text.ContentSize;
        text.Update();
        _ = text.ContentSize;

        store.SetDpiScale(2.0f);
        text.Update();
        _ = text.ContentSize;

        long shapesBefore = textShapes;
        long hitsBefore = shapeHits;

        store.SetDpiScale(1.0f);
        text.Update();
        _ = text.ContentSize;

        // Two separate claims, and they need separate counters to tell apart. A rise in *hits* is the
        // sprite asking the store again, which is the invalidation working — going back down is the
        // direction that looks plausible at a glance (an oversized glyph downsampled reads as slightly
        // soft rather than obviously broken), so it is worth asserting rather than eyeballing.
        //
        // No rise in *shapes* is the 1x entry still being in the cache from the first layout. Dragging a
        // window back and forth between two displays is a thing people do repeatedly, and it costs
        // nothing after the first trip because the shape key includes the scale and both live at once.
        Assert.Multiple(() =>
        {
            Assert.That(shapeHits, Is.GreaterThan(hitsBefore),
                "the sprite kept its 2x layout after moving back to a 1x display");
            Assert.That(textShapes, Is.EqualTo(shapesBefore),
                "moving back re-rasterised glyphs that were already cached at this scale");
        });
    }

    [Test]
    public void AScaleThatDidNotChangeCostsNothing()
    {
        var text = sprite();

        _ = text.ContentSize;
        text.Update();
        _ = text.ContentSize;

        int versionBefore = store.DpiScaleVersion;
        long shapesBefore = textShapes;

        // The resize path calls this unconditionally, including on every ordinary window resize, so a
        // repeated value must not re-rasterise every glyph in the app.
        store.SetDpiScale(store.DpiScale);
        text.Update();
        _ = text.ContentSize;

        Assert.Multiple(() =>
        {
            Assert.That(store.DpiScaleVersion, Is.EqualTo(versionBefore));
            Assert.That(textShapes, Is.EqualTo(shapesBefore));
        });
    }

    [Test]
    public void ANonsenseScaleIsIgnored()
    {
        float before = store.DpiScale;

        // A window reports a zero size while minimised, and the resize path divides by it. Letting a 0
        // or NaN through would multiply into a glyph size of zero and take out every piece of text in
        // the app, which is a good deal worse than the soft text this all exists to fix.
        store.SetDpiScale(0f);
        store.SetDpiScale(-2f);
        store.SetDpiScale(float.NaN);
        store.SetDpiScale(float.PositiveInfinity);

        Assert.That(store.DpiScale, Is.EqualTo(before));
    }

    [Test]
    public void GlyphsAreRasterisedLargerAtAHigherScale()
    {
        // The reason any of this matters, asserted at the source: the same text at the same logical
        // font size occupies more physical pixels on a denser display. If this ever stopped being true,
        // re-shaping on a DPI change would be pointless work.
        var at1X = store.Shape(usage, sample, 1.0f);
        var at2X = store.Shape(usage, sample, 2.0f);

        var glyph1X = at1X.Glyphs[0];
        var glyph2X = at2X.Glyphs[0];

        Assert.That(glyph2X.Texture!.Width, Is.GreaterThan(glyph1X.Texture!.Width),
            "a 2x glyph should carry roughly twice the pixels of the 1x one");
    }
}
