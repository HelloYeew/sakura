// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.IO;

/// <summary>
/// A block of unmanaged memory holding the raw bytes of a file, handed to native libraries that
/// require a pointer which stays valid for as long as they hold it (BASS memory streams,
/// FreeType faces, HarfBuzz blobs).
/// </summary>
public sealed class NativeMemoryBuffer : IDisposable
{
    /// <summary>
    /// Size of the transfer buffer used to copy a stream into unmanaged memory. Rented from
    /// <see cref="ArrayPool{T}"/> for the duration of the copy, so reading a 10 MB file allocates
    /// nothing on the managed heap.
    /// </summary>
    private const int copy_buffer_size = 32 * 1024;

    /// <summary>
    /// Starting size for the growing path taken when the source stream cannot report its length.
    /// </summary>
    private const int initial_growth_capacity = 128 * 1024;

    private IntPtr pointer;
    private int referenceCount = 1;

    /// <summary>
    /// Accounts for this block in <see cref="NativeMemoryTracker"/> until it is freed.
    /// </summary>
    /// <remarks>
    /// Held here rather than by each consumer, because the consumers were previously each adding and
    /// subtracting the figure by hand and had to remember the original size to subtract — a
    /// <see cref="Length"/> that this class zeroes the moment it frees. Accounting where the allocation
    /// happens means it cannot be forgotten at a new call site, and there is exactly one place that knows
    /// the block is really gone.
    /// </remarks>
    private readonly NativeMemoryLease memoryLease;

    /// <summary>
    /// Pointer to the start of the block, or <see cref="IntPtr.Zero"/> once every reference has
    /// been released.
    /// </summary>
    public IntPtr Pointer => pointer;

    /// <summary>
    /// Number of valid bytes at <see cref="Pointer"/>.
    /// </summary>
    public long Length { get; private set; }

    /// <summary>
    /// Whether every reference has been released and the block freed.
    /// </summary>
    public bool IsFreed => Volatile.Read(ref referenceCount) <= 0;

    private NativeMemoryBuffer(IntPtr pointer, long length, NativeMemoryCategory category)
    {
        this.pointer = pointer;
        Length = length;
        memoryLease = NativeMemoryTracker.Add(category, length);
    }

    /// <summary>
    /// Reads a stream in full into a new unmanaged block. The stream is read from its current
    /// position to its end and is <em>not</em> disposed.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="category">
    /// What the block is being held for, for <see cref="NativeMemoryTracker"/> attribution. Defaults to
    /// <see cref="NativeMemoryCategory.Other"/> so an untagged caller is still counted in the total rather
    /// than being silently missing from it.
    /// </param>
    /// <returns>
    /// A buffer with one reference held by the caller, or <c>null</c> if the stream held no bytes.
    /// </returns>
    public static NativeMemoryBuffer? CreateFrom(Stream stream, NativeMemoryCategory category = NativeMemoryCategory.Other)
    {
        ArgumentNullException.ThrowIfNull(stream);

        long knownLength = tryGetRemainingLength(stream);

        return knownLength > 0
            ? createFromKnownLength(stream, knownLength, category)
            : createByGrowing(stream, category);
    }

    /// <summary>
    /// Reads a file in full into a new unmanaged block.
    /// </summary>
    /// <returns>
    /// A buffer with one reference held by the caller, or <c>null</c> if the file is empty.
    /// </returns>
    public static NativeMemoryBuffer? CreateFromFile(string path, NativeMemoryCategory category = NativeMemoryCategory.Other)
    {
        using (var stream = File.OpenRead(path))
            return CreateFrom(stream, category);
    }

    /// <summary>
    /// The number of bytes remaining in the stream, or 0 if it cannot be determined up front.
    /// </summary>
    private static long tryGetRemainingLength(Stream stream)
    {
        if (!stream.CanSeek)
            return 0;

        try
        {
            long remaining = stream.Length - stream.Position;
            return remaining > 0 ? remaining : 0;
        }
        catch (NotSupportedException)
        {
            // A seekable stream is still allowed to refuse to report a length.
            return 0;
        }
    }

    private static unsafe NativeMemoryBuffer? createFromKnownLength(Stream stream, long length, NativeMemoryCategory category)
    {
        byte* destination = (byte*)NativeMemory.Alloc((nuint)length);

        long written = 0;

        try
        {
            written = copyInto(stream, destination, length);
        }
        catch
        {
            NativeMemory.Free(destination);
            throw;
        }

        if (written == 0)
        {
            NativeMemory.Free(destination);
            return null;
        }

        return new NativeMemoryBuffer((IntPtr)destination, written, category);
    }

    /// <summary>
    /// Fallback for streams that cannot report a length (compressed or network sources): grow the
    /// block by doubling as the stream is consumed, then shrink it to the bytes actually read.
    /// <see cref="NativeMemory.Realloc"/> can extend in place, so this is still cheaper than a
    /// grow-by-doubling <see cref="MemoryStream"/> followed by a <c>ToArray</c>.
    /// </summary>
    private static unsafe NativeMemoryBuffer? createByGrowing(Stream stream, NativeMemoryCategory category)
    {
        byte[] transfer = ArrayPool<byte>.Shared.Rent(copy_buffer_size);

        long capacity = initial_growth_capacity;
        byte* destination = (byte*)NativeMemory.Alloc((nuint)capacity);
        long written = 0;

        try
        {
            int read;

            while ((read = stream.Read(transfer, 0, transfer.Length)) > 0)
            {
                if (written + read > capacity)
                {
                    while (written + read > capacity)
                        capacity *= 2;

                    destination = (byte*)NativeMemory.Realloc(destination, (nuint)capacity);
                }

                transfer.AsSpan(0, read).CopyTo(new Span<byte>(destination + written, read));
                written += read;
            }
        }
        catch
        {
            NativeMemory.Free(destination);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(transfer);
        }

        if (written == 0)
        {
            NativeMemory.Free(destination);
            return null;
        }

        if (written < capacity)
            destination = (byte*)NativeMemory.Realloc(destination, (nuint)written);

        return new NativeMemoryBuffer((IntPtr)destination, written, category);
    }

    /// <summary>
    /// Copies up to <paramref name="length"/> bytes from a stream into unmanaged memory through a
    /// single pooled transfer buffer.
    /// </summary>
    /// <returns>The number of bytes actually copied, which is short if the stream ended early.</returns>
    private static unsafe long copyInto(Stream stream, byte* destination, long length)
    {
        byte[] transfer = ArrayPool<byte>.Shared.Rent(copy_buffer_size);

        try
        {
            long written = 0;

            while (written < length)
            {
                int wanted = (int)Math.Min(transfer.Length, length - written);
                int read = stream.Read(transfer, 0, wanted);

                if (read <= 0)
                    break;

                transfer.AsSpan(0, read).CopyTo(new Span<byte>(destination + written, read));
                written += read;
            }

            return written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(transfer);
        }
    }

    /// <summary>
    /// Takes an additional reference, keeping the block alive until the matching
    /// <see cref="Release"/>.
    /// </summary>
    /// <returns>
    /// <c>false</c> if the block has already been freed, in which case no reference was taken and
    /// <see cref="Pointer"/> must not be used.
    /// </returns>
    public bool AddReference()
    {
        int current = Volatile.Read(ref referenceCount);

        while (current > 0)
        {
            int previous = Interlocked.CompareExchange(ref referenceCount, current + 1, current);

            if (previous == current)
                return true;

            current = previous;
        }

        return false;
    }

    /// <summary>
    /// Releases one reference, freeing the block if it was the last.
    /// </summary>
    /// <returns>Whether this call freed the block.</returns>
    public bool Release()
    {
        int remaining = Interlocked.Decrement(ref referenceCount);

        if (remaining > 0)
            return false;

        // Guard against an over-release racing the free, so a double release can never free twice.
        if (remaining < 0)
        {
            Interlocked.Exchange(ref referenceCount, 0);
            return false;
        }

        free();
        return true;
    }

    /// <summary>
    /// Releases the reference held by whoever created this buffer. Consumers that took their own
    /// reference keep the block alive past this point.
    /// </summary>
    public void Dispose()
    {
        // The finalizer is only suppressed once the block is actually gone. While a consumer still
        // holds a reference this object is still the only thing that can free the block, so it must
        // stay finalizable in case that consumer is dropped without releasing.
        if (Release())
            GC.SuppressFinalize(this);
    }

    private unsafe void free()
    {
        IntPtr toFree = Interlocked.Exchange(ref pointer, IntPtr.Zero);

        if (toFree == IntPtr.Zero)
            return;

        NativeMemory.Free((void*)toFree);
        Length = 0;

        memoryLease?.Dispose();
    }

    ~NativeMemoryBuffer()
    {
        // Only reachable once nothing holds a reference to this object, and therefore once nothing
        // can still be reading the pointer.
        free();
    }
}
