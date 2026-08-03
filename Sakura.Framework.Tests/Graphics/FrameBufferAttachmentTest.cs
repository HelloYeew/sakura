// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests that a framebuffer's color attachment leaves the texture registry when it is replaced or freed.
/// </summary>
[TestFixture]
public class FrameBufferAttachmentTest
{
    private HeadlessRenderer renderer = null!;

    [SetUp]
    public void SetUp()
    {
        renderer = new HeadlessRenderer(new HeadlessTextureManager());
        TextureRegistry.Reset();
    }

    [TearDown]
    public void TearDown() => TextureRegistry.Reset();

    /// <summary>
    /// The same figure the texture visualizer overlay summary line reports as <c>peak</c>.
    /// </summary>
    private static long peakBytes => GlobalStatistics.Get<long>("Textures", "Peak Bytes").Value;

    [Test]
    public void AnAttachmentIsRegisteredWhileItExists()
    {
        using var frameBuffer = renderer.CreateFrameBuffer(64, 64);

        Assert.Multiple(() =>
        {
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(1));
            Assert.That(TextureRegistry.LiveBytes, Is.EqualTo(64 * 64 * 4));
        });
    }

    /// <summary>
    /// Resizing must replace the attachment, not accumulate wrappers over destroyed backends.
    /// </summary>
    [Test]
    public void ResizingDoesNotAccumulateRegistryEntries()
    {
        using var frameBuffer = renderer.CreateFrameBuffer(1280, 720);

        // A window drag walks through intermediate sizes, one attachment each.
        for (int i = 0; i < 200; i++)
            frameBuffer.Resize(1280 - i, 720 - i);

        Assert.Multiple(() =>
        {
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(1), "only the current attachment is live");
            Assert.That(TextureRegistry.LiveBytes, Is.EqualTo(frameBuffer.Width * frameBuffer.Height * 4));
        });
    }

    /// <summary>
    /// The reading that made this visible: <c>Live Bytes</c> equalling <c>Peak Bytes</c> exactly is the
    /// tell that the counter only ever climbed.
    /// </summary>
    [Test]
    public void ResizingLeavesLiveBytesBelowPeak()
    {
        using var frameBuffer = renderer.CreateFrameBuffer(2578, 1914);

        frameBuffer.Resize(640, 480);

        Assert.Multiple(() =>
        {
            Assert.That(TextureRegistry.LiveBytes, Is.EqualTo(640 * 480 * 4));
            Assert.That(peakBytes, Is.GreaterThan(TextureRegistry.LiveBytes), "live equalling peak is the tell that nothing ever decremented");
        });
    }

    [Test]
    public void DisposingAFrameBufferUnregistersItsAttachment()
    {
        var frameBuffer = renderer.CreateFrameBuffer(256, 256);
        var attachment = frameBuffer.Texture;

        frameBuffer.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(attachment.IsDisposed, Is.True);
            Assert.That(TextureRegistry.LiveCount, Is.Zero);
            Assert.That(TextureRegistry.LiveBytes, Is.Zero);
            Assert.That(TextureRegistry.GetAll(), Does.Not.Contain(attachment));
        });
    }

    /// <summary>
    /// Registration and the GPU resource have to move together — a registry that lists a texture whose
    /// backend is gone is what renders as a white card in the viewer.
    /// </summary>
    [Test]
    public void NothingListedByTheRegistryHasADestroyedAttachment()
    {
        using var frameBuffer = renderer.CreateFrameBuffer(512, 512);

        frameBuffer.Resize(256, 256);
        frameBuffer.Resize(128, 128);

        Assert.That(TextureRegistry.GetAll(), Has.All.Matches<Texture>(t => t.IsAvailable));
    }

    [Test]
    public void ResizingToTheSameSizeKeepsTheExistingAttachment()
    {
        using var frameBuffer = renderer.CreateFrameBuffer(300, 200);
        var attachment = frameBuffer.Texture;

        frameBuffer.Resize(300, 200);

        Assert.Multiple(() =>
        {
            Assert.That(frameBuffer.Texture, Is.SameAs(attachment), "a no-op resize must not reallocate");
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(1));
        });
    }
}
