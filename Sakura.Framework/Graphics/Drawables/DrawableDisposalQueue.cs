// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// An update-thread work queue for drawables that have been removed from the tree and are to be
/// disposed, with a per-frame item budget. Drained once per update frame by <see cref="Process"/>.
/// </summary>
/// <remarks>
/// <para>
/// Tearing down a screen removes one drawable and disposes its entire subtree. Doing that inline
/// walks thousands of drawables inside a single frame, which is exactly the kind of spike the frame
/// graph shows as a stutter. Disposal of a removed drawable is not urgent — it is already detached,
/// so it is neither updated nor drawn — so the work is spread over subsequent frames instead.
/// </para>
/// <para>
/// A container's cascade enqueues its children rather than recursing, so each queued item is a
/// bounded amount of work and the budget below actually bounds the frame. Ordering is therefore
/// breadth-first, and a deep tree drains over more frames than a shallow one.
/// </para>
/// <para>
/// Deferral is gated on <see cref="Enabled"/>, which an <see cref="Platform.AppHost"/> sets while its
/// update loop is running. Without a loop to drain the queue (plain unit tests, tooling) a queued
/// drawable would never be disposed at all, so in that state disposal stays inline.
/// </para>
/// </remarks>
public static class DrawableDisposalQueue
{
    /// <summary>
    /// The default number of drawables disposed per <see cref="Process"/> call.
    /// </summary>
    public const int DEFAULT_ITEMS_PER_FRAME = 250;

    /// <summary>
    /// Maximum number of drawables to dispose per <see cref="Process"/> call. The first queued item is
    /// always processed, so the queue can never stall.
    /// </summary>
    public static int ItemsPerFrameBudget { get; set; } = DEFAULT_ITEMS_PER_FRAME;

    /// <summary>
    /// Whether removal-triggered disposal is deferred to this queue. Set by <see cref="Platform.AppHost"/>
    /// for as long as it is pumping frames; while false, removal disposes inline so a drawable is never
    /// left queued against a loop that will never run.
    /// </summary>
    public static bool Enabled { get; set; }

    private static readonly ConcurrentQueue<PendingRelease> queue = new ConcurrentQueue<PendingRelease>();

    private static readonly GlobalStatistic<int> stat_pending = GlobalStatistics.Get<int>("Drawables", "Disposal Queue (pending)");
    private static readonly GlobalStatistic<long> stat_disposed = GlobalStatistics.Get<long>("Drawables", "Disposed");

    /// <summary>
    /// Number of releases currently waiting (approximate, for stats/debugging).
    /// </summary>
    public static int PendingCount => queue.Count;

    /// <summary>
    /// Enqueues a removed drawable for disposal on a later update frame, or disposes it inline when
    /// <see cref="Enabled"/> is false.
    /// </summary>
    public static void Enqueue(Drawable drawable)
    {
        if (drawable == null || drawable.IsDisposed)
            return;

        if (!Enabled)
        {
            drawable.Dispose();
            return;
        }

        // Read by Process to skip a drawable that was re-added (and therefore un-queued) in the
        // meantime; a ConcurrentQueue cannot have an entry removed.
        drawable.DisposalPending = true;

        queue.Enqueue(new PendingRelease(drawable, null));
        stat_pending.Value = queue.Count;
    }

    /// <summary>
    /// Enqueues a cleanup action to run on the update thread within a later frame's budget, or runs it
    /// inline when <see cref="Enabled"/> is false.
    /// </summary>
    /// <remarks>
    /// Deliberately internal and deliberately not a general-purpose scheduler. It exists because some
    /// framework cleanup must not be routed through a <see cref="Drawable.Scheduler"/>: a discarded async
    /// load is usually discarded because the drawable that started it was torn down, and a disposed
    /// drawable's scheduler is cleared and never runs again. This queue is drained by the host every frame
    /// regardless of any drawable's state, so work handed to it cannot be silently dropped.
    /// </remarks>
    internal static void EnqueueCleanup(Action release)
    {
        if (release.IsNull())
            return;

        if (!Enabled)
        {
            release();
            return;
        }

        queue.Enqueue(new PendingRelease(null, release));
        stat_pending.Value = queue.Count;
    }

    /// <summary>
    /// Runs queued releases until the per-frame budget is spent (always at least one).
    /// Call once per frame from the update thread.
    /// </summary>
    /// <returns>The number of releases run.</returns>
    public static int Process() => process(ItemsPerFrameBudget);

    /// <summary>
    /// Runs everything currently queued, ignoring the per-frame budget. Used at shutdown, and by tests
    /// that need the queue settled before asserting.
    /// </summary>
    /// <returns>The number of releases run.</returns>
    public static int Flush() => process(int.MaxValue);

    private static int process(int budget)
    {
        int processed = 0;
        long disposed = 0;

        while (queue.TryDequeue(out var pending))
        {
            if (pending.Drawable is { } drawable)
            {
                // Re-added between being queued and now: it is live again and must not be disposed.
                if (!drawable.DisposalPending)
                    continue;

                drawable.DisposalPending = false;
                drawable.Dispose();
                disposed++;
            }
            else
            {
                pending.Release!();
            }

            // Counted after the fact so a re-added drawable does not consume budget it did no work for.
            if (++processed >= budget)
                break;
        }

        if (processed > 0)
        {
            stat_disposed.Value += disposed;
            stat_pending.Value = queue.Count;
        }

        return processed;
    }

    /// <summary>
    /// One queued release: either a drawable to dispose, or a bare cleanup action.
    /// </summary>
    private readonly record struct PendingRelease(Drawable? Drawable, Action? Release);
}
