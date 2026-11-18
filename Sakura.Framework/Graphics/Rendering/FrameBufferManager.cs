// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Threading;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Manage the indices for triple buffering to safely hand off data between update and draw loop.
/// </summary>
public class FrameBufferManager
{
    private static readonly GlobalStatistic<int> stat_dropped_frames = GlobalStatistics.Get<int>("Buffers", "Dropped Frames", StatisticKind.Cumulative);
    private static readonly GlobalStatistic<int> stat_draw_starvation = GlobalStatistics.Get<int>("Buffers", "Draw Starvation", StatisticKind.Cumulative);

    private int updateIndex = 0;
    private int drawIndex = 1;
    private int waitingIndex = 2;

    private bool hasWaitingFrame = false;
    private SpinLock swapLock = new SpinLock(false);

    /// <summary>
    /// Gets the index of the buffer the update thread should write to.
    /// </summary>
    public int GetUpdateIndex() => updateIndex;

    /// <summary>
    /// Marks the current update buffer as ready and swaps it into the waiting slot.
    /// </summary>
    public void FinishUpdate()
    {
        bool dropped;
        bool lockTaken = false;

        try
        {
            swapLock.Enter(ref lockTaken);

            // The draw thread was too slow and missed the previous frame
            // Means that update is faster than draw
            dropped = hasWaitingFrame;

            // Swap the current update buffer with the waiting buffer
            (updateIndex, waitingIndex) = (waitingIndex, updateIndex);
            hasWaitingFrame = true;
        }
        finally
        {
            if (lockTaken)
                swapLock.Exit(false);
        }

        // Counted outside the lock: only the update thread writes it.
        if (dropped)
            stat_dropped_frames.Value++;
    }

    /// <summary>
    /// Gets the index of the buffer the draw thread should read from.
    /// If a new frame is waiting, it swaps it into the draw slot.
    /// </summary>
    public int GetDrawIndex()
    {
        bool starved;
        int index;
        bool lockTaken = false;

        try
        {
            swapLock.Enter(ref lockTaken);

            starved = !hasWaitingFrame;

            if (!starved)
            {
                (drawIndex, waitingIndex) = (waitingIndex, drawIndex);
                hasWaitingFrame = false;
            }

            index = drawIndex;
        }
        finally
        {
            if (lockTaken)
                swapLock.Exit(false);
        }

        // Counted outside the lock: only the draw thread writes it.
        if (starved)
            stat_draw_starvation.Value++;

        return index;
    }
}
