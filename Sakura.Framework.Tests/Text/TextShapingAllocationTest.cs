// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Text;

/// <summary>
/// What shaping a new string allocates besides its result. Text that changes every update, such as a score, shapes a new
/// string every update, so anything shaping allocates on the way is garbage at the update rate.
/// </summary>
[TestFixture]
public class TextShapingAllocationTest
{
    private const int strings_measured = 100;

    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;
    private FontUsage usage;
    private string[] scores = null!;

    /// <summary>
    /// Makes a store with one font, and the strings to shape: 8-digit scores, built ahead so making them isn't counted.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        store = new RendererFontStore(new HeadlessRenderer(textureManager));

        var fonts = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources").GetStorageForDirectory("Fonts");
        store.AddFont(fonts, "Comfortaa-Regular.ttf", alias: "Score");

        usage = FontUsage.Default.With(family: "Score", size: 16f);
        scores = new string[strings_measured * 2];

        for (int i = 0; i < scores.Length; i++)
        {
            scores[i] = (12345678 + i * 7).ToString("D8");
        }
    }

    /// <summary>
    /// Releases the store and its textures.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    /// <summary>
    /// Finding the font and the variation for a usage, which every string shaped does, allocates nothing.
    /// </summary>
    [Test]
    public void LookingUpAUsagesFontAndVariationAllocatesNothing()
    {
        store.Get(usage);
        store.GetVariation(usage);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < strings_measured; i++)
        {
            store.Get(usage);
            store.GetVariation(usage);
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    /// <summary>
    /// A font shaping a new 8-digit string allocates its result and nothing more: the glyph list (one array of 8 glyphs)
    /// and the <see cref="ShapedText"/>, about 340 B. With a copy of HarfBuzz's results, a second glyph list per run and a
    /// new scripts array, it came to 1,232 B.
    /// </summary>
    [Test]
    public void ShapingANewStringAllocatesOnlyItsResult()
    {
        var font = store.Get(usage);
        var fallbacks = store.GetFallbackSource(usage);
        var variation = store.GetVariation(usage);

        // The first half warms the glyphs and the pools; the second half is measured.
        for (int i = 0; i < strings_measured; i++)
        {
            font.ProcessText(scores[i], usage.Size, 1f, fallbacks, variation);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = strings_measured; i < scores.Length; i++)
        {
            font.ProcessText(scores[i], usage.Size, 1f, fallbacks, variation);
        }

        double perString = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)strings_measured;

        Assert.That(perString, Is.LessThan(400), $"{perString:N0} B a string");
    }

    /// <summary>
    /// A string asked for once is shaped for its caller alone, over the result the caller hands back, so text that
    /// changes every update allocates nothing at all.
    /// </summary>
    [Test]
    public void ShapingANewStringOverAHandedBackResultAllocatesNothing()
    {
        var own = store.Shape(usage, scores[0], 1f);

        for (int i = 1; i < strings_measured; i++)
        {
            own = store.Shape(usage, scores[i], 1f, own);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = strings_measured; i < scores.Length; i++)
        {
            own = store.Shape(usage, scores[i], 1f, own);
        }

        // Read before asserting: the assertions' own delegate would be counted.
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(allocated, Is.Zero);
            Assert.That(own.Glyphs, Has.Count.EqualTo(8));
        });
    }
}
