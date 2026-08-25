// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Sakura.Framework.Tests.Graphics.Textures;

/// <summary>
/// Tests for <see cref="ExifOrientation"/>, the header parse a router uses to keep silently-divergent
/// images away from a decoder that ignores EXIF.
/// </summary>
[TestFixture]
public class ExifOrientationTest
{
    private static byte[] jpeg(int width, int height, ushort? orientation)
    {
        using var stream = new MemoryStream();
        using (var image = new Image<Rgba32>(width, height))
        {
            if (orientation is { } value)
            {
                image.Metadata.ExifProfile = new ExifProfile();
                image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, value);
            }

            image.SaveAsJpeg(stream);
        }

        return stream.ToArray();
    }

    private static byte[] png(ushort orientation)
    {
        using var stream = new MemoryStream();
        using (var image = new Image<Rgba32>(32, 32))
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
            image.SaveAsPng(stream);
        }

        return stream.ToArray();
    }

    [Test]
    public void ReadsJpegOrientation()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ExifOrientation.Read(jpeg(64, 64, 6)), Is.EqualTo(6));
            Assert.That(ExifOrientation.Read(jpeg(64, 64, 1)), Is.EqualTo(1));
            Assert.That(ExifOrientation.Read(jpeg(64, 64, 8)), Is.EqualTo(8));
        }
    }

    [Test]
    public void ReadsPngOrientation()
    {
        // PNG carries the same TIFF block in an eXIf chunk rather than a JPEG APP1 segment.
        Assert.That(ExifOrientation.Read(png(6)), Is.EqualTo(6));
    }

    [Test]
    public void AbsentOrientationReadsAsZero()
    {
        Assert.That(ExifOrientation.Read(jpeg(64, 64, null)), Is.EqualTo(0));
    }

    [Test]
    public void RequiresTransformOnlyForNonUprightImages()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ExifOrientation.RequiresTransform(jpeg(64, 64, null)), Is.False);
            Assert.That(ExifOrientation.RequiresTransform(jpeg(64, 64, 1)), Is.False);
            Assert.That(ExifOrientation.RequiresTransform(jpeg(64, 64, 6)), Is.True);
            Assert.That(ExifOrientation.RequiresTransform(jpeg(64, 64, 8)), Is.True);
        }
    }

    /// <remarks>
    /// This parser runs on every image before either decoder sees it, including files that are not
    /// images at all, so it has to answer rather than throw for anything handed to it.
    /// </remarks>
    [Test]
    public void MalformedInputIsAnsweredNotThrown()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ExifOrientation.Read([]), Is.EqualTo(0));
            Assert.That(ExifOrientation.Read([0xFF]), Is.EqualTo(0));
            Assert.That(ExifOrientation.Read([0x00, 0x01, 0x02, 0x03, 0x04]), Is.EqualTo(0));
            // a JPEG that stops partway through its header
            Assert.That(ExifOrientation.Read([0xFF, 0xD8, 0xFF, 0xE1, 0x00]), Is.EqualTo(0));
            // a PNG signature and nothing after it
            Assert.That(ExifOrientation.Read([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]), Is.EqualTo(0));
        }
    }

    [Test]
    public void TruncationAtEveryLengthIsSurvivable()
    {
        // fuzzing the one input a router cannot control: every prefix of a real oriented JPEG
        byte[] full = jpeg(64, 64, 6);

        for (int length = 0; length < Math.Min(full.Length, 4096); length++)
        {
            int prefix = length;
            Assert.That(() => ExifOrientation.Read(full.AsSpan(0, prefix)), Throws.Nothing, $"threw at length {prefix}");
        }
    }
}
