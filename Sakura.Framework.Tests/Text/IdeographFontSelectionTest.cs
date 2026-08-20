// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Text;

/// <summary>
/// Unified CJK ideographs are the one script resolved per word rather than per codepoint, because the
/// chain for them is ordered by an assumed language: resolving each character on its own draws a word
/// half in one language's font and half in another's wherever their coverage differs.
/// </summary>
[TestFixture]
public class IdeographFontSelectionTest
{
    /// <summary>
    /// 好 is in both bundled CJK families; 东 is simplified-only, so NotoSansJP cannot draw it.
    /// </summary>
    private const string mixed_coverage_word = "好东";

    private static long shapedRuns => GlobalStatistics.Get<long>("Fonts", "Shaped Runs").Value;

    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        store = new RendererFontStore(new HeadlessRenderer(textureManager));

        store.LoadDefaultFont(new EmbeddedResourceStorage(typeof(RendererFontStore).Assembly, "Sakura.Framework.Resources")
            .GetStorageForDirectory("Fonts"));
    }

    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    /// <summary>
    /// One font for the whole word, even though the preferred one covers only part of it.
    /// </summary>
    [Test]
    public void AWordOfIdeographsIsDrawnByOneFont()
    {
        long before = shapedRuns;

        store.Shape(new FontUsage("NotoSansJP", 16), mixed_coverage_word, 1f);

        Assert.That(shapedRuns - before, Is.EqualTo(1), "a word split across two fonts would shape as two runs");
    }

    /// <summary>
    /// And it is the font covering the whole word that wins, not the one asked for — a character the
    /// requested font can draw still moves, so the word does not change face halfway through.
    /// </summary>
    [Test]
    public void TheCoveringFontWinsOverThePreferredOne()
    {
        var usage = new FontUsage("NotoSansJP", 16);

        // 好 on its own is drawn by the requested font, which covers it.
        var alone = store.Shape(usage, "好", 1f);
        // In the word it must come from the family that also covers 东, so both characters match.
        var inWord = store.Shape(usage, mixed_coverage_word, 1f);

        Assert.That(alone.Glyphs, Is.Not.Empty);
        Assert.That(inWord.Glyphs, Has.Count.EqualTo(2));

        // Glyphs are cached per font, so a different texture instance means a different font drew it.
        Assert.That(inWord.Glyphs[0].Texture, Is.Not.SameAs(alone.Glyphs[0].Texture));
    }

    /// <summary>
    /// With nothing to disambiguate them, ideographs follow the configured language.
    /// </summary>
    [Test]
    public void ACoveredWordStaysWithTheLanguagesFont()
    {
        var usage = new FontUsage("NotoSans", 16);

        store.HanScript = FontScript.Japanese;
        var japanese = store.Shape(usage, "好", 1f);

        store.HanScript = FontScript.ChineseSimplified;
        var chinese = store.Shape(usage, "好", 1f);

        Assert.That(japanese.Glyphs, Is.Not.Empty);
        Assert.That(chinese.Glyphs, Is.Not.Empty);
        Assert.That(chinese.Glyphs[0].Texture, Is.Not.SameAs(japanese.Glyphs[0].Texture));
    }
}
