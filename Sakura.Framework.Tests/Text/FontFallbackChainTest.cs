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
/// Tests for <see cref="RendererFontStore.GetFallbacks"/> — specifically that the chain is resolved one
/// family at a time as it is walked
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

    private List<Font> walk() => store.GetFallbacks(FontUsage.Default).ToList();
}
