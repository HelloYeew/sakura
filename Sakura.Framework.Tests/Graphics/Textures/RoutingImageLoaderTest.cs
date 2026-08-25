// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Textures.Stb;
using Sakura.Framework.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sakura.Framework.Tests.Graphics.Textures;

/// <summary>
/// Test for <see cref="RoutingImageLoader"/>
/// </summary>
[TestFixture]
public class RoutingImageLoaderTest : ImageLoaderTest
{
    private readonly RoutingImageLoader loader = new RoutingImageLoader();

    protected override IImageLoader CreateLoader() => loader;

    private static MemoryStream webp(int width, int height)
    {
        var stream = new MemoryStream();
        using (var image = new Image<Rgba32>(width, height))
            image.SaveAsWebp(stream);
        stream.Position = 0;
        return stream;
    }

    /// <remarks>
    /// The loud half of the fallback. stb_image contains no WebP decoder at all — not disabled, absent —
    /// so this can only succeed by way of ImageSharp. It is the test that would fail if the fallback
    /// were ever quietly removed.
    /// </remarks>
    [Test]
    public void WebpIsServedByTheFallback()
    {
        using var stream = webp(320, 240);
        using var raw = loader.Load(stream);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.EqualTo(320));
            Assert.That(raw.Height, Is.EqualTo(240));
            Assert.That(raw.Data.Length, Is.EqualTo(320 * 240 * 4));
        }
    }

    [Test]
    public void WebpIsReducedByTheFallbackToo()
    {
        // the fallback has to honour ImageLoadOptions, not just return something
        using var stream = webp(1200, 800);
        using var raw = loader.Load(stream, ImageLoadOptions.FillTarget(new Vector2(300, 300)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.EqualTo(300));
            Assert.That(raw.Height, Is.EqualTo(300));
        }
    }

    /// <remarks>
    /// The quiet half. stb would decode this successfully at 400x200 and report no error at all, so
    /// the router has to have refused it before to decode rather than caught anything afterward.
    /// A result of 200x400 is proof the pre-check fired.
    /// </remarks>
    [Test]
    public void OrientedImageIsRoutedAwayFromStb()
    {
        using var stream = JpegWithOrientation(400, 200, 6);
        using var raw = loader.Load(stream);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.EqualTo(200));
            Assert.That(raw.Height, Is.EqualTo(400));
        }
    }

    /// <remarks>
    /// Asserted against <see cref="StbImageLoader.IsAvailable"/> rather than against <c>true</c>, because
    /// the decision has two independent inputs and only one of them is the image. On a platform with no
    /// native every answer is false, and a test that hard-coded true would pass only on the machine that
    /// happened to have the library which is most of the point of having the fallback.
    /// </remarks>
    [Test]
    public void RoutingDecisionMatchesTheImageContent()
    {
        using var upright = Jpeg(64, 64);
        using var rotated = JpegWithOrientation(64, 64, 6);
        using var notAnImage = new MemoryStream([0x00, 0x01, 0x02, 0x03]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loader.WouldUseStb(upright.ToArray()), Is.EqualTo(StbImageLoader.IsAvailable));

            // False whatever the platform: an orientation stb cannot honour is decided by the content.
            Assert.That(loader.WouldUseStb(rotated.ToArray()), Is.False);

            // Not routed away up front — garbage fails loudly, so the caught fallback handles it.
            Assert.That(loader.WouldUseStb(notAnImage.ToArray()), Is.EqualTo(StbImageLoader.IsAvailable));
        }
    }

    [Test]
    public void DataNeitherLoaderCanReadStillThrows()
    {
        using var stream = new MemoryStream([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        // Falling back is not the same as swallowing: when both decoders refuse, the caller has to hear
        // about it rather than receive an empty texture.
        Assert.That(() => loader.Load(stream), Throws.Exception);
    }
}
