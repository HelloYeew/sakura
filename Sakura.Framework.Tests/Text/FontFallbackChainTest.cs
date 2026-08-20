// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Text;

/// <summary>
/// Tests for <see cref="RendererFontStore.GetFallbacks(FontUsage,FontScript)"/> the priority tiers a
/// chain is ordered by, and that it is resolved one family at a time as it is walked.
/// </summary>
[TestFixture]
public class FontFallbackChainTest
{
    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;
    private Storage fonts = null!;

    private static int loadedFonts => GlobalStatistics.Get<int>("Fonts", "Loaded Fonts").Value;

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        store = new RendererFontStore(new HeadlessRenderer(textureManager));

        fonts = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources")
            .GetStorageForDirectory("Fonts");

        store.AddFont(fonts, "Comfortaa-Regular.ttf", alias: "FallbackOne");
        store.AddFont(fonts, "Comfortaa-Medium.ttf", alias: "FallbackTwo");
        store.AddFont(fonts, "Comfortaa-Bold.ttf", alias: "FallbackThree");
    }

    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    [Test]
    public void AskingForTheChainLoadsNothing()
    {
        store.AddFallbackFamily("FallbackOne");
        store.AddFallbackFamily("FallbackTwo");

        int before = loadedFonts;

        // SpriteText asks for the chain on every layout but only walks it for a codepoint its primary
        // font lacks, so merely obtaining it must not touch a font file.
        store.GetFallbacks(FontUsage.Default);

        Assert.That(loadedFonts, Is.EqualTo(before));
    }

    [Test]
    public void WalkingTheChainLoadsOneFontPerStep()
    {
        store.AddFallbackFamily("FallbackOne");
        store.AddFallbackFamily("FallbackTwo");
        store.AddFallbackFamily("FallbackThree");

        int before = loadedFonts;

        using var enumerator = store.GetFallbacks(FontUsage.Default).GetEnumerator();

        Assert.That(enumerator.MoveNext(), Is.True);
        Assert.That(loadedFonts - before, Is.EqualTo(1), "the first step must not load the rest of the chain");

        Assert.That(enumerator.MoveNext(), Is.True);
        Assert.That(loadedFonts - before, Is.EqualTo(2));
    }

    [Test]
    public void ChainIsCachedSoRepeatedLayoutsReloadNothing()
    {
        store.AddFallbackFamily("FallbackOne");
        store.AddFallbackFamily("FallbackTwo");

        int before = loadedFonts;

        var first = walk();
        int afterFirstWalk = loadedFonts;
        var second = walk();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterFirstWalk - before, Is.EqualTo(2));
            Assert.That(loadedFonts, Is.EqualTo(afterFirstWalk), "a resolved chain must be reused, not re-resolved");
            Assert.That(second, Is.EqualTo(first));
        }
    }

    [Test]
    public void ChainIsInRegistrationOrder()
    {
        store.AddFallbackFamily("FallbackTwo");
        store.AddFallbackFamily("FallbackOne");

        var chain = walk();

        Assert.That(chain.Select(f => f.Name), Is.EqualTo(new[] { "FallbackTwo", "FallbackOne" }));
    }

    [Test]
    public void RegisteringAFamilyInvalidatesAnAlreadyWalkedChain()
    {
        store.AddFallbackFamily("FallbackOne");

        Assert.That(walk(), Has.Count.EqualTo(1));

        store.AddFallbackFamily("FallbackTwo");

        Assert.That(walk().Select(f => f.Name), Is.EqualTo(new[] { "FallbackOne", "FallbackTwo" }));
    }

    [Test]
    public void AFamilyRegisteredWithoutBeingLoadedIsSkipped()
    {
        store.AddFallbackFamily("NeverRegistered");
        store.AddFallbackFamily("FallbackOne");

        Assert.That(walk().Select(f => f.Name), Is.EqualTo(new[] { "FallbackOne" }));
    }

    /// <summary>
    /// The priority inversion this whole mechanism exists to fix: the framework registers its own
    /// families during <see cref="RendererFontStore.LoadDefaultFont"/>, which runs before any
    /// application code, so with one ordered list an application could never reach its own font for a
    /// script the framework already covers.
    /// </summary>
    [Test]
    public void AnApplicationClaimOutranksTheFrameworkFamilyForTheSameScript()
    {
        store.LoadDefaultFont(frameworkFonts());
        store.AddFallbackFamily("FallbackOne", FontScript.Japanese);

        Assert.That(firstFallbackFor(FontScript.Japanese), Is.EqualTo("FallbackOne"));
    }

    /// <summary>
    /// A claim is scoped to its script: it must not become the answer for every script the family
    /// happens to cover, which is what inserting it at the front of a single list would do.
    /// </summary>
    [Test]
    public void AClaimDoesNotApplyToOtherScripts()
    {
        store.AddFallbackFamily("FallbackOne", FontScript.Thai);
        store.AddFallbackFamily("FallbackTwo");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstFallbackFor(FontScript.Thai), Is.EqualTo("FallbackOne"));
            Assert.That(firstFallbackFor(FontScript.Latin), Is.EqualTo("FallbackTwo"));
        }
    }

    /// <summary>
    /// An application shipping one CJK family should not have to claim ideographs separately: kana and
    /// kanji sit in the same sentence and must be drawn by the same font.
    /// </summary>
    [Test]
    public void IdeographsDrawOnTheApplicationsCjkClaim()
    {
        store.AddFallbackFamily("FallbackOne", FontScript.Japanese);
        store.AddFallbackFamily("FallbackTwo");

        Assert.That(firstFallbackFor(FontScript.Han), Is.EqualTo("FallbackOne"));
    }

    /// <summary>
    /// With no application claim to go on, ideographs follow the configured language.
    /// </summary>
    [Test]
    public void HanScriptDecidesWhichFrameworkFamilyDrawsIdeographs()
    {
        store.LoadDefaultFont(frameworkFonts());

        store.HanScript = FontScript.Japanese;
        string? japanese = firstFallbackFor(FontScript.Han);

        store.HanScript = FontScript.Korean;
        string? korean = firstFallbackFor(FontScript.Han);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(japanese, Does.StartWith("NotoSansJP"));
            Assert.That(korean, Does.StartWith("NotoSansKR"), "changing the preference must invalidate the resolved chain");
        }
    }

    /// <summary>
    /// Auto derives claims from what the font covers, so a family that cannot draw a script never leads
    /// that script's chain.
    /// </summary>
    [Test]
    public void AutoClaimsOnlyScriptsTheFontCovers()
    {
        // Comfortaa covers Latin, Cyrillic and Greek, and nothing else.
        store.AddFallbackFamily("FallbackOne", FontScript.Auto);
        store.AddFallbackFamily("FallbackTwo");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstFallbackFor(FontScript.Cyrillic), Is.EqualTo("FallbackOne"));
            Assert.That(firstFallbackFor(FontScript.Thai), Is.EqualTo("FallbackTwo"));
        }
    }

    /// <summary>
    /// Latin is deliberately excluded from what Auto claims. Nearly every font covers it, so claiming it
    /// would let a font registered for its Japanese or Thai coverage supply Latin glyphs to labels that
    /// asked for a different family — the over-claiming the script tiers exist to prevent.
    /// </summary>
    [Test]
    public void AutoNeverClaimsLatin()
    {
        store.AddFallbackFamily("FallbackOne", FontScript.Auto);
        store.AddFallbackFamily("FallbackTwo");

        Assert.That(firstFallbackFor(FontScript.Latin), Is.EqualTo("FallbackTwo"));
    }

    /// <summary>
    /// An explicit claim is not overwritten by a derived one, so two auto-registered families resolve in
    /// registration order rather than fighting.
    /// </summary>
    [Test]
    public void AnExplicitClaimBeatsADerivedOne()
    {
        store.AddFallbackFamily("FallbackTwo", FontScript.Cyrillic);
        store.AddFallbackFamily("FallbackOne", FontScript.Auto);

        Assert.That(firstFallbackFor(FontScript.Cyrillic), Is.EqualTo("FallbackTwo"));
    }

    /// <summary>
    /// Scripts nobody claimed still resolve through every registered family, so an unclaimed script
    /// renders in a wrong-but-present font rather than as missing glyphs.
    /// </summary>
    [Test]
    public void AnUnclaimedScriptStillReachesEveryFamily()
    {
        store.AddFallbackFamily("FallbackOne", FontScript.Thai);
        store.AddFallbackFamily("FallbackTwo", FontScript.Hebrew);

        Assert.That(store.GetFallbacks(FontUsage.Default, FontScript.Arabic).Select(f => f.Name),
            Is.EquivalentTo(new[] { "FallbackOne", "FallbackTwo" }));
    }

    /// <summary>
    /// The chain is asked for on every layout, so a claim must not cost a font load until something
    /// actually walks it.
    /// </summary>
    [Test]
    public void AskingForAScriptChainLoadsNothing()
    {
        store.AddFallbackFamily("FallbackOne", FontScript.Thai);

        int before = loadedFonts;

        store.GetFallbacks(FontUsage.Default, FontScript.Thai);

        Assert.That(loadedFonts, Is.EqualTo(before));
    }

    /// <summary>
    /// The name of the first font a script's chain resolves to. Enumerated lazily so the assertion does
    /// not load the rest of the chain.
    /// </summary>
    private string? firstFallbackFor(FontScript script)
        => store.GetFallbacks(FontUsage.Default, script).Select(f => f.Name).FirstOrDefault();

    private static Storage frameworkFonts()
        => new EmbeddedResourceStorage(typeof(RendererFontStore).Assembly, "Sakura.Framework.Resources").GetStorageForDirectory("Fonts");

    private List<Font> walk() => store.GetFallbacks(FontUsage.Default).ToList();
}
