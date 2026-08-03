// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// A process-wide queue of native graphics resources whose owning managed object was collected without
/// being disposed. Drained on the draw thread at the start of every frame.
/// </summary>
/// <remarks>
/// <para>
/// This is a safety net, not the disposal mechanism. Native handles (GPU textures, framebuffers) are
/// invisible to the GC: dropping the last reference to a wrapper without calling <c>Dispose</c> would
/// otherwise leak the underlying GPU allocation for the lifetime of the process. Wrapper finalizers
/// enqueue here instead, so a missed dispose becomes a delayed reclaim rather than a permanent leak.
/// </para>
/// <para>
/// It is static deliberately. Finalizers run on their own thread with no ordering guarantees, so a
/// finalizer must not reach into other managed objects that may themselves already be finalized — a
/// static queue is always reachable and safe to enqueue to. For the same reason, callers must capture
/// only value-type handles (and any long-lived API object needed to release them) in the action, never
/// <c>this</c>: capturing the finalizing object would resurrect it.
/// </para>
/// <para>
/// A non-zero <c>Textures → Reclaimed by GC</c> statistic means something is failing to dispose a
/// native resource. The net is working, but the call site should be fixed.
/// </para>
/// </remarks>
public static class NativeDisposalQueue
{
    private static readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();

    private static readonly GlobalStatistic<long> stat_reclaimed = GlobalStatistics.Get<long>("Textures", "Reclaimed by GC");
    private static readonly GlobalStatistic<int> stat_pending = GlobalStatistics.Get<int>("Textures", "Reclaim Queue (pending)");

    /// <summary>
    /// Number of reclaims currently waiting for the next frame (approximate, for stats/debugging).
    /// </summary>
    public static int PendingCount => queue.Count;

    /// <summary>
    /// Enqueues a native release to run on the draw thread. Safe to call from a finalizer.
    /// </summary>
    /// <param name="release">
    /// The native release action. Must capture only handles and long-lived API objects — never the
    /// object being finalized.
    /// </param>
    public static void Enqueue(Action release)
    {
        if (release == null)
            return;

        queue.Enqueue(release);
        stat_pending.Value = queue.Count;
    }

    /// <summary>
    /// Runs every queued release. Call once per frame on the draw thread, before any rendering.
    /// </summary>
    /// <returns>The number of resources reclaimed.</returns>
    public static int Process()
    {
        int processed = 0;

        while (queue.TryDequeue(out var release))
        {
            try
            {
                release();
            }
            catch (Exception)
            {
                // A failed native release must not take down the frame, the resource is already
                // orphaned, so there is nothing to retry against.
            }

            processed++;
        }

        if (processed > 0)
        {
            stat_reclaimed.Value += processed;
            stat_pending.Value = queue.Count;
        }

        return processed;
    }
}
