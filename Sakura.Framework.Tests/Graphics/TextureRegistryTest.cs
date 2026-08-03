// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests for <see cref="TextureRegistry"/>
/// </summary>
[TestFixture]
public class TextureRegistryTest
{
    [SetUp]
    public void SetUp() => TextureRegistry.Reset();

    [TearDown]
    public void TearDown() => TextureRegistry.Reset();

    private static Texture texture(int width = 64, int height = 64, string? name = null)
        => new Texture(new HeadlessNativeTexture(width, height))
        {
            Name = name
        };

    [Test]
    public void ConstructingATextureRegistersIt()
    {
        var tex = texture(name: "cover");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(1));
            Assert.That(TextureRegistry.GetAll(), Does.Contain(tex));
        }
    }

    [Test]
    public void ATextureWithNoCacheKeyIsStillTracked()
    {
        // The whole point of splitting the registry from the cache: cover art and other pixel-data
        // textures are created without a key, and used to be invisible to every tool.
        var tex = texture(128, 128);

        Assert.That(TextureRegistry.GetAll(), Does.Contain(tex));
    }

    [Test]
    public void LiveBytesCountsWholeTextures()
    {
        texture(100, 50);

        Assert.That(TextureRegistry.LiveBytes, Is.EqualTo(100 * 50 * 4));
    }

    [Test]
    public void AtlasSlicesDoNotDoubleCountTheirPage()
    {
        var page = new HeadlessNativeTexture(1024, 1024);

        // A whole-page view plus two slices of it: only the page's own allocation should be counted.
        _ = new Texture(page);
        _ = new Texture(page, new RectangleF(0, 0, 0.25f, 0.25f));
        _ = new Texture(page, new RectangleF(0.5f, 0.5f, 0.25f, 0.25f));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(1), "only the page owns an allocation");
            Assert.That(TextureRegistry.LiveSliceCount, Is.EqualTo(2), "the slices are tracked, separately");
            Assert.That(TextureRegistry.LiveBytes, Is.EqualTo(1024L * 1024 * 4), "and slices share the page's memory");
        }
    }
    
    [Test]
    public void GlyphSlicesDoNotMoveTheLiveTextureCount()
    {
        var atlasPage = new HeadlessNativeTexture(1024, 1024);
        _ = new Texture(atlasPage);

        int countWithPageOnly = TextureRegistry.LiveCount;

        // A screen's worth of glyphs sliced out of that page.
        for (int i = 0; i < 500; i++)
            _ = new Texture(atlasPage, new RectangleF(0, 0, 0.01f, 0.01f));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(countWithPageOnly), "no new GPU allocations exist");
            Assert.That(TextureRegistry.LiveSliceCount, Is.EqualTo(500));
            Assert.That(TextureRegistry.LiveBytes, Is.EqualTo(1024L * 1024 * 4), "still one page's worth");
        }
    }

    [Test]
    public void ProxyTexturesCountNoBytes()
    {
        // Dimension-only textures (the video pipeline) have no GPU allocation of their own.
        _ = new Texture(320, 240);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextureRegistry.LiveCount, Is.Zero, "it owns no allocation, so it is not a live texture");
            Assert.That(TextureRegistry.LiveSliceCount, Is.EqualTo(1), "but it is still tracked");
            Assert.That(TextureRegistry.LiveBytes, Is.Zero);
        }
    }

    [Test]
    public void DisposingUnregisters()
    {
        var tex = texture(64, 64);
        tex.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextureRegistry.LiveCount, Is.Zero);
            Assert.That(TextureRegistry.LiveBytes, Is.Zero);
            Assert.That(TextureRegistry.GetAll(), Does.Not.Contain(tex));
        }
    }

    [Test]
    public void DisposingTwiceOnlyUnregistersOnce()
    {
        var a = texture();
        var b = texture();

        a.Dispose();
        a.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(1));
            Assert.That(TextureRegistry.GetAll(), Does.Contain(b));
        }
    }

    [Test]
    public void PeakBytesRemembersTheHighWaterMark()
    {
        var big = texture(512, 512);
        long peakWhileAlive = TextureRegistry.LiveBytes;

        big.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextureRegistry.LiveBytes, Is.Zero);
            Assert.That(Statistic.GlobalStatistics.Get<long>("Textures", "Peak Bytes").Value, Is.EqualTo(peakWhileAlive));
        }
    }

    [Test]
    public void RegistrationDoesNotKeepATextureAlive()
    {
        // Entries are weak, so tooling can never be the reason a texture stays resident.
        allocateAndAbandon();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(TextureRegistry.GetAll(), Is.Empty);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void allocateAndAbandon() => _ = texture(256, 256);

    [Test]
    public void PruneDropsCollectedEntriesWithoutAffectingLiveOnes()
    {
        var kept = texture(32, 32);

        allocateAndAbandon();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        TextureRegistry.Prune();

        Assert.That(TextureRegistry.GetAll().Single(), Is.SameAs(kept));
    }

    /// <summary>
    /// A texture collected without ever being disposed has no chance to unregister itself, so its
    /// contribution has to be reconciled out of the counters when the dead entry is noticed. Without
    /// this the counters only ever climb, and "live count returns to its baseline" — the criterion the
    /// whole texture-lifetime investigation is measured against — can never hold.
    /// </summary>
    [Test]
    public void PruneReconcilesCountersForTexturesCollectedWithoutDisposal()
    {
        var kept = texture(32, 32);
        long keptBytes = TextureRegistry.LiveBytes;

        allocateAndAbandon();

        Assert.That(TextureRegistry.LiveCount, Is.EqualTo(2), "both are counted while alive");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        TextureRegistry.Prune();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(1));
            Assert.That(TextureRegistry.LiveBytes, Is.EqualTo(keptBytes));
            Assert.That(TextureRegistry.GetAll().Single(), Is.SameAs(kept));
        }
    }

    [Test]
    public void DisposingAfterAResetDoesNotDriveCountersNegative()
    {
        var tex = texture(64, 64);

        TextureRegistry.Reset();

        var fresh = texture(16, 16);
        tex.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(1), "only the texture registered after the reset");
            Assert.That(TextureRegistry.LiveBytes, Is.EqualTo(16 * 16 * 4));
            Assert.That(TextureRegistry.GetAll().Single(), Is.SameAs(fresh));
        }
    }
}
