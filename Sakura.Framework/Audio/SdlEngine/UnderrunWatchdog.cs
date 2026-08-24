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
/// <para>
/// Sakura set the default device buffer really aggressive (see <see cref="SDLAudioManager.DEFAULT_DEVICE_BUFFER_FRAMES"/> that's the default value)
/// because measurement says the callback has an order of magnitude of headroom at that size and because a driver that cannot honor the
/// request rounds it up rather than failing. What measurement cannot rule out is scheduling jitter on
/// a machine nobody has tested, and the symptom of getting that wrong is crackling that a user has no
/// way to connect to a setting they have never heard of.
/// </para>
/// <para>
/// So the default is not a promise, it is a starting point that retreats under evidence. This type is
/// the evidence half: it watches the underrun counter and reports the one transition worth acting on.
/// Separated from the manager because the interesting cases — a burst that should be ignored, a
/// sustained failure that should not, the guarantee of acting only once — are all about counting and
/// timing, and none of them need an audio device to test.
/// </para>
/// </remarks>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class UnderrunWatchdog
{
    /// <summary>
    /// Window over which underruns are counted, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Five seconds since long enough that one stall on startup, during a device change, or from a hitch
    /// elsewhere in the app cannot trip it, and short enough that a listener has not been putting up
    /// with crackling for long before something says so.
    /// </remarks>
    public const double CHECK_INTERVAL_MS = 5000;

    /// <summary>
    /// How many underruns inside <see cref="CHECK_INTERVAL_MS"/> mean the buffer is genuinely too
    /// small, rather than the machine having had a bad moment.
    /// </summary>
    public const long THRESHOLD = 10;

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

    private long underrunsAtLastCheck;
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
    /// Feeds the watchdog the running underrun total.
    /// </summary>
    /// <param name="totalUnderruns">Underruns since the device was opened.</param>
    /// <param name="frameTime">Elapsed time since the last call, in milliseconds.</param>
    /// <returns>
    /// The number of underruns in the window that tripped it, or null — which is the answer almost
    /// every frame, and the answer forever after it has fired once.
    /// </returns>
    public long? Poll(long totalUnderruns, double frameTime)
    {
        if (reported)
            return null;

        sinceCheckMs += frameTime;

        if (sinceCheckMs < CHECK_INTERVAL_MS)
            return null;

        long since = totalUnderruns - underrunsAtLastCheck;

        underrunsAtLastCheck = totalUnderruns;
        sinceCheckMs = 0;

        if (since < THRESHOLD)
            return null;

        reported = true;
        return since;
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
