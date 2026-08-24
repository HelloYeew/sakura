// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Measures how long the output device spent with nothing to play, from a thread that may itself have
/// been stopped for the whole of it.
/// </summary>
internal sealed class DeviceStarvationTracker
{
    private long starvedMicroseconds;
    private long longestGapMicroseconds;

    /// <summary>
    /// Total time the device has spent with an empty queue while audio was playing.
    /// </summary>
    public double StarvedMilliseconds => Interlocked.Read(ref starvedMicroseconds) / 1000.0;

    /// <summary>
    /// The longest the mix loop went between iterations, whether it starved the device.
    /// </summary>
    /// <remarks>
    /// Published because it is the direct answer to "was the mix thread stopped, and for how long",
    /// which is the first thing worth knowing about a dropout and is otherwise only inferable. A large
    /// gap with no starvation means the queue did its job; starvation with a small gap means something
    /// else is wrong, and the two need telling apart.
    /// </remarks>
    public double LongestGapMilliseconds => Interlocked.Read(ref longestGapMicroseconds) / 1000.0;

    /// <summary>
    /// Records one interval of the mix loop.
    /// </summary>
    /// <param name="gapMs">Wall-clock length of the interval, top of one iteration to the top of the next.</param>
    /// <param name="playableMs">
    /// How much audio the device had available across that interval: what was queued when it began plus
    /// anything pushed into it before it ended. The most it could have played before running out.
    /// </param>
    /// <param name="anythingPlaying">
    /// Whether audio was expected. An empty queue with nothing playing is an idle device, not a
    /// dropout.
    /// </param>
    public void Observe(double gapMs, double playableMs, bool anythingPlaying)
    {
        long gapMicroseconds = (long)(gapMs * 1000.0);

        if (gapMicroseconds > Interlocked.Read(ref longestGapMicroseconds))
            Interlocked.Exchange(ref longestGapMicroseconds, gapMicroseconds);

        if (!anythingPlaying)
            return;

        double dry = gapMs - Math.Max(0, playableMs);

        if (dry <= 0)
            return;

        Interlocked.Add(ref starvedMicroseconds, (long)(dry * 1000.0));
    }

    /// <summary>
    /// The starvation expressed as a count of missed periods of <paramref name="periodMs"/>.
    /// </summary>
    /// <remarks>
    /// So that the managed mixer reports an underrun count in the same shape the native engine
    /// does, rather than the two statistics sharing a name and meaning different things. Neither count
    /// is comparable to the other across engines since the native one is per voice per mix block, this one
    /// is per block of missing output which is why <see cref="StarvedMilliseconds"/> is
    /// published alongside it and is the figure to read.
    /// </remarks>
    public long CountIn(double periodMs) =>
        periodMs <= 0 ? 0 : (long)(StarvedMilliseconds / periodMs);
}
