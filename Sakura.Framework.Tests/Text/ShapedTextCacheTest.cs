// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Text;

/// <summary>
/// Tests for the shaped-text cache: every layout used to re-shape from scratch, allocating a glyph list
/// per call plus a fresh managed array per HarfBuzz buffer read
/// </summary>
[TestFixture]
public class ShapedTextCacheTest
{
    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;
    private Storage fonts = null!;

    private static long textShapes => GlobalStatistics.Get<long>("Fonts", "Text Shapes").Value;
    private static long hits => GlobalStatistics.Get<long>("Fonts", "Shape Cache Hits").Value;
    private static int entries => GlobalStatistics.Get<int>("Fonts", "Shaped Text Entries").Value;

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        store = new RendererFontStore(new HeadlessRenderer(textureManager));

        fonts = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources")
            .GetStorageForDirectory("Fonts");

        store.AddFont(fonts, "Comfortaa-Regular.ttf", alias: "Cached");
    }

    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    private static FontUsage usage(float size = 16f) => FontUsage.Default.With(family: "Cached", size: size);

    /// <summary>
    /// Asks for <paramref name="text"/> twice, so it's shaped into the cache.
    /// </summary>
    private ShapedText shapeShared(string text, float size = 16f)
    {
        store.Shape(usage(size), text, 1f);
        return store.Shape(usage(size), text, 1f);
    }

    /// <summary>
    /// The first ask is shaped for its caller alone, the second is shaped into the cache, and every ask after that is
    /// a hit.
    /// </summary>
    [Test]
    public void RepeatedTextIsShapedTwiceThenShared()
    {
        long before = textShapes;

        for (int i = 0; i < 20; i++)
            store.Shape(usage(), "hello", 1f);

        Assert.That(textShapes - before, Is.EqualTo(2), "twenty layouts of unchanged text is two shapes");
    }

    [Test]
    public void AHitReturnsTheSameInstance()
    {
        var shared = shapeShared("hello");
        var hit = store.Shape(usage(), "hello", 1f);

        Assert.That(hit, Is.SameAs(shared));
    }

    /// <summary>
    /// Text asked for once, such as a score that changes every update, is shaped for its caller and kept out of the
    /// cache, so the cache doesn't fill with entries nothing reads again.
    /// </summary>
    [Test]
    public void TextAskedForOnceIsNotShared()
    {
        var once = store.Shape(usage(), "once", 1f);

        Assert.Multiple(() =>
        {
            Assert.That(once.Refillable, Is.True);
            Assert.That(once.Glyphs, Has.Count.EqualTo(4));
            Assert.That(entries, Is.Zero);
        });
    }

    /// <summary>
    /// A text shaped for one caller is shaped over the result that caller hands back, and comes out as a new result
    /// would.
    /// </summary>
    [Test]
    public void AOneOffShapeIsShapedOverTheResultHandedBack()
    {
        var own = store.Shape(usage(), "first", 1f);
        var next = store.Shape(usage(), "two", 1f, own);

        var font = store.Get(usage());
        var fresh = font.ProcessText("two", 16f, 1f, store.GetFallbackSource(usage()), store.GetVariation(usage()));

        Assert.Multiple(() =>
        {
            Assert.That(next, Is.SameAs(own));
            Assert.That(next.Glyphs, Is.EqualTo(fresh.Glyphs));
            Assert.That(next.BoundingBox, Is.EqualTo(fresh.BoundingBox));
        });
    }

    /// <summary>
    /// A shared result is read by every caller showing its text, so it's never shaped over, whoever hands it back.
    /// </summary>
    [Test]
    public void ASharedResultHandedBackIsLeftAlone()
    {
        var shared = shapeShared("shared");
        var other = store.Shape(usage(), "other", 1f, shared);

        Assert.Multiple(() =>
        {
            Assert.That(other, Is.Not.SameAs(shared));
            Assert.That(shared.Glyphs, Has.Count.EqualTo(6));
            Assert.That(store.Shape(usage(), "shared", 1f), Is.SameAs(shared));
        });
    }

    /// <summary>
    /// Each font usage remembers its own misses: one whose text changes all the time can't push out another's before
    /// it's asked for again.
    /// </summary>
    [Test]
    public void EachFontUsageRemembersItsOwnMisses()
    {
        store.Shape(usage(24f), "label", 1f);

        for (int i = 0; i < 200; i++)
        {
            store.Shape(usage(), $"{i:D8}", 1f);
        }

        var second = store.Shape(usage(24f), "label", 1f);

        Assert.That(store.Shape(usage(24f), "label", 1f), Is.SameAs(second), "the second ask was shared");
    }

    /// <summary>
    /// A usage remembers only its latest misses, so text that comes round again only after many others is shaped
    /// for its caller again.
    /// </summary>
    [Test]
    public void AMissPastAUsagesLatestIsForgotten()
    {
        store.Shape(usage(), "label", 1f);

        for (int i = 0; i < 200; i++)
        {
            store.Shape(usage(), $"{i:D8}", 1f);
        }

        Assert.That(store.Shape(usage(), "label", 1f).Refillable, Is.True);
    }

    /// <summary>
    /// A hit has to be allocation-free, or the cache trades one kind of churn for another.
    /// </summary>
    [Test]
    public void AHitAllocatesNothing()
    {
        // Warm both the entry and everything on the hit path (statistics objects, the LinkedList node).
        shapeShared("hello");
        store.Shape(usage(), "hello", 1f);

        // Built outside the measured loop deliberately: FontUsage.With allocates ~24 bytes a call, which
        // would otherwise be attributed to the cache. Nothing on the real layout path calls it — SpriteText
        // holds its usage — so it is the test helper that has to stay out of the way, not the cache.
        var u = usage();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100; i++)
            store.Shape(u, "hello", 1f);

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    [Test]
    public void DifferentTextShapesSeparately()
    {
        long before = textShapes;

        store.Shape(usage(), "hello", 1f);
        store.Shape(usage(), "world", 1f);

        Assert.That(textShapes - before, Is.EqualTo(2));
    }

    /// <summary>
    /// Size and scale change the rasterized pixels, so they are part of the result's identity — the same
    /// string at the same logical size shapes differently on a retina display.
    /// </summary>
    [Test]
    public void SizeAndDpiScaleAreBothPartOfTheKey()
    {
        long before = textShapes;

        store.Shape(usage(16f), "hello", 1f);
        store.Shape(usage(32f), "hello", 1f);
        store.Shape(usage(16f), "hello", 2f);

        Assert.That(textShapes - before, Is.EqualTo(3));
    }

    [Test]
    public void EmptyTextIsNeitherShapedNorCached()
    {
        long beforeShapes = textShapes;
        long beforeHits = hits;

        Assert.That(store.Shape(usage(), "", 1f), Is.SameAs(ShapedText.Empty));

        Assert.Multiple(() =>
        {
            Assert.That(textShapes, Is.EqualTo(beforeShapes));
            Assert.That(hits, Is.EqualTo(beforeHits), "the short circuit is before the cache, so it is not a hit either");
        });
    }

    /// <summary>
    /// The cache is resident by design, so it must stay bounded — otherwise it becomes the next thing a
    /// profile reads as a leak.
    /// </summary>
    [Test]
    public void TheCacheIsBounded()
    {
        store.ShapeCacheSize = 8;

        for (int i = 0; i < 100; i++)
        {
            shapeShared($"text {i}");
        }

        Assert.That(entries, Is.EqualTo(8));
    }

    /// <summary>
    /// Least-recently-used, not least-recently-added: text that is still on screen is asked for every
    /// layout, and evicting it while a scrolling list churns past would defeat the cache exactly when it
    /// is needed.
    /// </summary>
    [Test]
    public void EvictionIsLeastRecentlyUsed()
    {
        store.ShapeCacheSize = 3;

        shapeShared("keep");
        shapeShared("a");
        shapeShared("b");

        // Keep "keep" in use while the others age out.
        for (int i = 0; i < 5; i++)
        {
            store.Shape(usage(), "keep", 1f);
            shapeShared($"churn {i}");
        }

        long before = textShapes;
        store.Shape(usage(), "keep", 1f);

        Assert.That(textShapes, Is.EqualTo(before), "the entry in continuous use must have survived");
    }

    /// <summary>
    /// Mandatory, not tidy: a cached glyph holds the atlas texture it was rasterized into, so surviving a
    /// <see cref="RendererFontStore.ClearCaches"/> would mean drawing text from destroyed textures.
    /// </summary>
    [Test]
    public void ClearingCachesDropsShapedResults()
    {
        var shared = shapeShared("hello");

        store.ClearCaches();

        long before = textShapes;
        var after = store.Shape(usage(), "hello", 1f);

        Assert.Multiple(() =>
        {
            Assert.That(textShapes - before, Is.EqualTo(1), "the result must have been re-shaped, not reused");
            Assert.That(after, Is.Not.SameAs(shared));
            Assert.That(entries, Is.EqualTo(1), "text that came round before is shared again at once");
        });
    }

    /// <summary>
    /// Registering a fallback family changes which font covers a codepoint, so results shaped before it
    /// was registered may now be wrong.
    /// </summary>
    [Test]
    public void ChangingTheFallbackChainDropsShapedResults()
    {
        shapeShared("hello");

        store.AddFallbackFamily("Cached");

        long before = textShapes;
        store.Shape(usage(), "hello", 1f);

        Assert.That(textShapes - before, Is.EqualTo(1));
    }

    [Test]
    public void RegisteringAFontDropsShapedResults()
    {
        shapeShared("hello");

        // A newly registered font can make a previously unresolvable family resolvable.
        store.AddFont(fonts, "Comfortaa-Bold.ttf", alias: "LateArrival");

        long before = textShapes;
        store.Shape(usage(), "hello", 1f);

        Assert.That(textShapes - before, Is.EqualTo(1));
    }

    [Test]
    public void AnUnresolvableUsageIsNotCached()
    {
        var missing = FontUsage.Default.With(family: "NoSuchFamily");

        Assert.That(store.Shape(missing, "hello", 1f), Is.SameAs(ShapedText.Empty));
    }
}
