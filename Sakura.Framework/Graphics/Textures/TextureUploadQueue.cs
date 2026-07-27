// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// A draw-thread work queue for texture uploads with a per-frame byte budget. Uploads are enqueued from
/// any thread (typically an async texture load) and drained on the draw thread once per frame by
/// <see cref="Process"/>, stopping once the frame's budget is spent so the rest carry over to later
/// frames.
/// </summary>
/// <remarks>
/// At least one queued upload is always processed per call, so an item larger than the whole budget can
/// never be starved. Ordering is FIFO, preserving the order uploads were requested in.
/// </remarks>
public sealed class TextureUploadQueue
{
    /// <summary>
    /// The default per-frame budget (bytes). ~8 MB is roughly 8 downscaled cover textures per frame from tested project.
    /// </summary>
    public const long DEFAULT_BYTES_PER_FRAME = 8 * 1024 * 1024;

    /// <summary>
    /// Maximum total upload bytes to process per <see cref="Process"/> call. The first queued item is
    /// always processed even if it alone exceeds this.
    /// </summary>
    public long BytesPerFrameBudget { get; set; } = DEFAULT_BYTES_PER_FRAME;

    private readonly ConcurrentQueue<PendingUpload> queue = new ConcurrentQueue<PendingUpload>();

    private static readonly GlobalStatistic<int> stat_pending = GlobalStatistics.Get<int>("Textures", "Upload Queue (pending)");
    private static readonly GlobalStatistic<long> stat_processed = GlobalStatistics.Get<long>("Textures", "Uploads Processed");
    private static readonly GlobalStatistic<long> stat_bytes_last_frame = GlobalStatistics.Get<long>("Textures", "Upload Bytes / Frame");

    /// <summary>
    /// Number of uploads currently waiting (approximate, for stats/debugging).
    /// </summary>
    public int PendingCount => queue.Count;

    /// <summary>
    /// Enqueue an upload to run on the draw thread within the frame budget.
    /// </summary>
    /// <param name="upload">The upload action; runs on the draw thread.</param>
    /// <param name="approximateBytes">Rough size of the upload, used to spend the budget.</param>
    public void Enqueue(Action upload, long approximateBytes)
    {
        if (upload == null)
            throw new ArgumentNullException(nameof(upload));

        queue.Enqueue(new PendingUpload(upload, Math.Max(0, approximateBytes)));
        stat_pending.Value = queue.Count;
    }

    /// <summary>
    /// Drains queued uploads on the draw thread until the per-frame budget is spent (always at least one).
    /// Call once per frame from the renderer's frame start.
    /// </summary>
    public void Process()
    {
        long spent = 0;
        int processed = 0;

        while (queue.TryDequeue(out var item))
        {
            item.Action();
            spent += item.Bytes;
            processed++;

            // Budget checked after running so a single over-budget upload still makes progress.
            if (spent >= BytesPerFrameBudget)
                break;
        }

        stat_bytes_last_frame.Value = spent;
        stat_pending.Value = queue.Count;
        if (processed > 0)
            stat_processed.Value += processed;
    }

    private readonly record struct PendingUpload(Action Action, long Bytes);
}
