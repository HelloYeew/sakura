// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Textures.ImageSharp;
using Sakura.Framework.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests for the backend-independent implementation behind <see cref="ITextureManager.CreateFromStream"/>
/// the one-call path from an encoded image to a GPU texture, including reference-counted sharing.
/// </summary>
[TestFixture]
public class CreateFromStreamTest
{
    private HeadlessRenderer renderer = null!;
    private ImageSharpImageLoader imageLoader = null!;
    private SharedTextureStore sharedTextures = null!;
    private int releases;

    [SetUp]
    public void SetUp()
    {
        renderer = new HeadlessRenderer(new HeadlessTextureManager());
        imageLoader = new ImageSharpImageLoader();
        sharedTextures = new SharedTextureStore();
        releases = 0;

        // After the renderer, so its own white-pixel texture is not counted as something these tests
        // created. The registry is process-wide.
        TextureRegistry.Reset();
    }

    [TearDown]
    public void TearDown() => TextureRegistry.Reset();

    private void release(Texture texture)
    {
        releases++;
        texture.Dispose();
    }

    private Texture? create(Stream stream, TextureCreationOptions options)
        => TextureUploads.FromStream(stream, options, renderer, imageLoader, sharedTextures, release);

    private static MemoryStream jpeg(int width, int height)
    {
        var stream = new MemoryStream();
        using (var image = new Image<Rgba32>(width, height))
            image.SaveAsJpeg(stream);
        stream.Position = 0;
        return stream;
    }

    [Test]
    public void DecodesAtTheRequestedSizeAndRegistersTheTexture()
    {
        using var stream = jpeg(1600, 900);

        var texture = create(stream, new TextureCreationOptions
        {
            Decode = ImageLoadOptions.FillTarget(new Vector2(320, 180)),
            Name = "cover-1"
        });

        Assert.That(texture, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(texture!.Width, Is.LessThanOrEqualTo(320), "the decode target should have capped the size");
            Assert.That(texture.Name, Is.EqualTo("cover-1"), "the viewer needs a label");
            Assert.That(TextureRegistry.GetAll(), Does.Contain(texture));
            Assert.That(texture.Ownership, Is.EqualTo(TextureOwnership.Owned));
        }
    }

    [Test]
    public void UnsharedTexturesAreIndependent()
    {
        using var first = jpeg(64, 64);
        using var second = jpeg(64, 64);

        var a = create(first, TextureCreationOptions.Default);
        var b = create(second, TextureCreationOptions.Default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(a, Is.Not.SameAs(b));
            Assert.That(sharedTextures.Count, Is.Zero, "no share key means no store entry");
        }
    }

    /// <summary>
    /// The point of SF-11: the same image at the same size, requested twice, is one GPU texture.
    /// </summary>
    [Test]
    public void TheSameShareKeyReturnsTheSameTexture()
    {
        var options = new TextureCreationOptions
        {
            Decode = ImageLoadOptions.FillTarget(new Vector2(128, 128)),
            ShareKey = TextureCreationOptions.ShareKeyFor("cover-1", new Vector2(128, 128), TextureFillMode.Fill)
        };

        using var first = jpeg(512, 512);
        using var second = jpeg(512, 512);

        var a = create(first, options);
        var b = create(second, options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(b, Is.SameAs(a));
            Assert.That(sharedTextures.Count, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// A share hit must not even read the stream — that is where the saved decode and upload come from.
    /// </summary>
    [Test]
    public void AShareHitDoesNotReadTheStream()
    {
        var options = new TextureCreationOptions { ShareKey = "shared" };

        using var first = jpeg(64, 64);
        create(first, options);

        using var second = jpeg(64, 64);
        create(second, options);

        Assert.That(second.Position, Is.Zero, "the second stream should be untouched");
    }

    [Test]
    public void DifferentShareKeysDoNotCollide()
    {
        using var first = jpeg(64, 64);
        using var second = jpeg(64, 64);

        var a = create(first, new TextureCreationOptions { ShareKey = "a" });
        var b = create(second, new TextureCreationOptions { ShareKey = "b" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(a, Is.Not.SameAs(b));
            Assert.That(sharedTextures.Count, Is.EqualTo(2));
        }
    }

    /// <summary>
    /// Acquire, acquire, release, release: the texture survives the first release and is disposed by the
    /// second. Getting this wrong blanks an image that is still on screen elsewhere.
    /// </summary>
    [Test]
    public void ReleasingSharedTexturesFollowsReferenceCount()
    {
        var options = new TextureCreationOptions { ShareKey = "shared" };

        using var first = jpeg(64, 64);
        using var second = jpeg(64, 64);

        var texture = create(first, options)!;
        create(second, options);

        sharedTextures.Release("shared", release);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(texture.IsDisposed, Is.False, "still held by the second acquirer");
            Assert.That(sharedTextures.Count, Is.EqualTo(1));
        }

        sharedTextures.Release("shared", release);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(texture.IsDisposed, Is.True, "the last release disposes it");
            Assert.That(sharedTextures.Count, Is.Zero);
            Assert.That(TextureRegistry.GetAll(), Does.Not.Contain(texture));
        }
    }

    [Test]
    public void AReleasedShareKeyIsDecodedAgainOnTheNextRequest()
    {
        var options = new TextureCreationOptions { ShareKey = "shared" };

        using var first = jpeg(64, 64);
        var original = create(first, options)!;

        sharedTextures.Release("shared", release);
        Assert.That(original.IsDisposed, Is.True);

        using var second = jpeg(64, 64);
        var replacement = create(second, options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(replacement, Is.Not.Null);
            Assert.That(replacement, Is.Not.SameAs(original));
            Assert.That(replacement!.IsDisposed, Is.False);
        }
    }

    [Test]
    public void AnUndecodableStreamReturnsNullWithoutRegisteringAnything()
    {
        using var garbage = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);

        var texture = create(garbage, new TextureCreationOptions { Name = "broken" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(texture, Is.Null);
            Assert.That(TextureRegistry.GetAll(), Is.Empty);
            Assert.That(sharedTextures.Count, Is.Zero);
        }
    }

    [Test]
    public void AFailedDecodeUnderAShareKeyLeavesNoEntry()
    {
        using var garbage = new MemoryStream([9, 9, 9, 9]);

        var texture = create(garbage, new TextureCreationOptions { ShareKey = "shared" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(texture, Is.Null);
            Assert.That(sharedTextures.Count, Is.Zero, "a failure must not poison the key");
        }
    }
}
