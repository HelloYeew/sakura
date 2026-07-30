// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Buffers;
using System.Threading;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// The raw image data that's ready for renderer to consume in RGBA8 row-major order.
/// </summary>
/// <remarks>
/// <para>
/// The pixel memory is owned, and usually rented from <see cref="ArrayPool{T}"/>: a 4K image is 33 MB,
/// which goes straight to the large object heap if freshly allocated, and a game changing backgrounds
/// does that repeatedly. Renting means the same few blocks are reused instead.
/// </para>
/// <para>
/// That makes <see cref="Dispose"/> mandatory rather than decorative — the pixels are invalid
/// afterwards, and a rental that is never returned is simply lost to the pool. Ownership can also be
/// handed to a texture manager, which disposes it once the GPU upload has run; see
/// <see cref="ITextureManager.CreateFromStream"/>.
/// </para>
/// <para>
/// Still a struct so <c>using var raw = loader.Load(...)</c> allocates nothing, but the rental itself is
/// tracked by a small owner object shared by every copy of the struct. Returning a pooled array twice
/// corrupts the pool for the whole process, and a struct is trivially copied — so the release has to be
/// idempotent across copies, which a bare field could not guarantee.
/// </para>
/// </remarks>
public readonly struct ImageRawData : IDisposable
{
    private readonly PixelOwner? owner;

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// The RGBA8 pixels, row-major, <c>Width * Height * 4</c> bytes long. Only valid until
    /// <see cref="Dispose"/>, empty afterward, and for a default instance.
    /// </summary>
    public ReadOnlySpan<byte> Data => owner == null ? default : owner.Span;

    /// <summary>
    /// Whether this holds pixel memory. False for a default instance and after disposal.
    /// </summary>
    public bool IsValid => owner?.IsValid ?? false;

    /// <summary>
    /// Wraps pixel memory the caller allocated. Disposal will not free it — use
    /// <see cref="Rent"/> for memory this should own.
    /// </summary>
    public ImageRawData(int width, int height, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        Width = width;
        Height = height;
        owner = new PixelOwner(data, data.Length, pooled: false);
    }

    private ImageRawData(int width, int height, PixelOwner owner)
    {
        Width = width;
        Height = height;
        this.owner = owner;
    }

    /// <summary>
    /// Allocates owned pixel memory for a <paramref name="width"/> × <paramref name="height"/> RGBA8
    /// image, rented from the shared array pool. The contents are undefined; fill
    /// <see cref="GetWritableSpan"/> before use.
    /// </summary>
    public static ImageRawData Rent(int width, int height)
    {
        int length = checked(width * height * 4);
        byte[] rented = ArrayPool<byte>.Shared.Rent(length);

        return new ImageRawData(width, height, new PixelOwner(rented, length, pooled: true));
    }

    /// <summary>
    /// Copies pixel data the caller owns into a new owned (pooled) buffer, sized for a
    /// <paramref name="width"/> × <paramref name="height"/> RGBA8 image.
    /// </summary>
    /// <remarks>
    /// Used where pixels have to outlive the call that supplied them — a queued GPU upload reads them a
    /// frame or more later, and a <see cref="ReadOnlySpan{T}"/> cannot be captured to be read then. Any
    /// shortfall is zero-filled rather than left as whatever the pool handed back, so a caller passing
    /// short data uploads transparent pixels instead of another image's leftovers.
    /// </remarks>
    public static ImageRawData CopyFrom(int width, int height, ReadOnlySpan<byte> source)
    {
        var raw = Rent(width, height);
        var destination = raw.GetWritableSpan();

        if (source.Length >= destination.Length)
        {
            source[..destination.Length].CopyTo(destination);
        }
        else
        {
            source.CopyTo(destination);
            destination[source.Length..].Clear();
        }

        return raw;
    }

    /// <summary>
    /// The pixel memory as a writable span, for filling it immediately after <see cref="Rent"/>.
    /// </summary>
    /// <remarks>
    /// A pooled array is handed out dirty, so a decoder must write every byte it intends to be read.
    /// </remarks>
    public Span<byte> GetWritableSpan() => owner == null ? default : owner.WritableSpan;

    /// <summary>
    /// Releases the pixel memory, returning it to the pool if it was rented. Idempotent, and safe to
    /// call on any copy of the same instance.
    /// </summary>
    public void Dispose() => owner?.Release();

    /// <summary>
    /// Holds the rental so that releasing it stays a single, idempotent action no matter how many times
    /// the enclosing struct is copied.
    /// </summary>
    private sealed class PixelOwner
    {
        private readonly int length;
        private readonly bool pooled;

        private byte[]? array;

        public PixelOwner(byte[] array, int length, bool pooled)
        {
            this.array = array;
            this.length = length;
            this.pooled = pooled;
        }

        public bool IsValid => Volatile.Read(ref array) != null;

        public ReadOnlySpan<byte> Span
        {
            get
            {
                byte[]? current = Volatile.Read(ref array);
                return current == null ? default : current.AsSpan(0, length);
            }
        }

        public Span<byte> WritableSpan
        {
            get
            {
                byte[]? current = Volatile.Read(ref array);
                return current == null ? default : current.AsSpan(0, length);
            }
        }

        public void Release()
        {
            byte[]? claimed = Interlocked.Exchange(ref array, null);

            if (claimed != null && pooled)
                ArrayPool<byte>.Shared.Return(claimed);
        }
    }
}
