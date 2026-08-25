// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Textures.Stb;

namespace Sakura.Framework.Tests.Graphics.Textures;

/// <summary>
/// Test for <see cref="StbImageLoader"/>
/// </summary>
[TestFixture]
public class StbImageLoaderTest : ImageLoaderTest
{
    private readonly StbImageLoader loader = new StbImageLoader();

    protected override IImageLoader CreateLoader() => loader;

    /// <remarks>
    /// The native ships per-RID and there are RIDs it is not built for. Ignoring rather than failing is
    /// the same judgement the loader itself makes: a missing native is a platform fact, not a defect.
    /// </remarks>
    [SetUp]
    public void RequireNative()
    {
        if (!StbImageLoader.IsAvailable)
            Assert.Ignore("libsakura-image is not available on this platform.");
    }

    /// <summary>
    /// The one place this loader deliberately disagrees with ImageSharp.
    /// </summary>
    /// <remarks>
    /// stb_image does not parse EXIF, so a photo stored a quarter turn from how it should be displayed
    /// comes back at its stored dimensions rather than its display ones. Asserted rather than left
    /// undocumented because it is invisible at run time — the decode succeeds and returns the wrong
    /// shape, so nothing routing to this loader can detect it by catching. The router has to read the
    /// header and send oriented images to ImageSharp instead.
    /// </remarks>
    [Test]
    public void ExifOrientationIsIgnored()
    {
        using var stream = JpegWithOrientation(400, 200, 6);
        using var raw = loader.Load(stream);

        using (Assert.EnterMultipleScope())
        {
            // ImageSharp answers 200x400 for this same input; see ImageSharpImageLoaderTest.
            Assert.That(raw.Width, Is.EqualTo(400));
            Assert.That(raw.Height, Is.EqualTo(200));
        }
    }

    /// <remarks>
    /// WebP is ImageSharp's alone — the shim compiles in JPEG, PNG, BMP and GIF only. A rejection has to
    /// be an exception rather than a wrong result, because that is what a router's fallback catches.
    /// </remarks>
    [Test]
    public void UnsupportedDataThrows()
    {
        using var stream = new System.IO.MemoryStream([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        Assert.That(() => loader.Load(stream), Throws.InstanceOf<System.IO.InvalidDataException>());
    }
}
