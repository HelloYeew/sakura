// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Textures.ImageSharp;
using Sakura.Framework.Maths;
using SixLabors.ImageSharp;

namespace Sakura.Framework.Tests.Graphics.Textures;

/// <summary>
/// Test for <see cref="ImageSharpImageLoader"/>. The size and crop behavior it shares with every other
/// loader is in <see cref="ImageLoaderTest"/>; what is left here is ImageSharp's alone.
/// </summary>
[TestFixture]
public class ImageSharpImageLoaderTest : ImageLoaderTest
{
    private readonly ImageSharpImageLoader loader = new ImageSharpImageLoader();

    protected override IImageLoader CreateLoader() => loader;

    /// <remarks>
    /// Orientation 6 (RightTop) means the stored pixels sit a quarter turn from how they should be
    /// displayed, so decoding one upright swaps the axes. Pinned here rather than in
    /// <see cref="ImageLoaderTest"/> because it is the one decoding behaviour a different loader cannot
    /// be assumed to share — stb_image does not parse EXIF at all — so anything routing between loaders
    /// has to keep these on a loader that honours them.
    /// </remarks>
    [Test]
    public void ExifOrientationIsApplied()
    {
        using var stream = JpegWithOrientation(400, 200, 6);
        using var raw = loader.Load(stream);

        Assert.That(raw.Width, Is.EqualTo(200));
        Assert.That(raw.Height, Is.EqualTo(400));
    }

    [Test]
    public void UprightImageIsNotReoriented()
    {
        using var stream = JpegWithOrientation(400, 200, 1);
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
        using var stream = JpegWithOrientation(400, 200, 6);
        using var raw = loader.Load(stream, ImageLoadOptions.FillTarget(new Vector2(100, 100)));

        Assert.That(raw.Width, Is.EqualTo(100));
        Assert.That(raw.Height, Is.EqualTo(100));
    }

    [Test]
    public void NeedsOrientationOnlyForNonUprightImages()
    {
        using (var none = Jpeg(64, 64))
            Assert.That(ImageSharpPipeline.NeedsOrientation(Image.Identify(none).Metadata), Is.False);

        using (var upright = JpegWithOrientation(64, 64, 1))
            Assert.That(ImageSharpPipeline.NeedsOrientation(Image.Identify(upright).Metadata), Is.False);

        using (var rotated = JpegWithOrientation(64, 64, 6))
            Assert.That(ImageSharpPipeline.NeedsOrientation(Image.Identify(rotated).Metadata), Is.True);
    }

    /// <remarks>
    /// ImageSharp's alone: this pins <em>how</em> the stream is consumed, not what comes back. A loader
    /// that has to buffer the encoded bytes to size its decode would fail it without being wrong.
    /// </remarks>
    [Test]
    public void DecodesFromSeekableStreamWithoutBufferingIt()
    {
        using var seekable = Jpeg(1200, 800);
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
}
