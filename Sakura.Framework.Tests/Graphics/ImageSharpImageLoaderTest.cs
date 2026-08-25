// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
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

    private static MemoryStream jpegWithOrientation(int width, int height, ushort orientation)
    {
        var stream = new MemoryStream();

        using (var image = new Image<Rgba32>(width, height))
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
            image.SaveAsJpeg(stream);
        }

        stream.Position = 0;
        return stream;
    }

    /// <remarks>
    /// Orientation 6 (RightTop) means the stored pixels sit a quarter turn from how they should be
    /// displayed, so decoding one upright swaps the axes. Pinned because it is the one decoding
    /// behaviour a different loader cannot be assumed to share — stb_image does not parse EXIF at all —
    /// so anything routing between loaders has to keep these on a loader that honours them.
    /// </remarks>
    [Test]
    public void ExifOrientationIsApplied()
    {
        using var stream = jpegWithOrientation(400, 200, 6);
        using var raw = loader.Load(stream);

        Assert.That(raw.Width, Is.EqualTo(200));
        Assert.That(raw.Height, Is.EqualTo(400));
    }

    [Test]
    public void UprightImageIsNotReoriented()
    {
        using var stream = jpegWithOrientation(400, 200, 1);
        using var raw = loader.Load(stream);

        Assert.That(raw.Width, Is.EqualTo(400));
        Assert.That(raw.Height, Is.EqualTo(200));
    }

    /// <remarks>
    /// The ordering that matters: the axes have to swap before the Fill decides which band of the
    /// source to keep. Planning the crop against the stored dimensions instead would keep the wrong
    /// band and hand back the wrong shape.
    /// </remarks>
    [Test]
    public void ExifOrientationIsAppliedBeforeReduction()
    {
        using var stream = jpegWithOrientation(400, 200, 6);
        using var raw = loader.Load(stream, ImageLoadOptions.FillTarget(new Vector2(100, 100)));

        Assert.That(raw.Width, Is.EqualTo(100));
        Assert.That(raw.Height, Is.EqualTo(100));
    }

    [Test]
    public void NeedsOrientationOnlyForNonUprightImages()
    {
        using (var none = jpeg(64, 64))
            Assert.That(ImageSharpImageLoader.NeedsOrientation(Image.Identify(none).Metadata), Is.False);

        using (var upright = jpegWithOrientation(64, 64, 1))
            Assert.That(ImageSharpImageLoader.NeedsOrientation(Image.Identify(upright).Metadata), Is.False);

        using (var rotated = jpegWithOrientation(64, 64, 6))
            Assert.That(ImageSharpImageLoader.NeedsOrientation(Image.Identify(rotated).Metadata), Is.True);
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

    [Test]
    public void FillCropsCentreBandToTargetAspect()
    {
        using var stream = jpeg(3840, 2160); // a 4K background bound for a small bar

        var raw = loader.Load(stream, ImageLoadOptions.FillTarget(new Vector2(768, 128)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.LessThanOrEqualTo(768));
            Assert.That(raw.Height, Is.LessThanOrEqualTo(128));
            Assert.That(raw.Data.Length, Is.EqualTo(raw.Width * raw.Height * 4));
            // Cropped to the target aspect, so a wide strip is kept rather than the whole 16:9 frame.
            Assert.That((float)raw.Width / raw.Height, Is.EqualTo(768f / 128f).Within(0.2f));
        }
    }

    [Test]
    public void FillKeepsFarFewerPixelsThanLongestEdgeCap()
    {
        // for a Fill, capping the longest edge alone still keeps
        // pixels that are clipped off-screen.
        using var cropped = jpeg(1920, 1080);
        using var capped = jpeg(1920, 1080);

        var withCrop = loader.Load(cropped, ImageLoadOptions.FillTarget(new Vector2(768, 128)));
        var withoutCrop = loader.Load(capped, 768);

        Assert.That(withCrop.Width * withCrop.Height, Is.LessThan(withoutCrop.Width * withoutCrop.Height / 2));
    }

    [Test]
    public void FitTargetKeepsAspectWithinBox()
    {
        using var stream = jpeg(2000, 1000); // 2:1

        var raw = loader.Load(stream, new ImageLoadOptions(new Vector2(256, 256), TextureFillMode.Fit));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.LessThanOrEqualTo(256));
            Assert.That(raw.Height, Is.LessThanOrEqualTo(256));
            // no crop, so the aspect is preserved: 2:1 fits as 256x128.
            Assert.That((float)raw.Width / raw.Height, Is.EqualTo(2f).Within(0.1f));
        }
    }

    [Test]
    public void FillDoesNotUpscaleSmallSource()
    {
        using var stream = jpeg(120, 120);

        var raw = loader.Load(stream, ImageLoadOptions.FillTarget(new Vector2(256, 256)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.EqualTo(120));
            Assert.That(raw.Height, Is.EqualTo(120));
        }
    }

    [Test]
    public void FullSizeOptionsDecodeAtFullResolution()
    {
        using var stream = jpeg(1600, 900);

        var raw = loader.Load(stream, ImageLoadOptions.FullSize);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.EqualTo(1600));
            Assert.That(raw.Height, Is.EqualTo(900));
        }
    }

    [Test]
    public void DecodesFromNonSeekableStream()
    {
        // Exercises the grow-and-hand-over path in EncodedBuffer (embedded/compressed sources).
        using var seekable = jpeg(1200, 800);
        using var stream = new NonSeekableStream(seekable);

        var raw = loader.Load(stream, ImageLoadOptions.FillTarget(new Vector2(300, 300)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.EqualTo(300));
            Assert.That(raw.Height, Is.EqualTo(300));
            Assert.That(raw.Data.Length, Is.EqualTo(raw.Width * raw.Height * 4));
        }
    }

    [Test]
    public void DecodesFromSeekableStreamWithoutBufferingIt()
    {
        using var seekable = jpeg(1200, 800);
        using var stream = new RewindCountingStream(seekable);

        var raw = loader.Load(stream, ImageLoadOptions.FillTarget(new Vector2(300, 300)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.EqualTo(300));
            Assert.That(raw.Height, Is.EqualTo(300));
            // the header is identified in place and the stream rewound for the decode, rather than the
            // whole encoded file being copied into a buffer that can be read twice.
            Assert.That(stream.Rewinds, Is.GreaterThan(0));
        }
    }

    [Test]
    public void DecodesFromSeekableStreamAtNonZeroPosition()
    {
        // an image that does not begin at byte 0, so the rewind after the header read has to return to
        // where the caller left the stream rather than to its start.
        using var seekable = new MemoryStream();
        seekable.Write(new byte[64]);

        using (var source = jpeg(1200, 800))
            source.CopyTo(seekable);

        seekable.Position = 64;

        var raw = loader.Load(seekable, ImageLoadOptions.FillTarget(new Vector2(300, 300)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.EqualTo(300));
            Assert.That(raw.Height, Is.EqualTo(300));
            Assert.That(raw.Data.Length, Is.EqualTo(raw.Width * raw.Height * 4));
        }
    }

    /// <summary>
    /// A seekable pass-through that counts how often it is asked to move backwards.
    /// </summary>
    private class RewindCountingStream : Stream
    {
        private readonly Stream inner;

        public RewindCountingStream(Stream inner)
        {
            this.inner = inner;
        }

        public int Rewinds { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set
            {
                if (value < inner.Position)
                    Rewinds++;

                inner.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() => inner.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
        {
            long from = inner.Position;
            long to = inner.Seek(offset, origin);

            if (to < from)
                Rewinds++;

            return to;
        }
    }

    private class NonSeekableStream : Stream
    {
        private readonly Stream inner;

        public NonSeekableStream(Stream inner)
        {
            this.inner = inner;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
