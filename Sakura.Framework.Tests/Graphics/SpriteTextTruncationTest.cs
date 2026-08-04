// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Truncation through <see cref="SpriteText"/> itself: that the properties feed the layout, that changing
/// one re-measures, and that the drawable ends up no wider than its budget.
/// </summary>
[TestFixture]
public class SpriteTextTruncationTest
{
    private readonly string longText = "The quick brown fox jumps over the lazy dog";

    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;
    private DependencyContainer dependencies = null!;

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
        dependencies.CacheAs<IWindow>(new HeadlessWindow());
    }

    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    private static FontUsage usage => FontUsage.Default.With(family: "Sprite", size: 16f);

    private SpriteText sprite(string text = "The quick brown fox jumps over the lazy dog")
    {
        var textSprite = new SpriteText
        {
            Text = text,
            Font = usage
        };

        DependencyActivator.Inject(textSprite, dependencies);
        return textSprite;
    }

    private float fullWidth => store.Shape(usage, longText, 1f).BoundingBox.X;

    [Test]
    public void TruncationIsOffByDefault()
    {
        var text = sprite();

        Assert.Multiple(() =>
        {
            Assert.That(text.Truncate, Is.False);
            Assert.That(text.MaxWidth, Is.EqualTo(float.PositiveInfinity));
            Assert.That(text.IsTruncated, Is.False);
            Assert.That(text.DisplayedText, Is.EqualTo(longText));
        });
    }

    [Test]
    public void AMaxWidthWithoutTruncateChangesNothing()
    {
        var text = sprite();
        text.MaxWidth = 50;

        Assert.That(text.IsTruncated, Is.False);
        Assert.That(text.ContentSize.X, Is.EqualTo(fullWidth).Within(0.01f), "the sprite still measures its full text");
    }

    [Test]
    public void TruncatingKeepsTheSpriteWithinItsBudget()
    {
        float budget = fullWidth / 2f;

        var text = sprite();
        text.Truncate = true;
        text.MaxWidth = budget;

        Assert.Multiple(() =>
        {
            Assert.That(text.IsTruncated, Is.True);
            Assert.That(text.ContentSize.X, Is.LessThanOrEqualTo(budget));
            Assert.That(text.Width, Is.LessThanOrEqualTo(budget), "Size follows the truncated content");
            Assert.That(text.DisplayedText, Does.EndWith(TextTruncation.DEFAULT_ELLIPSIS));
            Assert.That(text.Text, Is.EqualTo(longText), "the source text is left as the caller set it");
        });
    }

    [Test]
    public void TextThatFitsIsNotTruncated()
    {
        var text = sprite("Hi");
        text.Truncate = true;
        text.MaxWidth = fullWidth;

        Assert.That(text.IsTruncated, Is.False);
        Assert.That(text.DisplayedText, Is.EqualTo("Hi"));
    }

    /// <summary>
    /// Each of these setters has to invalidate the layout, or the sprite keeps showing a stale measurement.
    /// </summary>
    [Test]
    public void WideningTheBudgetReMeasures()
    {
        var text = sprite();
        text.Truncate = true;
        text.MaxWidth = fullWidth * 0.3f;

        string narrow = text.DisplayedText;
        float narrowWidth = text.ContentSize.X;

        text.MaxWidth = fullWidth * 0.7f;

        Assert.That(text.DisplayedText.Length, Is.GreaterThan(narrow.Length));
        Assert.That(text.ContentSize.X, Is.GreaterThan(narrowWidth));
        Assert.That(text.ContentSize.X, Is.LessThanOrEqualTo(fullWidth * 0.7f));
    }

    [Test]
    public void ChangingTheEllipsisReMeasures()
    {
        var text = sprite();
        text.Truncate = true;
        text.MaxWidth = fullWidth / 2f;

        string withDefault = text.DisplayedText;

        text.Ellipsis = "...";

        Assert.That(text.DisplayedText, Does.EndWith("..."));
        Assert.That(text.DisplayedText, Is.Not.EqualTo(withDefault));
        Assert.That(text.ContentSize.X, Is.LessThanOrEqualTo(fullWidth / 2f));
    }

    [Test]
    public void DisablingTruncationRestoresTheFullText()
    {
        var text = sprite();
        text.Truncate = true;
        text.MaxWidth = fullWidth / 2f;

        Assert.That(text.IsTruncated, Is.True);

        text.Truncate = false;

        Assert.Multiple(() =>
        {
            Assert.That(text.IsTruncated, Is.False);
            Assert.That(text.DisplayedText, Is.EqualTo(longText));
            Assert.That(text.ContentSize.X, Is.EqualTo(fullWidth).Within(0.01f));
        });
    }

    [Test]
    public void ANewTextIsTruncatedToo()
    {
        var text = sprite("Hi");
        text.Truncate = true;
        text.MaxWidth = fullWidth / 2f;

        Assert.That(text.IsTruncated, Is.False);

        text.Text = longText;

        Assert.That(text.IsTruncated, Is.True);
        Assert.That(text.ContentSize.X, Is.LessThanOrEqualTo(fullWidth / 2f));
    }

    [Test]
    public void AnEmptyEllipsisCutsWithNoMarker()
    {
        var text = sprite();
        text.Truncate = true;
        text.MaxWidth = fullWidth / 2f;
        text.Ellipsis = string.Empty;

        Assert.That(text.DisplayedText, Does.Not.Contain(TextTruncation.DEFAULT_ELLIPSIS));
        Assert.That(longText, Does.StartWith(text.DisplayedText));
        Assert.That(text.ContentSize.X, Is.LessThanOrEqualTo(fullWidth / 2f));
    }

    [Test]
    public void ABudgetTooSmallForTheEllipsisShowsNothing()
    {
        var text = sprite();
        text.Truncate = true;
        text.MaxWidth = 1;

        Assert.That(text.IsTruncated, Is.True);
        Assert.That(text.DisplayedText, Is.Empty);
        Assert.That(text.ContentSize.X, Is.Zero);
    }
}
