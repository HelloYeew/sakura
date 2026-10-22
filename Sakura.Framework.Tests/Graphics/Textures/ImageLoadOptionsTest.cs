// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sakura.Framework.Tests.Graphics.Textures;

/// <summary>
/// Keeping decoded images within the largest texture a GPU takes.
/// </summary>
[TestFixture]
public class ImageLoadOptionsTest
{
    [Test]
    public void FullSizeIsCappedAtTheLargestTexture()
    {
        var options = ImageLoadOptions.FullSize.FitWithin(4096);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.TargetSize, Is.EqualTo(new Vector2(4096)));
            Assert.That(options.FillMode, Is.EqualTo(TextureFillMode.Fit));
        }
    }

    /// <summary>
    /// A box over the limit shrinks whole, so a Fill still crops the same shape out of the image.
    /// </summary>
    [Test]
    public void ABoxOverTheLimitShrinksWhole()
    {
        var options = ImageLoadOptions.FillTarget(new Vector2(8192, 2048)).FitWithin(4096);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.TargetSize, Is.EqualTo(new Vector2(4096, 1024)));
            Assert.That(options.FillMode, Is.EqualTo(TextureFillMode.Fill));
        }
    }

    [Test]
    public void ABoxWithinTheLimitIsLeftAlone()
    {
        var options = ImageLoadOptions.FillTarget(new Vector2(1920, 1080)).FitWithin(4096);

        Assert.That(options.TargetSize, Is.EqualTo(new Vector2(1920, 1080)));
    }

    [TestCase(int.MaxValue)]
    [TestCase(0)]
    public void NoLimitLeavesFullSize(int maxDimension)
    {
        Assert.That(ImageLoadOptions.FullSize.FitWithin(maxDimension).HasTarget, Is.False);
    }

    /// <summary>
    /// The cap reduces an image larger than the GPU takes, and leaves a smaller one at its own size.
    /// </summary>
    [TestCase(5000, 2500, 4096, 2048)]
    [TestCase(800, 600, 800, 600)]
    public void DecodedWithinTheLimit(int width, int height, int expectedWidth, int expectedHeight)
    {
        using var stream = new MemoryStream();

        using (var image = new Image<Rgba32>(width, height))
            image.SaveAsPng(stream);

        stream.Position = 0;

        using var raw = new RoutingImageLoader().Load(stream, ImageLoadOptions.FullSize.FitWithin(4096));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.EqualTo(expectedWidth));
            Assert.That(raw.Height, Is.EqualTo(expectedHeight));
        }
    }
}
