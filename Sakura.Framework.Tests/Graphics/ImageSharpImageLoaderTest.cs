// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Test for <see cref="ImageSharpImageLoader"/>
/// </summary>
[TestFixture]
public class ImageSharpImageLoaderTest
{
    private readonly ImageSharpImageLoader loader = new ImageSharpImageLoader();

    private static MemoryStream jpeg(int width, int height)
    {
        var stream = new MemoryStream();
        using (var image = new Image<Rgba32>(width, height))
            image.SaveAsJpeg(stream);
        stream.Position = 0;
        return stream;
    }

    [Test]
    public void CapsLongestEdgePreservingAspect()
    {
        using var stream = jpeg(4000, 2000); // 2:1

        var raw = loader.Load(stream, 512);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Math.Max(raw.Width, raw.Height), Is.LessThanOrEqualTo(512));
            Assert.That((float)raw.Width / raw.Height, Is.EqualTo(2f).Within(0.05f));
            Assert.That(raw.Data.Length, Is.EqualTo(raw.Width * raw.Height * 4));
        }
    }

    [Test]
    public void DoesNotUpscaleSmallSource()
    {
        using var stream = jpeg(100, 80);

        var raw = loader.Load(stream, 512);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.EqualTo(100));
            Assert.That(raw.Height, Is.EqualTo(80));
        }
    }

    [Test]
    public void NoLimitDecodesFullResolution()
    {
        using var stream = jpeg(1024, 768);

        var raw = loader.Load(stream);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.EqualTo(1024));
            Assert.That(raw.Height, Is.EqualTo(768));
        }
    }
}
