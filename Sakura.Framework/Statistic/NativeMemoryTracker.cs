// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;

namespace Sakura.Framework.Statistic;

/// <summary>
/// Process-wide accounting for memory held outside the managed heap, broken down by
/// <see cref="NativeMemoryCategory"/> and surfaced through <see cref="GlobalStatistics"/>.
/// </summary>
public static class NativeMemoryTracker
{
    private static readonly int category_count = Enum.GetValues<NativeMemoryCategory>().Length;

    private static readonly long[] bytes_per_category = new long[category_count];
    private static readonly GlobalStatistic<long>[] stats_per_category = new GlobalStatistic<long>[category_count];

    private static readonly GlobalStatistic<long> stat_total = GlobalStatistics.Get<long>(statistic_group, "Total");
    private static readonly GlobalStatistic<long> stat_peak = GlobalStatistics.Get<long>(statistic_group, "Peak Total");

    private const string statistic_group = "Native Memory";

    private static long totalBytes;
    private static long peakTotalBytes;

    static NativeMemoryTracker()
    {
        foreach (var category in Enum.GetValues<NativeMemoryCategory>())
            stats_per_category[(int)category] = GlobalStatistics.Get<long>(statistic_group, category.ToString());
    }

    /// <summary>
    /// Total unmanaged bytes currently accounted for across every category.
    /// </summary>
    public static long TotalBytes => Interlocked.Read(ref totalBytes);

    /// <summary>
    /// The highest <see cref="TotalBytes"/> seen so far. Retained because a footprint that returns to
    /// baseline after a spike and one that never spiked are the same reading otherwise.
    /// </summary>
    public static long PeakTotalBytes => Interlocked.Read(ref peakTotalBytes);

    /// <summary>
    /// Unmanaged bytes currently accounted for in one category.
    /// </summary>
    public static long BytesFor(NativeMemoryCategory category) => Interlocked.Read(ref bytes_per_category[(int)category]);

    /// <summary>
    /// Records an unmanaged allocation and returns the lease that accounts for it.
    /// </summary>
    /// <param name="category">What the memory is being held for.</param>
    /// <param name="size">
    /// Size of the allocation in bytes. Zero or negative is accepted and produces a lease that accounts
    /// for nothing, so a caller does not have to special-case a failed or empty allocation.
    /// </param>
    /// <returns>
    /// A lease that must be disposed when the memory is released. Disposing it more than once is safe and
    /// counts once.
    /// </returns>
    /// <remarks>
    /// A lease rather than a matching <c>Remove(category, size)</c> call, because the size to subtract has
    /// to survive until release and the allocation itself often cannot hold it: a
    /// <c>NativeMemoryBuffer</c> zeroes its own <c>Length</c> when it frees, so by the time anything
    /// notices the release the original figure is gone. Handing back an object that already knows both
    /// numbers removes the chance of subtracting a different amount than was added.
    /// </remarks>
    public static NativeMemoryLease Add(NativeMemoryCategory category, long size)
    {
        var lease = new NativeMemoryLease(category, Math.Max(0, size));

        if (lease.Bytes > 0)
            adjust(category, lease.Bytes);

        return lease;
    }

    /// <summary>
    /// Forgets every recorded allocation and resets the counters, including the peak.
    /// </summary>
    /// <remarks>
    /// For test isolation only — the tracker is process-wide, so a fixture asserting on byte totals needs a
    /// clean slate. Leases taken before a reset become inert rather than dangerous: each one still knows it
    /// has not been disposed, so it will subtract on disposal, and the counters floor at zero rather than
    /// going negative.
    /// </remarks>
    public static void Reset()
    {
        foreach (var category in Enum.GetValues<NativeMemoryCategory>())
        {
            Interlocked.Exchange(ref bytes_per_category[(int)category], 0);
            stats_per_category[(int)category].Value = 0;
        }

        Interlocked.Exchange(ref totalBytes, 0);
        Interlocked.Exchange(ref peakTotalBytes, 0);

        stat_total.Value = 0;
        stat_peak.Value = 0;
    }

    /// <summary>
    /// Applies a signed delta to one category and the total, then republishes both statistics.
    /// </summary>
    private static void adjust(NativeMemoryCategory category, long delta)
    {
        int index = (int)category;

        long categoryTotal = addFloored(ref bytes_per_category[index], delta);
        long total = addFloored(ref totalBytes, delta);

        stats_per_category[index].Value = categoryTotal;
        stat_total.Value = total;

        if (delta <= 0)
            return;

        // Compare-and-swap rather than a plain compare-then-set: two threads allocating at once must not
        // let the larger total lose to the smaller one.
        long peak = Interlocked.Read(ref peakTotalBytes);

        while (total > peak)
        {
            long previous = Interlocked.CompareExchange(ref peakTotalBytes, total, peak);

            if (previous == peak)
            {
                stat_peak.Value = total;
                break;
            }

            peak = previous;
        }
    }

    /// <summary>
    /// Applies a delta to a counter and returns the new value, never leaving it below zero.
    /// </summary>
    /// <remarks>
    /// The clamp has to write the field back, not just clamp what it returns: a counter that is negative in
    /// storage reports negative on the next read, and a negative byte total is never a true reading. It also
    /// keeps one mismatched release from permanently skewing every category that shares the total. Leases
    /// taken before <see cref="Reset"/> and disposed afterwards are the case that reaches this in practice.
    /// </remarks>
    private static long addFloored(ref long counter, long delta)
    {
        long updated = Interlocked.Add(ref counter, delta);

        if (updated >= 0)
            return updated;

        // Add back exactly the overshoot that was observed. A concurrent add landing in between leaves the
        // counter at that thread's contribution rather than at zero, which is the right answer anyway.
        Interlocked.Add(ref counter, -updated);
        return 0;
    }

    internal static void Release(NativeMemoryCategory category, long size)
    {
        if (size > 0)
            adjust(category, -size);
    }
}

/// <summary>
/// Accounts for one unmanaged allocation for as long as it is held. Obtained from
/// <see cref="NativeMemoryTracker.Add"/> and disposed when the memory is freed.
/// </summary>
public sealed class NativeMemoryLease : IDisposable
{
    /// <summary>
    /// What the accounted allocation is held for.
    /// </summary>
    public NativeMemoryCategory Category { get; }

    /// <summary>
    /// Bytes this lease accounts for, or zero once it has been disposed.
    /// </summary>
    public long Bytes => Interlocked.Read(ref bytes);

    private long bytes;

    internal NativeMemoryLease(NativeMemoryCategory category, long bytes)
    {
        Category = category;
        this.bytes = bytes;
    }

    /// <summary>
    /// Removes this allocation from the tracker. Safe to call more than once; only the first call counts.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        release();
    }

    ~NativeMemoryLease() => release();

    /// <summary>
    /// Subtracts this lease's contribution exactly once, whichever path gets here first.
    /// </summary>
    private void release()
    {
        // Claimed atomically so an explicit release racing finalization can only subtract once, mirroring
        // how the native handle wrappers claim their handles.
        long claimed = Interlocked.Exchange(ref bytes, 0);

        NativeMemoryTracker.Release(Category, claimed);
    }
}
