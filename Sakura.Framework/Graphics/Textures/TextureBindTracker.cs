// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Counts GPU texture binds, both in total and per texture, for the frame currently being drawn and the
/// one before it.
/// </summary>
public static class TextureBindTracker
{
    private static readonly GlobalStatistic<int> stat_binds = GlobalStatistics.Get<int>("Renderer", "Texture Binds");
    private static readonly GlobalStatistic<int> stat_binds_last_frame = GlobalStatistics.Get<int>("Renderer", "Texture Binds (Last Frame)");

    /// <summary>
    /// Which frame is being drawn. Incremented by <see cref="EndFrame"/> and used by
    /// <see cref="TextureBindCounter"/> to tell this frame's binds from the previous frame's without
    /// having to sweep every live texture at each frame boundary.
    /// </summary>
    internal static long FrameIndex;

    private static int binds;

    /// <summary>
    /// Records one bind against the frame total. Called by <see cref="TextureBindCounter.Record"/>, which
    /// is what a backend's <see cref="INativeTexture.Bind"/> calls (never call this directly, or the
    /// total and the per-texture counts stop agreeing)
    /// </summary>
    internal static void RecordBind()
    {
        binds++;
        stat_binds.Value = binds;
    }

    /// <summary>
    /// Closes the current frame by publishing its total as the last-frame figure and starts the next one.
    /// Called by each renderer at the end of its draw, after the last draw call for the frame has
    /// been issued (on GL, after the final batch flush, or the batched binds would land in the next
    /// frame's count).
    /// </summary>
    public static void EndFrame()
    {
        stat_binds_last_frame.Value = binds;
        binds = 0;
        FrameIndex++;
    }
}

/// <summary>
/// One texture's bind count. Every <see cref="INativeTexture"/> and <see cref="INativeVideoTexture"/>
/// owns one and reports it to the texture viewer.
/// </summary>
public sealed class TextureBindCounter
{
    /// <summary>
    /// The frame <see cref="count"/> was accumulated during
    /// </summary>
    private long stampedFrame = -1;

    /// <summary>
    /// Binds during <see cref="stampedFrame"/>.
    /// </summary>
    private int count;

    /// <summary>
    /// Binds during the frame before <see cref="stampedFrame"/>, if it was the frame right before it.
    /// </summary>
    private int previousCount;

    /// <summary>
    /// Records one bind of this texture, against both this counter and the frame total. Draw thread only.
    /// </summary>
    internal void Record()
    {
        long frame = TextureBindTracker.FrameIndex;

        if (stampedFrame != frame)
        {
            // Whatever was counted under the old stamp is finished. It only describes the previous frame
            // if that stamp was the previous frame. Anything older means this texture went a whole frame
            // without being bound.
            previousCount = stampedFrame == frame - 1 ? count : 0;
            count = 0;
            stampedFrame = frame;
        }

        count++;
        TextureBindTracker.RecordBind();
    }

    /// <summary>
    /// How many times this texture was bound during the last completed frame. Safe to read from any
    /// thread.
    /// </summary>
    public int LastFrame
    {
        get
        {
            long frame = TextureBindTracker.FrameIndex;

            // Already bound this frame, so the roll-forward has run, and the completed total moved aside.
            if (stampedFrame == frame)
                return previousCount;

            // Not bound yet this frame, so the running count is still the completed frame.
            return stampedFrame == frame - 1 ? count : 0;
        }
    }
}
