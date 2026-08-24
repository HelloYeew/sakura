// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Turns a channel's mix cursor into the position a listener is actually hearing, and keeps that
/// answer moving forwards.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class OutputLatencyCompensator
{
    /// <summary>
    /// Slack on top of the compensation window, absorbing jitter in the queue reading when the queue
    /// itself is near empty.
    /// </summary>
    private const double wobble_slack_ms = 1.0;

    private double lastReported;
    private bool primed;

    /// <summary>
    /// The audible position for a channel whose mix cursor is at <paramref name="rawMs"/>.
    /// </summary>
    /// <param name="rawMs">The channel's own cursor, in milliseconds of source time.</param>
    /// <param name="latencyMs">Output latency, from <see cref="ISDLAudioContext.OutputLatencyMs"/>.</param>
    /// <param name="rate">The channel's playback rate, as <see cref="IAudioChannel.Frequency"/>.</param>
    public double Compensate(double rawMs, double latencyMs, double rate)
    {
        double window = Math.Max(0, latencyMs) * Math.Max(0, rate);
        double audible = Math.Max(0, rawMs - window);

        if (!primed)
        {
            primed = true;
            lastReported = audible;
            return audible;
        }

        double backwards = lastReported - audible;

        // Anything larger than the window that produced it is not wobble, it is the position genuinely
        // having moved, by something that should have called Reset and did not.
        if (backwards > 0 && backwards <= window + wobble_slack_ms)
            return lastReported;

        lastReported = audible;
        return audible;
    }

    /// <summary>
    /// Forgets the monotonic floor, for when the position is meant to jump: a seek, a loop wrap, or a
    /// stop that rewinds.
    /// </summary>
    public void Reset() => primed = false;
}
