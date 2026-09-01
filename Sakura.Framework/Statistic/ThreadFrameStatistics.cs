// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Sakura.Framework.Statistic;

/// <summary>
/// One frame's worth of timing for a single thread, recorded by the thread that ran the frame.
/// </summary>
public struct ThreadFrameSample
{
    /// <summary>
    /// Time spent running the frame's work, with any GC pause that landed inside it subtracted.
    /// This excludes the throttle's sleep and busy-spin, so it is the only figure here that
    /// reflects how much of the frame budget the thread actually consumed.
    /// </summary>
    public double BusyMilliseconds;

    /// <summary>
    /// GC pause time that elapsed while the frame's work was running. Broken out separately
    /// because a stall the collector caused is not a cost the frame itself can be asked to fix.
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public double GCMilliseconds;

    /// <summary>
    /// Time within the frame spent blocked on an external device rather than doing work, already
    /// subtracted from <see cref="BusyMilliseconds"/>. For the draw thread this is the buffer swap,
    /// where the display applies its own back-pressure; every other thread reports 0.
    /// </summary>
    /// <remarks>
    /// Kept out of the busy figure because it is not headroom the frame can win back by doing less:
    /// under VSync a healthy frame blocks here for most of its budget, and counting that as a load
    /// would report an idle app as saturated.
    /// </remarks>
    public double BlockedMilliseconds;

    /// <summary>
    /// Wall-clock period between the start of this frame and the start of the previous one,
    /// sleep and spin included. While the throttle is holding, this sits at
    /// <see cref="BudgetMilliseconds"/> regardless of how much work the frame did.
    /// </summary>
    public double ElapsedMilliseconds;

    /// <summary>
    /// The frame budget in effect for this frame, or 0 when the thread was unthrottled.
    /// </summary>
    public double BudgetMilliseconds;

    /// <summary>
    /// Whether the frame's work overran its budget. Always false for an unthrottled thread.
    /// </summary>
    public readonly bool MissedDeadline => BudgetMilliseconds > 0 && BusyMilliseconds > BudgetMilliseconds;
}

/// <summary>
/// A single-producer ring of per-frame timings for one thread.
/// </summary>
/// <remarks>
/// Samples are recorded by the thread that ran the frame rather than sampled from another thread,
/// which is what makes them complete. (A consumer polling a 1000 Hz thread at update rate observes
/// under half its frames and is blind to any spike between two polls.)
/// </remarks>
public sealed class ThreadFrameStatistics
{
    /// <summary>
    /// Number of frames retained. Must stay a power of two.
    /// </summary>
    /// <remarks>
    /// Half a second of history for the fastest thread we run (1000 Hz), against consumers that
    /// poll at least at display rate. A consumer would have to stall for that long to lose data.
    /// </remarks>
    public const int CAPACITY = 512;

    private readonly ThreadFrameSample[] samples = new ThreadFrameSample[CAPACITY];

    private long writeCount;

    /// <summary>
    /// Total frames recorded since startup. Also, the cursor value a consumer should start from
    /// if it wants only frames from this point on, rather than replaying the retained history.
    /// </summary>
    public long TotalFrames => Volatile.Read(ref writeCount);

    /// <summary>
    /// Records one frame. Must only be called by the thread that owns this instance.
    /// </summary>
    public void Record(in ThreadFrameSample sample)
    {
        long index = writeCount;
        samples[index & (CAPACITY - 1)] = sample;

        // Published last, so a consumer never sees a count covering a slot that is still being written.
        Volatile.Write(ref writeCount, index + 1);
    }

    /// <summary>
    /// Copies every frame recorded since <paramref name="cursor"/> into <paramref name="destination"/>,
    /// oldest first, and advances the cursor past them.
    /// </summary>
    /// <param name="destination">Receives the frames. Frames beyond its length are dropped, the oldest first.</param>
    /// <param name="cursor">
    /// The caller's read position, advanced by this call. Start it at <see cref="TotalFrames"/>.
    /// </param>
    /// <param name="skipped">
    /// Frames that were recorded but could not be handed back, because the caller fell further behind
    /// than the ring retains or than <paramref name="destination"/> holds.
    /// </param>
    /// <returns>The number of frames written to <paramref name="destination"/>.</returns>
    /// <remarks>
    /// Safe to call from any thread and lock-free since the producer is never made to wait on a consumer.
    /// Whatever is returned is complete and in order - frames the producer overwrote while the copy
    /// was in progress are dropped and counted in <paramref name="skipped"/> rather than handed back
    /// half-written, so a caller can trust the sequence it gets.
    /// </remarks>
    public int Drain(Span<ThreadFrameSample> destination, ref long cursor, out long skipped)
    {
        long write = Volatile.Read(ref writeCount);

        // A cursor ahead of the producer can only come from a caller that never seeded it; treat it
        // as a request for everything from here on.
        if (cursor < 0 || cursor > write)
            cursor = write;

        long oldest = Math.Max(cursor, write - CAPACITY);
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
            destination[i] = samples[(oldest + i) & (CAPACITY - 1)];

        cursor = write;

        // The producer may have wrapped past slots we were still reading, in which case those entries
        // are a mix of the frames we asked for and much newer ones - handing them back would put the
        // sequence out of order. A slot is only known intact if the producer could not have reached it
        // at any point during the copy, which is everything from (latest - CAPACITY) on.
        long safeOldest = Volatile.Read(ref writeCount) - CAPACITY;

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
