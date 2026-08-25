// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Buffers;
using System.IO;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// An encoded image's bytes held in one contiguous, pooled buffer, readable both as a span and as a
/// non-copying stream.
/// </summary>
internal readonly struct EncodedImage : IDisposable
{
    private readonly byte[]? array;
    private readonly int length;

    private EncodedImage(byte[] array, int length)
    {
        this.array = array;
        this.length = length;
    }

    public ReadOnlySpan<byte> Span => array is null ? default : array.AsSpan(0, length);

    /// <summary>
    /// A read-only stream over the bytes. Wraps the existing array rather than copying it, so it stays
    /// valid only until <see cref="Dispose"/>.
    /// </summary>
    public Stream AsStream() => new MemoryStream(array ?? [], 0, length, writable: false);

    public static EncodedImage Read(Stream stream)
    {
        // A seekable stream reports its remaining length, so the buffer is rented once at the right
        // size. Everything else has to grow first, and only then is copied into a rental.
        if (stream.CanSeek)
        {
            int remaining = checked((int)(stream.Length - stream.Position));
            byte[] exact = ArrayPool<byte>.Shared.Rent(remaining);
            stream.ReadExactly(exact.AsSpan(0, remaining));
            return new EncodedImage(exact, remaining);
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        int grown = (int)buffer.Length;
        byte[] rented = ArrayPool<byte>.Shared.Rent(grown);
        buffer.GetBuffer().AsSpan(0, grown).CopyTo(rented);
        return new EncodedImage(rented, grown);
    }

    public void Dispose()
    {
        if (array != null)
            ArrayPool<byte>.Shared.Return(array);
    }
}
