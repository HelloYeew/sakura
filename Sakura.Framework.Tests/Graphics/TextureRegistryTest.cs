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
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(3), "every texture is tracked");
            Assert.That(TextureRegistry.LiveBytes, Is.EqualTo(1024L * 1024 * 4), "but slices share the page's memory");
        }
    }

    [Test]
    public void ProxyTexturesCountNoBytes()
    {
        // Dimension-only textures (the video pipeline) have no GPU allocation of their own.
        _ = new Texture(320, 240);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(1));
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
}
