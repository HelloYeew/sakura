// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace Sakura.Framework.Graphics.Textures.ImageSharp;

/// <summary>
/// The basic image loader using ImageSharp.
/// </summary>
public class ImageSharpImageLoader : IImageLoader
{
    /// <summary>
    /// Configures ImageSharp process-wide.
    /// </summary>
    static ImageSharpImageLoader()
    {
        Configuration.Default.MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount);
    }

    public void LogInfo()
    {
        Logger.Verbose("🖼️ ImageSharp image loader initialized");
        Logger.Verbose($"ImageSharp Version: {imageSharpVersion()}");
        Logger.Verbose($"ImageSharp Formats: {string.Join(", ", Configuration.Default.ImageFormats.Select(f => f.Name))}");
        Logger.Verbose($"ImageSharp Max Parallelism: {Configuration.Default.MaxDegreeOfParallelism}");
        // Only JPEG can decode at a reduced scale, which is why the hint is gated on it and why this is
        // worth stating next to the format list rather than left implied.
        Logger.Verbose("ImageSharp Scaled Decode: JPEG only");
    }

    /// <summary>
    /// The package version, e.g. 3.1.12.
    /// </summary>
    /// <remarks>
    /// From the informational version, not <c>AssemblyName.Version</c> — Six Labors pins the assembly
    /// version at <c>3.0.0.0</c> across the whole 3.x line, so reporting that would name a version
    /// nobody can look up. Any build metadata after a <c>+</c> is dropped.
    /// </remarks>
    private static string imageSharpVersion()
    {
        string? informational = typeof(Image).Assembly
                                            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                            ?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
            return typeof(Image).Assembly.GetName().Version?.ToString() ?? "unknown";

        int metadata = informational.IndexOf('+');

        return metadata < 0 ? informational : informational[..metadata];
    }

    public ImageRawData Load(Stream stream) => Load(stream, ImageLoadOptions.FullSize);

    public ImageRawData Load(Stream stream, int maxDimension) => Load(stream, ImageLoadOptions.MaxDimension(maxDimension));

    public ImageRawData Load(Stream stream, ImageLoadOptions options)
    {
        if (!options.HasTarget)
        {
            using var full = Image.Load<Rgba32>(stream);
            return ImageSharpPipeline.Finish(full);
        }

        var target = options.TargetSize!.Value;
        bool crop = options.CropToFill;

        // A seekable source can be read twice, so read the header, rewind, and decode straight from it.
        if (stream.CanSeek)
        {
            long origin = stream.Position;
            var decodeSize = decodeSizeFor(stream, target, crop);
            stream.Position = origin;

            using var image = Image.Load<Rgba32>(new DecoderOptions { TargetSize = decodeSize }, stream);
            return ImageSharpPipeline.Finish(image, target, crop);
        }

        // Non-seekable (an embedded resource or an archive entry): the header read cannot be undone, so
        // the bytes have to be buffered before the size hint can be computed.
        var encoded = EncodedBuffer.Read(stream);
        var buffered = new DecoderOptions { TargetSize = decodeSizeFor(encoded.Span, target, crop) };

        using var encodedStream = encoded.AsStream();

        {
            using var image = Image.Load<Rgba32>(buffered, encodedStream);
            return ImageSharpPipeline.Finish(image, target, crop);
        }
    }

    /// <summary>
    /// <see cref="ImageSharpPipeline.DecodeSizeFor"/> for an already-buffered image.
    /// </summary>
    private static Size? decodeSizeFor(ReadOnlySpan<byte> encoded, Vector2 target, bool cropToFill)
    {
        try
        {
            return ImageSharpPipeline.DecodeSizeFor(Image.Identify(encoded), target, cropToFill);
        }
        catch
        {
            return null; // header unreadable, fall back to a full decode, the reduction still caps it
        }
    }

    /// <summary>
    /// <see cref="ImageSharpPipeline.DecodeSizeFor"/> for a stream, read in place. Leaves the stream
    /// positioned wherever the header read ended, so the caller must rewind before decoding.
    /// </summary>
    private static Size? decodeSizeFor(Stream stream, Vector2 target, bool cropToFill)
    {
        try
        {
            return ImageSharpPipeline.DecodeSizeFor(Image.Identify(stream), target, cropToFill);
        }
        catch
        {
            return null; // header unreadable, fall back to a full decode, the reduction still caps it
        }
    }

    /// <summary>
    /// An encoded image's bytes held in memory, readable both as a span (for
    /// <see cref="Image.Identify(ReadOnlySpan{byte})"/>) and as a non-copying stream (for the decode
    /// itself). Only for sources that cannot be read twice, a seekable stream is identified and decoded
    /// in place instead.
    /// </summary>
    private readonly struct EncodedBuffer
    {
        private readonly byte[] array;
        private readonly int length;

        private EncodedBuffer(byte[] array, int length)
        {
            this.array = array;
            this.length = length;
        }

        public ReadOnlySpan<byte> Span => array.AsSpan(0, length);

        /// <summary>
        /// A read-only stream over the buffered bytes. Wraps the existing array rather than copying it.
        /// </summary>
        public Stream AsStream() => new MemoryStream(array, 0, length, writable: false);

        public static EncodedBuffer Read(Stream stream)
        {
            // the length is not known ahead of time, so grow, then hand over the stream's own buffer
            // rather than the ToArray() copy of it.
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return new EncodedBuffer(ms.GetBuffer(), (int)ms.Length);
        }
    }
}
