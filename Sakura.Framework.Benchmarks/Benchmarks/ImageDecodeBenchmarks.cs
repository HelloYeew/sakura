// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// Test based on usage in yuuki. (heavy load texture in song selection screen)
/// </summary>
[MemoryDiagnoser]
public class ImageDecodeBenchmarks
{
    /// <summary>
    /// A cover-sized source, encoded once. Real covers are JPEG or PNG of roughly this size.
    /// </summary>
    private byte[] encodedPng = null!;

    private byte[] encodedJpeg = null!;

    private ImageSharpImageLoader loader = null!;

    /// <summary>
    /// What a carousel asks for: a cover reduced to fit a card.
    /// </summary>
    private static readonly ImageLoadOptions cover_target = ImageLoadOptions.FillTarget(new Vector2(320, 180));

    [GlobalSetup]
    public void Setup()
    {
        loader = new ImageSharpImageLoader();

        using var source = new Image<Rgba32>(1920, 1080);

        // A flat image compresses to almost nothing, which would make the encoded-buffer rental
        // unrepresentatively small. Vary every pixel so the encoded size is realistic.
        source.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);

                for (int x = 0; x < row.Length; x++)
                    row[x] = new Rgba32((byte)(x * 7 % 256), (byte)(y * 13 % 256), (byte)((x ^ y) % 256), 255);
            }
        });

        using (var buffer = new MemoryStream())
        {
            source.SaveAsPng(buffer);
            encodedPng = buffer.ToArray();
        }

        using (var buffer = new MemoryStream())
        {
            source.SaveAsJpeg(buffer);
            encodedJpeg = buffer.ToArray();
        }
    }

    /// <summary>
    /// Reports the encoded sizes once, so the allocation figures below can be read against them.
    /// </summary>
    [Benchmark]
    public int EncodedSizes() => encodedPng.Length + encodedJpeg.Length;

    /// <summary>
    /// Full-size decode, which skips the encoded buffer entirely — so its allocation is the
    /// <c>ImageRawData.Rent</c> half on its own.
    /// </summary>
    [Benchmark]
    public int Decode_FullSize_NoTarget()
    {
        using var stream = new MemoryStream(encodedPng, writable: false);
        using var raw = loader.Load(stream);

        return raw.Width;
    }

    /// <summary>
    /// The path a cover actually takes: a target size, so the header is identified first and the encoded
    /// bytes are buffered to allow it. Allocation here is both rentals plus ImageSharp's own working set.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int Decode_WithTarget_Png()
    {
        using var stream = new MemoryStream(encodedPng, writable: false);
        using var raw = loader.Load(stream, cover_target);

        return raw.Width;
    }

    [Benchmark]
    public int Decode_WithTarget_Jpeg()
    {
        using var stream = new MemoryStream(encodedJpeg, writable: false);
        using var raw = loader.Load(stream, cover_target);

        return raw.Width;
    }

    /// <summary>
    /// The same targeted decode from a stream that refuses to seek, which is the one shape that genuinely
    /// has to buffer the encoded bytes — a compressed or archive-backed source. Read against
    /// <see cref="Decode_WithTarget_Png"/>: the gap is what seek ability is worth.
    /// </summary>
    [Benchmark]
    public int Decode_WithTarget_NonSeekable()
    {
        using var stream = new UnseekableStream(new MemoryStream(encodedPng, writable: false));
        using var raw = loader.Load(stream, cover_target);

        return raw.Width;
    }

    /// <summary>
    /// A stream that refuses to seek or report a length.
    /// </summary>
    private sealed class UnseekableStream : Stream
    {
        private readonly Stream inner;

        public UnseekableStream(Stream inner)
        {
            this.inner = inner;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new System.NotSupportedException();

        public override long Position
        {
            get => throw new System.NotSupportedException();
            set => throw new System.NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new System.NotSupportedException();
        public override void SetLength(long value) => throw new System.NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new System.NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();

            base.Dispose(disposing);
        }
    }
}
