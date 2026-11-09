// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;

namespace Sakura.Framework.Statistic;

/// <summary>
/// A single-producer ring of per-frame records, which any thread can drain without making the producer wait.
/// </summary>
/// <typeparam name="T">What one frame records.</typeparam>
internal sealed class FrameRing<T>
    where T : struct
{
    private readonly T[] samples;
    private readonly int mask;

    private long writeCount;

    // Frames the producer has started writing: one ahead of writeCount while a write is in progress.
    private long startedCount;

    /// <summary>
    /// Makes a ring that keeps the last <paramref name="capacity"/> frames.
    /// </summary>
    /// <param name="capacity">How many frames it keeps. Must be a power of two.</param>
    public FrameRing(int capacity)
    {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The capacity must be a power of two.");

        samples = new T[capacity];
        mask = capacity - 1;
    }

    /// <summary>
    /// How many frames it keeps.
    /// </summary>
    public int Capacity => samples.Length;

    /// <summary>
    /// Total frames recorded since it was made. Also the cursor a consumer starts from to get only frames from now on.
    /// </summary>
    public long TotalFrames => Volatile.Read(ref writeCount);

    /// <summary>
    /// Records one frame. Must only be called by the one thread that produces them.
    /// </summary>
    public void Record(in T sample)
    {
        long index = writeCount;

        // Announced first, with a full fence so the slot can't be seen changing before it, for Drain to know which
        // slot the producer may be in the middle of.
        Interlocked.Exchange(ref startedCount, index + 1);

        samples[index & mask] = sample;

        // Published last, so a consumer never sees a count covering a slot that is still being written.
        Volatile.Write(ref writeCount, index + 1);
    }

    /// <summary>
    /// Copies every frame recorded since <paramref name="cursor"/> into <paramref name="destination"/>, oldest first,
    /// and advances the cursor past them.
    /// </summary>
    /// <param name="destination">Receives the frames. Frames beyond its length are dropped, the oldest first.</param>
    /// <param name="cursor">The caller's read position, advanced by this call. Start it at <see cref="TotalFrames"/>.</param>
    /// <param name="skipped">
    /// Frames that were recorded but could not be handed back, because the caller fell further behind than the ring
    /// retains or than <paramref name="destination"/> holds.
    /// </param>
    /// <returns>The number of frames written to <paramref name="destination"/>.</returns>
    /// <remarks>
    /// Safe to call from any thread and lock-free since the producer is never made to wait on a consumer. Whatever is
    /// returned is complete and in order: frames the producer overwrote while the copy was in progress are dropped and
    /// counted in <paramref name="skipped"/> rather than handed back half-written, so a caller can trust the sequence it
    /// gets.
    /// </remarks>
    public int Drain(Span<T> destination, ref long cursor, out long skipped)
    {
        long write = Volatile.Read(ref writeCount);

        // A cursor ahead of the producer can only come from a caller that never seeded it; treat it
        // as a request for everything from here on.
        if (cursor < 0 || cursor > write)
            cursor = write;

        long oldest = Math.Max(cursor, write - samples.Length);
        skipped = oldest - cursor;

        long available = write - oldest;

        if (available > destination.Length)
        {
            skipped += available - destination.Length;
            oldest = write - destination.Length;
            available = destination.Length;
        }

        int count = (int)available;

        for (int i = 0; i < count; i++)
        {
            destination[i] = samples[(oldest + i) & mask];
        }

        cursor = write;

        // The producer may have wrapped past slots we were still reading, in which case those entries
        // are a mix of the frames we asked for and much newer ones - handing them back would put the
        // sequence out of order. A slot is only known intact if the producer could not have reached it
        // at any point during the copy: every frame from (started - capacity) on, where started counts
        // the frame being written too, if there is one. The fence keeps the copy's reads from moving
        // past the count's.
        Interlocked.MemoryBarrier();
        long safeOldest = Volatile.Read(ref startedCount) - samples.Length;

        if (safeOldest > oldest)
        {
            int discarded = (int)Math.Min(safeOldest - oldest, count);

            skipped += discarded;
            count -= discarded;

            if (count > 0)
                destination.Slice(discarded, count).CopyTo(destination);
        }

        return count;
    }
}
