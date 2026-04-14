// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Manage the indices for triple buffering to safely hand off data between update and draw loop.
/// </summary>
public class FrameBufferManager
{
    private int updateIndex = 0;
    private int drawIndex = 1;
    private int waitingIndex = 2;

    private bool hasWaitingFrame = false;
    private readonly object swapLock = new object();

    /// <summary>
    /// Gets the index of the buffer the update thread should write to.
    /// </summary>
    public int GetUpdateIndex() => updateIndex;

    /// <summary>
    /// Marks the current update buffer as ready and swaps it into the waiting slot.
    /// </summary>
    public void FinishUpdate()
    {
        lock (swapLock)
        {
            if (hasWaitingFrame)
            {
                // The draw thread was too slow and missed the previous frame
                // Means that update is faster than draw
                GlobalStatistics.Get<int>("Buffers", "Dropped Frames").Value++;
            }

            // Swap the current update buffer with the waiting buffer
            (updateIndex, waitingIndex) = (waitingIndex, updateIndex);
            hasWaitingFrame = true;
        }
    }

    /// <summary>
    /// Gets the index of the buffer the draw thread should read from.
    /// If a new frame is waiting, it swaps it into the draw slot.
    /// </summary>
    public int GetDrawIndex()
    {
        lock (swapLock)
        {
            if (hasWaitingFrame)
            {
                // new frame finished updating, swap it into the active draw slot
                (drawIndex, waitingIndex) = (waitingIndex, drawIndex);
                hasWaitingFrame = false;
            }
            else
            {
                // Update thread hasn't finished a new frame yet
                // so the draw thread is forced to reuse the previous frame
                GlobalStatistics.Get<int>("Buffers", "Draw Starvation").Value++;
            }
            return drawIndex;
        }
    }
}
