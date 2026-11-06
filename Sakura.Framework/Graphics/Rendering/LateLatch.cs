// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Takes the newest update as late as a frame can still make the next refresh. Under VSync the draw already waits for the
/// display to hand it a drawable; once it has one, it waits again until shortly before the next refresh, less how long
/// drawing and the GPU take and some slack, and only then takes the update to draw. The frame shows content fresher by
/// up to a refresh, and the frame rate stays as it was, since the next drawable comes at the next refresh either way.
/// </summary>
/// <remarks>
/// <para>
/// The next refresh comes from when frames were last shown (<see cref="RefreshCadence"/>) and the display's rate. The
/// slack starts at <see cref="initial_slack_ms"/>, grows by <see cref="slack_step_up_ms"/> each time a frame misses its
/// refresh while waiting, and gives a little back with each frame that doesn't. Frames that keep missing with the most
/// slack turn it off for good, saying so once.
/// </para>
/// <para>
/// Run on the draw thread only.
/// </para>
/// </remarks>
internal sealed class LateLatch : IDisposable
{
    private const double initial_slack_ms = 1.0;
    private const double min_slack_ms = 0.5;
    private const double max_slack_ms = 4.0;
    private const double slack_step_up_ms = 0.5;

    // About a minute of frames on time at 120 Hz brings the slack down by a millisecond.
    private const double slack_step_down_ms = 0.00015;

    // Misses with the most slack before it's given up.
    private const int misses_at_most_slack_to_give_up = 20;

    private static readonly double ticks_per_ms = Stopwatch.Frequency / 1000.0;

    private static readonly GlobalStatistic<double> stat_waited = GlobalStatistics.Get<double>("Renderer", "Late Latch: Waited", StatisticKind.Gauge, StatisticUnit.Milliseconds);
    private static readonly GlobalStatistic<double> stat_slack = GlobalStatistics.Get<double>("Renderer", "Late Latch: Slack", StatisticKind.Gauge, StatisticUnit.Milliseconds);

    private readonly bool allowSpinning;
    private FrameWaiter? waiter;
    private double slackMs = initial_slack_ms;
    private int missesAtMostSlack;
    private double waitedMs;

    /// <summary>
    /// Whether the last frame waited, and so whether the next one shown counts for the slack.
    /// </summary>
    public bool Waited { get; private set; }

    /// <summary>
    /// Whether it's been turned off for good, frames missing their refresh even with the most slack.
    /// </summary>
    public bool GivenUp { get; private set; }

    /// <param name="allowSpinning">Whether the wait may spin its last stretch, as the frame threads' may.</param>
    public LateLatch(bool allowSpinning)
    {
        this.allowSpinning = allowSpinning;
    }

    /// <summary>
    /// Waits, once the frame has its drawable, until the latest it can take the update and still make the next refresh.
    /// Doesn't wait before a frame has been seen on screen, where that moment is already past, or where it's a whole
    /// refresh or more away, which would cost a frame.
    /// </summary>
    /// <param name="cadence">When frames were shown, which the next refresh is predicted from.</param>
    /// <param name="periodTicks">The display's refresh period, in <see cref="Stopwatch"/> ticks.</param>
    /// <param name="drawMs">How long drawing a frame takes, settled.</param>
    /// <param name="gpuMs">How long the GPU takes, settled, or 0 where that isn't known.</param>
    /// <param name="precise">Whether to land on the moment with the precise timer, as the draw thread does while it's tight.</param>
    public void Wait(RefreshCadence cadence, double periodTicks, double drawMs, double gpuMs, bool precise)
    {
        Waited = false;

        if (GivenUp || cadence.LastShown == 0 || periodTicks <= 0)
            return;

        long now = Stopwatch.GetTimestamp();
        long until = Deadline(RefreshCadence.NextRefresh(cadence.LastShown, periodTicks, now), drawMs, gpuMs, slackMs);

        if (until <= now || until - now >= periodTicks)
            return;

        waiter ??= new FrameWaiter("DrawThread Late Latch", allowSpinning);
        waiter.WaitUntil(until, precise);

        Waited = true;
        waitedMs += ((until - now) / ticks_per_ms - waitedMs) * 0.05;
        stat_waited.Value = Math.Round(waitedMs, 2);
    }

    /// <summary>
    /// Takes whether a frame that waited made its refresh: more slack after a miss, a little less after each on time.
    /// </summary>
    public void FrameShown(bool missedRefresh)
    {
        if (GivenUp)
            return;

        if (!missedRefresh)
        {
            slackMs = Math.Max(min_slack_ms, slackMs - slack_step_down_ms);
            stat_slack.Value = Math.Round(slackMs, 2);
            return;
        }

        if (slackMs >= max_slack_ms && ++missesAtMostSlack >= misses_at_most_slack_to_give_up)
        {
            GivenUp = true;
            Logger.Verbose($"Late latch is off: frames kept missing their refresh with {max_slack_ms} ms to spare.");
            return;
        }

        slackMs = Math.Min(max_slack_ms, slackMs + slack_step_up_ms);
        stat_slack.Value = Math.Round(slackMs, 2);
    }

    /// <summary>
    /// When to take the update for a frame to make <paramref name="nextRefresh"/>: before it by the draw, the GPU and the
    /// slack.
    /// </summary>
    internal static long Deadline(long nextRefresh, double drawMs, double gpuMs, double slackMs) =>
        nextRefresh - (long)((Math.Max(0, drawMs) + Math.Max(0, gpuMs) + slackMs) * ticks_per_ms);

    public void Dispose() => waiter?.Dispose();
}
