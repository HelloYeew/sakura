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
/// Tests truncation against real font metrics. The headless font store measures everything as zero, so
/// these drive a <see cref="RendererFontStore"/> over a headless renderer with a bundled font — the same
/// setup the shaping-cache tests use.
/// </summary>
[TestFixture]
public class TextTruncationTest
{
    private const string long_text = "The quick brown fox jumps over the lazy dog";

    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        store = new RendererFontStore(new HeadlessRenderer(textureManager));

        var fonts = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources")
            .GetStorageForDirectory("Fonts");

        store.AddFont(fonts, "Comfortaa-Regular.ttf", alias: "Truncated");
    }

    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    private static FontUsage usage(float size = 16f) => FontUsage.Default.With(family: "Truncated", size: size);

    private float width(string text) => store.Shape(usage(), text, 1f).BoundingBox.X;

    private TextTruncation.Result apply(string text, float maxWidth, string ellipsis = TextTruncation.DEFAULT_ELLIPSIS)
        => TextTruncation.Apply(store, usage(), text, 1f, maxWidth, ellipsis);

    [Test]
    public void TextThatFitsIsUntouched()
    {
        float full = width(long_text);

        var result = apply(long_text, full + 10);

        Assert.Multiple(() =>
        {
            Assert.That(result.Truncated, Is.False);
            Assert.That(result.Text, Is.EqualTo(long_text));
            Assert.That(result.Shaped.BoundingBox.X, Is.EqualTo(full).Within(0.01f));
        });
    }

    [Test]
    public void ExactFitIsNotTruncated()
    {
        float full = width(long_text);

        var result = apply(long_text, full);

        Assert.That(result.Truncated, Is.False, "a budget equal to the measured width is not an overflow");
        Assert.That(result.Text, Is.EqualTo(long_text));
    }

    [Test]
    public void UnlimitedBudgetIsANoOp()
    {
        var result = apply(long_text, float.PositiveInfinity);

        Assert.That(result.Truncated, Is.False);
        Assert.That(result.Text, Is.EqualTo(long_text));
    }

    [Test]
    public void LongTextIsShortenedWithAnEllipsis()
    {
        float budget = width(long_text) / 2f;

        var result = apply(long_text, budget);

        Assert.Multiple(() =>
        {
            Assert.That(result.Truncated, Is.True);
            Assert.That(result.Text, Does.EndWith(TextTruncation.DEFAULT_ELLIPSIS));
            Assert.That(result.Text, Is.Not.EqualTo(long_text));
            Assert.That(result.Shaped.BoundingBox.X, Is.LessThanOrEqualTo(budget));
        });
    }

    [Test]
    public void KeptTextIsAPrefixOfTheSource()
    {
        var result = apply(long_text, width(long_text) / 3f);

        string kept = result.Text[..^TextTruncation.DEFAULT_ELLIPSIS.Length];

        Assert.That(long_text, Does.StartWith(kept), $"'{kept}' is not the start of the source text");
    }

    /// <summary>
    /// The point of fitting: what is kept must be the longest that fits, not merely something that does.
    /// Swept across many budgets on purpose — at any single budget a result one character short can
    /// coincide with the correct one, because a cut landing on whitespace trims back to the same string.
    /// </summary>
    [Test]
    public void ResultIsAlwaysTheLongestThatFits()
    {
        float full = width(long_text);

        for (int i = 1; i < 60; i++)
        {
            float budget = full * i / 60f;
            var result = apply(long_text, budget);

            if (!result.Truncated)
                continue;

            // The next cut that composes a genuinely longer string than the one we got.
            string longer = null;

            for (int cut = result.Text.Length; cut <= long_text.Length; cut++)
            {
                string candidate = string.Concat(long_text.AsSpan(0, cut).TrimEnd(), TextTruncation.DEFAULT_ELLIPSIS);

                if (candidate.Length > result.Text.Length)
                {
                    longer = candidate;
                    break;
                }
            }

            if (longer == null)
                continue;

            Assert.That(width(longer), Is.GreaterThan(budget),
                $"budget {budget:F2} returned '{result.Text}' but '{longer}' would also have fit");
        }
    }

    [Test]
    public void AWiderBudgetKeepsMoreText()
    {
        float full = width(long_text);

        string narrow = apply(long_text, full * 0.3f).Text;
        string wide = apply(long_text, full * 0.6f).Text;

        Assert.That(wide.Length, Is.GreaterThan(narrow.Length));
        Assert.That(long_text, Does.StartWith(wide[..^TextTruncation.DEFAULT_ELLIPSIS.Length]));
    }

    [Test]
    public void TrailingWhitespaceIsTrimmedBeforeTheEllipsis()
    {
        // A budget that lands the cut inside the run of spaces.
        const string spaced = "Hello     world";
        float budget = width("Hello  " + TextTruncation.DEFAULT_ELLIPSIS);

        var result = apply(spaced, budget);

        Assert.That(result.Truncated, Is.True);
        Assert.That(result.Text, Does.Not.Match(@"\s" + TextTruncation.DEFAULT_ELLIPSIS + "$"), "whitespace was left in front of the ellipsis");
    }

    [Test]
    public void ABudgetThatOnlyFitsTheEllipsisKeepsNoText()
    {
        float budget = width(TextTruncation.DEFAULT_ELLIPSIS);

        var result = apply(long_text, budget);

        Assert.That(result.Truncated, Is.True);
        Assert.That(result.Text, Is.EqualTo(TextTruncation.DEFAULT_ELLIPSIS));
        Assert.That(result.Shaped.BoundingBox.X, Is.LessThanOrEqualTo(budget));
    }

    [Test]
    public void NothingIsDrawnWhenEvenTheEllipsisDoesNotFit()
    {
        float budget = width(TextTruncation.DEFAULT_ELLIPSIS) / 2f;

        var result = apply(long_text, budget);

        Assert.Multiple(() =>
        {
            Assert.That(result.Truncated, Is.True);
            Assert.That(result.Text, Is.Empty);
            Assert.That(result.Shaped.Glyphs, Is.Empty);
        });
    }

    [TestCase(0f)]
    [TestCase(-5f)]
    public void ANonPositiveBudgetKeepsNothing(float budget)
    {
        var result = apply(long_text, budget);

        Assert.That(result.Truncated, Is.True);
        Assert.That(result.Text, Is.Empty);
    }

    [Test]
    public void EmptyTextIsNeverTruncated()
    {
        var result = apply(string.Empty, 5f);

        Assert.That(result.Truncated, Is.False);
        Assert.That(result.Text, Is.Empty);
    }

    [Test]
    public void ACustomEllipsisIsUsed()
    {
        var result = apply(long_text, width(long_text) / 2f, "...");

        Assert.That(result.Text, Does.EndWith("..."));
        Assert.That(result.Text, Does.Not.Contain(TextTruncation.DEFAULT_ELLIPSIS));
    }

    [Test]
    public void AnEmptyEllipsisCutsWithNoMarker()
    {
        float budget = width(long_text) / 2f;

        var result = apply(long_text, budget, string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(result.Truncated, Is.True);
            Assert.That(long_text, Does.StartWith(result.Text));
            Assert.That(result.Text, Is.Not.Empty);
            Assert.That(result.Shaped.BoundingBox.X, Is.LessThanOrEqualTo(budget));
        });
    }

    /// <summary>
    /// A result may never contain half of a surrogate pair, whatever the budget. Measured against the
    /// real color-emoji font so the emoji carry their true (wide) advances.
    /// </summary>
    /// <remarks>
    /// This pins the invariant, not the mechanism: an implementation cutting on raw UTF-16 indices
    /// passes this too, because a lone surrogate measures the same as the whole pair here, so the fit
    /// search still settles past the pair. See the note on <see cref="TextTruncation"/> for why cuts are
    /// taken from cluster boundaries regardless.
    /// </remarks>
    [Test]
    public void SurrogatePairsAreNotSplit()
    {
        const string emoji = "a👍b👍c👍d👍e👍f👍g👍h";

        var frameworkFonts = new EmbeddedResourceStorage(typeof(App).Assembly, "Sakura.Framework.Resources")
            .GetStorageForDirectory("Fonts");
        store.AddFont(frameworkFonts, "NotoColorEmoji-Regular.ttf", alias: "TruncatedEmoji");

        var emojiUsage = FontUsage.Default.With(family: "TruncatedEmoji", size: 24f);
        float full = store.Shape(emojiUsage, emoji, 1f).BoundingBox.X;

        Assume.That(full, Is.GreaterThan(0), "the emoji font measured nothing");

        for (int step = 1; step < 40; step++)
        {
            var result = TextTruncation.Apply(store, emojiUsage, emoji, 1f, full * step / 40f);
            string text = result.Text;

            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]))
                {
                    Assert.That(i + 1, Is.LessThan(text.Length), $"high surrogate left dangling at {i} in '{text}'");
                    Assert.That(char.IsLowSurrogate(text[i + 1]), Is.True, $"broken pair at {i} in '{text}'");
                    i++;
                }
                else
                {
                    Assert.That(char.IsLowSurrogate(text[i]), Is.False, $"orphan low surrogate at {i} in '{text}'");
                }
            }
        }
    }

    /// <summary>
    /// Every result must respect the budget, whatever the budget is.
    /// </summary>
    [Test]
    public void NoBudgetIsEverExceeded()
    {
        float full = width(long_text);

        for (int i = 0; i <= 40; i++)
        {
            float budget = full * i / 20f;
            var result = apply(long_text, budget);

            Assert.That(result.Shaped.BoundingBox.X, Is.LessThanOrEqualTo(budget).Within(0.01f),
                $"budget {budget:F2} overflowed by '{result.Text}'");
        }
    }

    [Test]
    public void ShapedResultMatchesTheReturnedText()
    {
        var result = apply(long_text, width(long_text) / 2f);

        Assert.That(result.Shaped, Is.SameAs(store.Shape(usage(), result.Text, 1f)),
            "the returned shaping is the one for the returned text");
    }

    /// <summary>
    /// A store that cannot measure (the headless one) must not cause text to disappear.
    /// </summary>
    [Test]
    public void AStoreThatMeasuresNothingLeavesTextAlone()
    {
        using var headlessTextures = new HeadlessTextureManager();
        var headless = new HeadlessFontStore(headlessTextures);

        var result = TextTruncation.Apply(headless, usage(), long_text, 1f, 10f);

        Assert.That(result.Truncated, Is.False);
        Assert.That(result.Text, Is.EqualTo(long_text));
    }

    [Test]
    public void FittingCostsALogarithmicNumberOfShapings()
    {
        // Warm the cache for the full text so only fitting is measured.
        store.Shape(usage(), long_text, 1f);

        long before = Sakura.Framework.Statistic.GlobalStatistics.Get<long>("Fonts", "Text Shapes").Value;

        apply(long_text, width(long_text) / 2f);

        long shapings = Sakura.Framework.Statistic.GlobalStatistics.Get<long>("Fonts", "Text Shapes").Value - before;

        // 43 characters: walking back one cluster at a time would cost ~20 shapings, a binary search
        // over the cut points at most ceil(log2(44)) = 6.
        Assert.That(shapings, Is.LessThanOrEqualTo(6), $"fitting took {shapings} shapings");
    }

    [Test]
    public void RepeatedApplicationIsStable()
    {
        float budget = width(long_text) / 2f;

        string first = apply(long_text, budget).Text;
        string second = apply(long_text, budget).Text;

        Assert.That(second, Is.EqualTo(first));
    }
}
