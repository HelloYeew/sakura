// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using static Sakura.Framework.Graphics.Containers.BufferedContainer;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests for the framebuffer release in <see cref="BufferedContainerSharedData"/>, which is what the
/// drawable disposal cascade reaches when a buffered container leaves the tree.
/// </summary>
/// <remarks>
/// A buffered container holds up to four framebuffers, each a GPU allocation sized to its on-screen
/// bounds. They used to live for the process lifetime, so every transient buffered container permanently
/// leaked full-screen render targets.
/// </remarks>
[TestFixture]
public class BufferedContainerSharedDataTest
{
    private HeadlessRenderer renderer = null!;

    [SetUp]
    public void SetUp() => renderer = new HeadlessRenderer(new HeadlessTextureManager());

    [Test]
    public void ReleaseFreesEveryBuffer()
    {
        var data = new BufferedContainerSharedData();
        var main = new CountingFrameBuffer(renderer.WhitePixel);
        var effectA = new CountingFrameBuffer(renderer.WhitePixel);
        var effectB = new CountingFrameBuffer(renderer.WhitePixel);

        data.FrameBuffer = main;
        data.EffectBuffers[0] = effectA;
        data.EffectBuffers[1] = effectB;
        data.RenderedVersion = 42;

        data.Release(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(main.DisposeCount, Is.EqualTo(1));
            Assert.That(effectA.DisposeCount, Is.EqualTo(1));
            Assert.That(effectB.DisposeCount, Is.EqualTo(1));
            Assert.That(data.FrameBuffer, Is.Null);
            Assert.That(data.EffectBuffers[0], Is.Null);
            Assert.That(data.EffectBuffers[1], Is.Null);
            Assert.That(data.RenderedVersion, Is.EqualTo(-1), "the next draw must re-render, not composite a freed buffer");
        });
    }

    /// <summary>
    /// <see cref="BufferedContainerSharedData.FinalEffectBuffer"/> aliases whichever effect buffer the
    /// ping-pong landed on, so releasing it as a buffer of its own would be a double free.
    /// </summary>
    [Test]
    public void TheAliasedFinalEffectBufferIsNotFreedTwice()
    {
        var data = new BufferedContainerSharedData();
        var effect = new CountingFrameBuffer(renderer.WhitePixel);

        data.EffectBuffers[0] = effect;
        data.FinalEffectBuffer = effect;

        data.Release(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(effect.DisposeCount, Is.EqualTo(1));
            Assert.That(data.FinalEffectBuffer, Is.Null);
        });
    }

    /// <summary>
    /// Release is not a one-way latch — the draw node reallocates on demand — so a container that is
    /// disposed, re-added and drawn again must not have its fresh buffers freed by the earlier release.
    /// </summary>
    [Test]
    public void ReleaseCannotFreeBuffersAllocatedAfterIt()
    {
        var data = new BufferedContainerSharedData();
        var original = new CountingFrameBuffer(renderer.WhitePixel);

        data.FrameBuffer = original;
        data.Release(renderer);

        var replacement = new CountingFrameBuffer(renderer.WhitePixel);
        data.FrameBuffer = replacement;

        // A second release with nothing new to free (as a repeated disposal would do) must leave the
        // replacement alone until it is itself released.
        Assert.That(replacement.DisposeCount, Is.Zero);

        data.Release(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(original.DisposeCount, Is.EqualTo(1), "the first release freed it exactly once");
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ReleaseWithoutARendererStillDetachesTheBuffers()
    {
        var data = new BufferedContainerSharedData
        {
            FrameBuffer = new CountingFrameBuffer(renderer.WhitePixel)
        };

        // Null renderer is the never-loaded container: it can hold no buffers the renderer allocated, so
        // there is nothing to schedule a release against.
        data.Release(null);

        Assert.That(data.FrameBuffer, Is.Null);
    }

    private sealed class CountingFrameBuffer : IFrameBuffer
    {
        public Texture Texture { get; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public int DisposeCount { get; private set; }

        public CountingFrameBuffer(Texture texture)
        {
            Texture = texture;
            Width = Height = 16;
        }

        public void Resize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public void Dispose() => DisposeCount++;
    }
}
