// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Follows when frames reach the screen under VSync, refresh by refresh: when the latest was shown, which the next
/// refreshes are predicted from, and which frames missed their refresh, shown more than half a step later than a refresh
/// after the frame before. A late frame waits for the display's next step: a whole refresh at a fixed rate, so a miss is
/// over one and a half refreshes, but on a display whose refresh varies in steps only the step (4.17 ms on ProMotion,
/// where a late frame shows 12.5 ms after the one before, exactly one and a half of its 8.33 ms refreshes).
/// </summary>
/// <remarks>
/// Counted in windows of <see cref="window_frames"/> frames, a few seconds' worth, so a single hitch reads as noise and
/// a frame missed every few seconds doesn't.
/// </remarks>
internal sealed class RefreshCadence
{
    /// <summary>
    /// Frames to a window.
    /// </summary>
    private const int window_frames = 600;

    /// <summary>
    /// Further apart than this many refreshes isn't a frame that missed one, but the app pausing (a stall, a drag of the
    /// window), which says nothing about the frames' pace.
    /// </summary>
    private const double pause_refreshes = 8;

    private int windowFrames;
    private int windowMisses;

    /// <summary>
    /// When the latest frame was shown, as a <see cref="Stopwatch"/> timestamp, or 0 before any.
    /// </summary>
    public long LastShown { get; private set; }

    /// <summary>
    /// Takes a frame seen on screen.
    /// </summary>
    /// <param name="shown">When it was shown, as a <see cref="Stopwatch"/> timestamp.</param>
    /// <param name="periodTicks">The display's refresh period, in <see cref="Stopwatch"/> ticks.</param>
    /// <param name="stepTicks">
    /// The step between the display's refresh intervals, in <see cref="Stopwatch"/> ticks, or 0 for a whole refresh, as at a
    /// fixed rate. A step longer than the period counts as the period.
    /// </param>
    /// <returns>Whether it missed its refresh.</returns>
    public bool Add(long shown, double periodTicks, double stepTicks = 0)
    {
        bool missed = false;

        if (LastShown != 0 && periodTicks > 0 && shown > LastShown)
        {
            double interval = shown - LastShown;

            if (interval < periodTicks * pause_refreshes)
            {
                missed = interval > MissedAfter(periodTicks, stepTicks);
                windowFrames++;

                if (missed)
                    windowMisses++;
            }
        }

        if (shown > LastShown)
            LastShown = shown;

        return missed;
    }

    /// <summary>
    /// Ends the current window once it's full: whether more than 1% of its frames missed their refresh.
    /// </summary>
    /// <param name="misses">How many did, once the window is full.</param>
    /// <returns>True for a full window that missed too many, and starts the next one; false before a window is full.</returns>
    public bool TryEndWindow(out int misses)
    {
        misses = windowMisses;

        if (windowFrames < window_frames)
            return false;

        windowFrames = 0;
        windowMisses = 0;
        return misses * 100 > window_frames;
    }

    /// <summary>
    /// Forgets what's been seen, as after a change that sets frames' pace afresh (VSync turned on, a new display).
    /// </summary>
    public void Reset()
    {
        LastShown = 0;
        windowFrames = 0;
        windowMisses = 0;
    }

    /// <summary>
    /// How long after the frame before a frame shown missed its refresh: half a step past the refresh, where a frame that
    /// made it can't be and a late one, a step later, is.
    /// </summary>
    /// <param name="periodTicks">The display's refresh period.</param>
    /// <param name="stepTicks">The step between its refresh intervals, or 0 for a whole refresh.</param>
    internal static double MissedAfter(double periodTicks, double stepTicks) => periodTicks + (stepTicks > 0 ? Math.Min(stepTicks, periodTicks) : periodTicks) / 2;

    /// <summary>
    /// The first refresh at or after <paramref name="after"/>, on the refreshes through <paramref name="lastShown"/>.
    /// </summary>
    internal static long NextRefresh(long lastShown, double periodTicks, long after)
    {
        if (after <= lastShown)
            return lastShown;

        double refreshes = Math.Ceiling((after - lastShown) / periodTicks);
        return lastShown + (long)(refreshes * periodTicks);
    }
}
