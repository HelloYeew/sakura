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
    }
}
