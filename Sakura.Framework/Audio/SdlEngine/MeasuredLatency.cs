// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Settles the output latency read from the output's timestamps into one figure to compensate positions by.
/// </summary>
/// <remarks>
/// A reading is off by up to one callback's worth of audio, by how far between two callbacks it was taken, and on
/// average by nothing. Positions only move on once a callback too, so the average is the figure to subtract. A
/// reading that keeps landing far from the settled figure is the output having changed (headphones, Bluetooth)
/// rather than noise, and is taken at once instead of eased into over seconds of drifting sync.
/// </remarks>
internal sealed class MeasuredLatency
{
    private const double smoothing = 0.1;

    /// <summary>
    /// Further from the settled figure than a callback could put a reading, on any output.
    /// </summary>
    private const double jump_ms = 15;

    /// <summary>
    /// Readings that far out in a row before they're believed.
    /// </summary>
    private const int readings_to_jump = 3;

    /// <summary>
    /// Beyond any output's latency: a reading out here is a timestamp left over from before the stream restarted.
    /// </summary>
    private const double max_ms = 1000;

    /// <summary>
    /// Readings in a row that can't be a latency before the settled figure is dropped: a second's worth at the rate
    /// they're taken. They come when the output's timestamps stop adding up, as they do after the system rebuilt a
    /// stream under it, and the figure from before stands for nothing then. Kept, it moved every position by it for
    /// good (862 ms, once).
    /// </summary>
    private const int impossible_readings_to_forget = 10;

    private int farReadings;
    private int impossibleReadings;

    /// <summary>
    /// The settled latency, in milliseconds, or null before the first reading, and after readings stopped making sense.
    /// </summary>
    public double? Milliseconds { get; private set; }

    public void Add(double milliseconds)
    {
        if (milliseconds <= 0 || milliseconds > max_ms)
        {
            if (++impossibleReadings >= impossible_readings_to_forget)
                Reset();

            return;
        }

        impossibleReadings = 0;

        if (Milliseconds is not double settled)
        {
            Milliseconds = milliseconds;
            return;
        }

        if (Math.Abs(milliseconds - settled) > jump_ms)
        {
            if (++farReadings >= readings_to_jump)
            {
                Milliseconds = milliseconds;
                farReadings = 0;
            }

            return;
        }

        farReadings = 0;
        Milliseconds = settled + (milliseconds - settled) * smoothing;
    }

    /// <summary>
    /// Forgets the figure, for a stream that's been replaced.
    /// </summary>
    public void Reset()
    {
        Milliseconds = null;
        farReadings = 0;
        impossibleReadings = 0;
    }
}
