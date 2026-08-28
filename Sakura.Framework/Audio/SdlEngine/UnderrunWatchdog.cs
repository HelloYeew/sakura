// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Decides when the output device is starving badly enough that its buffer is the wrong size for this
/// machine, and what to do about it.
/// </summary>
/// <remarks>
/// Sakura set the default device buffer really aggressive (see <see cref="SDLAudioManager.DEFAULT_DEVICE_BUFFER_FRAMES"/> that's the default value)
/// because measurement says the callback has an order of magnitude of headroom at that size and because a driver that cannot honor the
/// request rounds it up rather than failing. What measurement cannot rule out is scheduling jitter on
/// a machine nobody has tested, and the symptom of getting that wrong is crackling that a user has no
/// way to connect to a setting they have never heard of.
/// </remarks>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class UnderrunWatchdog
{
    /// <summary>
    /// One frame's answer to "is the output path keeping the device fed".
    /// </summary>
    /// <remarks>
    /// Deliberately a judgment the caller makes rather than a number it hands over, because the two
    /// engines fail in different places and only the manager knows which it is running. On the native
    /// engine the device callback mixes on demand and the failure is the callback overrunning the
    /// deadline its own buffer sets; on the managed one a push loop keeps a queue ahead of the device
    /// and the failure is that queue running dry. Both are the device going unfed, which is what a
    /// bigger buffer buys time against.
    /// </remarks>
    public enum Observation
    {
        /// <summary>
        /// Nothing was being served, so this frame says nothing either way and is not counted.
        /// </summary>
        /// <remarks>
        /// An idle device is not healthy, counting silence as evidence of health would let a
        /// mostly quiet session dilute a real failure below <see cref="TRIP_FRACTION"/>.
        /// </remarks>
        Idle,

        /// <summary>
        /// The device was served in time.
        /// </summary>
        Met,

        /// <summary>
        /// The device was not served in time.
        /// </summary>
        /// <remarks>
        /// A missed deadline, not a heard one. Whether any single miss clicks depends on slack the
        /// driver has and this side cannot see, which is why the trip is a share of many observations
        /// rather than a reaction to one — and why nothing that reports this should tell a listener
        /// what they heard.
        /// </remarks>
        Missed,
    }

    /// <summary>
    /// Window over which observations are counted, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Five seconds since long enough that one stall on startup, during a device change, or from a hitch
    /// elsewhere in the app cannot trip it, and short enough that a listener has not been putting up
    /// with crackling for long before something says so.
    /// </remarks>
    public const double CHECK_INTERVAL_MS = 5000;

    /// <summary>
    /// What share of the window's observations have to have missed before the buffer, rather than the
    /// machine having had a bad moment, is the explanation.
    /// </summary>
    /// <remarks>
    /// A fraction and not a count, because a count is only meaningful next to the rate it was sampled
    /// at, and that rate moves with the frame rate. A buffer that is genuinely too small for a machine
    /// misses more or less continuously. The callback is racing the same deadline every time it is
    /// called, so a fifth is far above anything a GC pause, a device change, or a shader compiler
    /// produces and far below what a real misfit does.
    /// </remarks>
    public const double TRIP_FRACTION = 0.2;

    /// <summary>
    /// Observations a window needs before its fraction means anything.
    /// </summary>
    /// <remarks>
    /// A window in which audio played for three frames can trivially read 100%, and startup is full of
    /// windows like that. At a normal frame rate a full window is several hundred observations, so
    /// this only excludes the ones that were mostly silent or mostly stalled.
    /// </remarks>
    public const int MINIMUM_OBSERVATIONS = 100;

    /// <summary>
    /// The largest buffer this will grow to on its own.
    /// </summary>
    /// <remarks>
    /// 1024 frames is roughly what SDL picks unprompted, so it is the point past which "the buffer is
    /// too small" has stopped being a credible explanation. Something else is wrong — a device being
    /// hammered by another app, a driver in a bad state, a machine that is simply overloaded — and
    /// doubling again would trade latency for nothing.
    /// </remarks>
    public const int MAX_AUTO_BACKOFF_FRAMES = 1024;

    /// <summary>
    /// What one tripped window looked like, for the log to quote.
    /// </summary>
    /// <param name="Observations">Frames in the window that had something to say.</param>
    /// <param name="Misses">How many of those found the device unfed.</param>
    public readonly record struct Verdict(int Observations, int Misses)
    {
        /// <summary>
        /// The share of observations that missed, which is the figure the trip is made on.
        /// </summary>
        public double Fraction => Observations == 0 ? 0 : Misses / (double)Observations;
    }

    private int observations;
    private int misses;
    private double sinceCheckMs;

    /// <summary>
    /// Whether this run has already reported sustained starvation.
    /// </summary>
    /// <remarks>
    /// Once per run, for two reasons. A device that is underrunning keeps underrunning, so a warning
    /// every five seconds buries the log it exists to help someone read. And backing off repeatedly
    /// inside one session would take a machine from 128 to 1024 frames in twenty seconds on the
    /// strength of a single bad patch — one doubling per launch converges just as surely and cannot
    /// overshoot on a transient.
    /// </remarks>
    private bool reported;

    /// <summary>
    /// Feeds the watchdog one frame's view of the device.
    /// </summary>
    /// <param name="observation">What this frame saw — see <see cref="Observation"/>.</param>
    /// <param name="frameTime">Elapsed time since the last call, in milliseconds.</param>
    /// <returns>
    /// The window that tripped it or null which is the answer almost every frame, and the answer
    /// forever after it has fired once.
    /// </returns>
    public Verdict? Poll(Observation observation, double frameTime)
    {
        if (reported)
            return null;

        sinceCheckMs += frameTime;

        switch (observation)
        {
            case Observation.Met:
                observations++;
                break;

            case Observation.Missed:
                observations++;
                misses++;
                break;
        }

        if (sinceCheckMs < CHECK_INTERVAL_MS)
            return null;

        var verdict = new Verdict(observations, misses);

        // Each window stands on its own. Carrying counts across them would turn a session that
        // misbehaved once an hour ago into a session that is misbehaving now.
        observations = 0;
        misses = 0;
        sinceCheckMs = 0;

        if (verdict.Observations < MINIMUM_OBSERVATIONS || verdict.Fraction < TRIP_FRACTION)
            return null;

        reported = true;
        return verdict;
    }

    /// <summary>
    /// The buffer size to try next launch, or null where backing off is not the right answer.
    /// </summary>
    /// <param name="current">The buffer size currently configured, in frames.</param>
    /// <remarks>
    /// Null for two distinct cases, and the caller says something different about each. A
    /// <paramref name="current"/> of zero means the buffer was never ours to choose — SDL picked it,
    /// so it is already the driver's own preference and there is nothing for us to correct. At or past
    /// <see cref="MAX_AUTO_BACKOFF_FRAMES"/>, doubling has stopped being a plausible fix.
    /// </remarks>
    public static int? NextBufferSize(int current)
    {
        if (current <= 0 || current >= MAX_AUTO_BACKOFF_FRAMES)
            return null;

        return Math.Min(current * 2, MAX_AUTO_BACKOFF_FRAMES);
    }
}
