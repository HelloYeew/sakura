// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Turns a channel's mix cursor into the position a listener is actually hearing and keeps that
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

    private readonly Func<double> clock;

    /// <summary>
    /// The rate the queued audio was mixed at before a <see cref="BeginRateTransition"/>, and when
    /// that was, or null outside a transition.
    /// </summary>
    private double? transitionFromRate;

    private double transitionStartMs;

    /// <param name="clock">Milliseconds of real time, for timing a rate transition. Defaults to a stopwatch.</param>
    public OutputLatencyCompensator(Func<double>? clock = null)
    {
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
    }

    /// <summary>
    /// Says the rate is about to change without the position moving, so the queue still holds a
    /// latency's worth of audio mixed at <paramref name="fromRate"/>.
    /// </summary>
    /// <remarks>
    /// Without this a change applies to the whole window at once: a slow-down makes the position leap
    /// forward by (old − new) × latency, and the clock chain hears that as a skip. With it, the window
    /// moves from the old rate to the new one as the queue drains, which is what the listener hears.
    /// Opt-in, used for <see cref="IAudioChannel.Tempo"/>, because the hold in <see cref="Compensate"/>
    /// already covers the small steps a continuously changing frequency makes.
    /// </remarks>
    public void BeginRateTransition(double fromRate)
    {
        transitionFromRate = fromRate;
        transitionStartMs = clock();
    }

    /// <summary>
    /// The audible position for a channel whose mix cursor is at <paramref name="rawMs"/>.
    /// </summary>
    /// <param name="rawMs">The channel's own cursor, in milliseconds of source time.</param>
    /// <param name="latencyMs">Output latency, from <see cref="ISDLAudioContext.OutputLatencyMs"/>.</param>
    /// <param name="rate">The channel's playback rate, as <see cref="IAudioChannel.Frequency"/>.</param>
    public double Compensate(double rawMs, double latencyMs, double rate)
    {
        double window = Math.Max(0, latencyMs) * Math.Max(0, transitionRate(latencyMs, rate));
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
    /// The rate the queued window is effectively at: the old rate for the part mixed before a
    /// transition began, the new one for the rest.
    /// </summary>
    private double transitionRate(double latencyMs, double rate)
    {
        if (transitionFromRate is not { } from)
            return rate;

        double elapsed = clock() - transitionStartMs;

        if (latencyMs <= 0 || elapsed >= latencyMs)
        {
            transitionFromRate = null;
            return rate;
        }

        return from + (rate - from) * (elapsed / latencyMs);
    }

    /// <summary>
    /// Forgets the monotonic floor, for when the position is meant to jump: a seek, a loop wrap, or a
    /// stop that rewinds.
    /// </summary>
    public void Reset() => primed = false;
}
