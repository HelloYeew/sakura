// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Graphics.Textures;

/// <summary>
/// Tests for <see cref="TextureOwnership"/> and the guarantee it exists to provide: releasing a texture
/// destroys its GPU resource and unregisters it <i>together</i>, so the two can never disagree.
/// </summary>
/// <remarks>
/// The regression these guard against: every release path used to dispose the
/// <see cref="INativeTexture"/> directly. That freed the GPU memory but left the <see cref="Texture"/>
/// registered and undisposed, so the texture viewer kept listing it — as a flat white rectangle, because
/// both backends bind a 1×1 white fallback for a texture that cannot be sampled — and
/// <see cref="TextureRegistry.LiveBytes"/> never came back down.
/// </remarks>
[TestFixture]
public class TextureOwnershipTest
{
    [SetUp]
    public void SetUp() => TextureRegistry.Reset();

    [TearDown]
    public void TearDown() => TextureRegistry.Reset();

    [Test]
    public void AnOwnedTextureDestroysItsBackendOnDispose()
    {
        var backend = new HeadlessNativeTexture(64, 64);
        var texture = new Texture(backend);

        Assert.That(texture.Ownership, Is.EqualTo(TextureOwnership.Owned));

        texture.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.Handle, Is.EqualTo(IntPtr.Zero), "the GPU resource should be gone");
            Assert.That(texture.IsDisposed, Is.True);
            Assert.That(TextureRegistry.LiveCount, Is.Zero);
            Assert.That(TextureRegistry.GetAll(), Does.Not.Contain(texture));
        }
    }

    /// <summary>
    /// The defect in one assertion: a released texture must never remain listed while its GPU resource
    /// is gone. That combination is what renders as a white card in the viewer.
    /// </summary>
    [Test]
    public void ReleasingATextureCannotLeaveALiveEntryOverADestroyedResource()
    {
        var backend = new HeadlessNativeTexture(2434, 1494);
        var texture = new Texture(backend) { Name = "beatmap-background-50" };

        texture.Dispose();

        foreach (var listed in TextureRegistry.GetAll())
        {
            Assert.That(listed.BackendTexture?.Handle, Is.Not.EqualTo(IntPtr.Zero),
                $"'{listed.Name}' is still listed but its GPU texture has been destroyed");
        }

        Assert.That(TextureRegistry.GetAll(), Is.Empty);
    }

    [Test]
    public void ABorrowedSliceDoesNotDestroyThePageItViews()
    {
        var page = new HeadlessNativeTexture(1024, 1024);
        var pageTexture = new Texture(page);
        var slice = new Texture(page, new RectangleF(0, 0, 0.25f, 0.25f));

        Assert.That(slice.Ownership, Is.EqualTo(TextureOwnership.Borrowed));

        slice.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Handle, Is.Not.EqualTo(IntPtr.Zero), "the atlas page belongs to the atlas");
            Assert.That(slice.IsDisposed, Is.True, "but the slice itself is released");
            Assert.That(TextureRegistry.GetAll(), Does.Not.Contain(slice));
            Assert.That(TextureRegistry.GetAll(), Does.Contain(pageTexture));
        }
    }

    /// <summary>
    /// <c>Get()</c> hands the missing-texture fallback and the white pixel to every caller that asks for
    /// something unavailable, so any one of them disposing what it was given must be a no-op.
    /// </summary>
    [Test]
    public void ASharedTextureIgnoresDisposal()
    {
        var backend = new HeadlessNativeTexture(1, 1);
        var shared = new Texture(backend, TextureOwnership.Shared);

        shared.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.Handle, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(shared.IsDisposed, Is.False);
            Assert.That(TextureRegistry.GetAll(), Does.Contain(shared));
            Assert.That(TextureRegistry.LiveCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void IsAvailableIsFalseOnceTheResourceIsGone()
    {
        var backend = new HeadlessNativeTexture(64, 64);
        var texture = new Texture(backend);

        Assert.That(texture.IsAvailable, Is.True);

        texture.Dispose();

        Assert.That(texture.IsAvailable, Is.False, "a destroyed texture must not claim to be renderable");
    }

    [Test]
    public void AProxyTextureUnregistersWithNothingToDestroy()
    {
        var proxy = new Texture(320, 240);

        Assert.DoesNotThrow(() => proxy.Dispose());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proxy.IsDisposed, Is.True);
            Assert.That(TextureRegistry.GetAll(), Is.Empty);
        }
    }
}
