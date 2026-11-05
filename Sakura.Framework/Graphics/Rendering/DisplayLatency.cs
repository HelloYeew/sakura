// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Settles how long frames take to reach the screen into one figure, from the frames a renderer can tell were shown:
/// <see cref="IRenderer.DisplayLatencyMilliseconds"/>.
/// </summary>
/// <remarks>
/// <para>
/// A reading runs from the update a frame shows (<see cref="IRenderer.SetFrameContentTime"/>) to the moment it was on
/// screen, so it takes in the wait for the draw, the GPU, the queue of presents, the compositor and the scan-out: all
/// but the panel's own response, which nothing reports.
/// </para>
/// <para>
/// Readings spread over up to a refresh, by where in it the update happened to land, so the figure is their
/// average, eased into. A reading that keeps landing far from it is the display having changed (vsync turned off, a
/// window moved to another monitor) rather than that spread, and is taken at once.
/// </para>
/// </remarks>
internal sealed class DisplayLatency
{
    private const double smoothing = 0.05;

    /// <summary>
    /// Further from the settled figure than a refresh's spread could put a reading, down to 40 Hz.
    /// </summary>
    private const double jump_ms = 25;

    /// <summary>
    /// Readings that far out in a row before they're believed: a few frames, so one slow frame isn't.
    /// </summary>
    private const int readings_to_jump = 10;

    /// <summary>
    /// Beyond any display's latency: a reading out here is a frame shown after a stall, or clocks that don't agree.
    /// </summary>
    private const double max_ms = 500;

    // Logged once there are this many, a couple of seconds' worth, when the figure has settled.
    private const int readings_to_log = 120;

    private static readonly GlobalStatistic<double> stat_display_latency = GlobalStatistics.Get<double>("Renderer", "Display Latency", StatisticKind.Gauge, StatisticUnit.Milliseconds);

    // Frames measured since the host last took them (TryTakeShown), for it to split each one's latency into where the
    // time went. The host takes them every frame, so only the newest few are kept.
    private const int shown_frames_kept = 8;

    private readonly ShownFrame[] shownFrames = new ShownFrame[shown_frames_kept];
    private int shownFirst;
    private int shownCount;

    private readonly string source;
    private int farReadings;
    private int readings;

    /// <param name="source">Where the readings come from, for the log.</param>
    public DisplayLatency(string source)
    {
        this.source = source;
    }

    /// <summary>
    /// The settled latency, in milliseconds, or null before the first reading.
    /// </summary>
    public double? Milliseconds { get; private set; }

    /// <summary>
    /// The milliseconds from <paramref name="contentTimestamp"/> to <paramref name="shownTimestamp"/>, both
    /// <see cref="Stopwatch"/> timestamps.
    /// </summary>
    public static double Between(long contentTimestamp, long shownTimestamp) => (shownTimestamp - contentTimestamp) * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// A time in nanoseconds of the monotonic clock as a <see cref="Stopwatch"/> timestamp: the same clock on Android and
    /// Linux, where present times come in nanoseconds.
    /// </summary>
    public static long TicksFromNanoseconds(long nanoseconds) => Stopwatch.Frequency == 1_000_000_000
        ? nanoseconds
        : (long)(nanoseconds * (Stopwatch.Frequency / 1_000_000_000.0));

    /// <summary>
    /// Adds the reading for a frame showing the update made at <paramref name="contentTimestamp"/>, on screen at
    /// <paramref name="shownTimestamp"/>, and keeps it for <see cref="TryTakeShown"/>, with when the GPU finished it
    /// where the renderer knows (<paramref name="gpuDoneTimestamp"/>, else 0). All <see cref="Stopwatch"/> timestamps.
    /// </summary>
    public void AddFrame(long contentTimestamp, long shownTimestamp, long gpuDoneTimestamp = 0)
    {
        double milliseconds = Between(contentTimestamp, shownTimestamp);
        Add(milliseconds);

        if (milliseconds <= 0 || milliseconds > max_ms)
            return;

        // Full, the oldest gives way.
        if (shownCount == shown_frames_kept)
        {
            shownFirst = (shownFirst + 1) % shown_frames_kept;
            shownCount--;
        }

        shownFrames[(shownFirst + shownCount) % shown_frames_kept] = new ShownFrame(contentTimestamp, gpuDoneTimestamp, shownTimestamp);
        shownCount++;
    }

    /// <summary>
    /// Takes the oldest frame <see cref="AddFrame"/> kept, or returns false where none is left.
    /// </summary>
    public bool TryTakeShown(out ShownFrame frame)
    {
        if (shownCount == 0)
        {
            frame = default;
            return false;
        }

        frame = shownFrames[shownFirst];
        shownFirst = (shownFirst + 1) % shown_frames_kept;
        shownCount--;
        return true;
    }

    public void Add(double milliseconds)
    {
        if (milliseconds <= 0 || milliseconds > max_ms)
            return;

        if (Milliseconds is not double settled)
            Milliseconds = milliseconds;
        else if (Math.Abs(milliseconds - settled) > jump_ms)
        {
            if (++farReadings < readings_to_jump)
                return;

            Milliseconds = milliseconds;
            farReadings = 0;
        }
        else
        {
            farReadings = 0;
            Milliseconds = settled + (milliseconds - settled) * smoothing;
        }

        stat_display_latency.Value = Math.Round(Milliseconds.Value, 1);

        if (++readings == readings_to_log)
            Logger.Verbose($"Frames reach the screen {Milliseconds:0.0} ms after the update they show, from {source}");
    }

    /// <summary>
    /// Forgets the figure, for a display that's been replaced.
    /// </summary>
    public void Reset()
    {
        Milliseconds = null;
        farReadings = 0;
        readings = 0;
        shownFirst = 0;
        shownCount = 0;
    }
}
