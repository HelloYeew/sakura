// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// How promptly the decoded thread comes back to a source, which is the difference between a seek
/// costing a millisecond of silence and costing several.
/// </summary>
[TestFixture]
public class AudioDecodeSchedulerTest
{
    private static readonly TimeSpan measure_over = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// A source that counts how often it is asked and never manages any work.
    /// </summary>
    /// <param name="wantsDecode">
    /// Whether it wants decoding. True, with a failing pump is the post-seek state: the ring is spoken
    /// for until the audio thread applies the discard, so there is work, and it cannot be done yet.
    /// </param>
    private sealed class StubSource(bool wantsDecode) : IDecodeSource
    {
        private int pumps;
        private int asked;

        /// <summary>
        /// Times <see cref="PumpDecode"/> was called once per scheduler round.
        /// </summary>
        public int Pumps => Volatile.Read(ref pumps);

        /// <summary>
        /// Times the scheduler looked at this source completely.
        /// </summary>
        public int Asked => Volatile.Read(ref asked);

        public bool WantsDecode
        {
            get
            {
                Interlocked.Increment(ref asked);
                return wantsDecode;
            }
        }

        public bool PumpDecode()
        {
            Interlocked.Increment(ref pumps);
            return false;
        }
    }

    /// <summary>
    /// Within the time the scheduler keeps asking a blocked source again at once.
    /// </summary>
    private static readonly TimeSpan prompt_window = TimeSpan.FromMilliseconds(80);

    private static StubSource run(bool wantsDecode) => run(wantsDecode, measure_over, TimeSpan.Zero, out _);

    /// <param name="wantsDecode">See <see cref="StubSource"/>.</param>
    /// <param name="duration">How long the scheduler runs with the source.</param>
    /// <param name="checkpoint">When to note the pumps so far, in <paramref name="pumpsAtCheckpoint"/>.</param>
    /// <param name="pumpsAtCheckpoint">The pumps by <paramref name="checkpoint"/>.</param>
    private static StubSource run(bool wantsDecode, TimeSpan duration, TimeSpan checkpoint, out int pumpsAtCheckpoint)
    {
        var source = new StubSource(wantsDecode);

        using var scheduler = new AudioDecodeScheduler();
        scheduler.Register(source);

        var elapsed = Stopwatch.StartNew();
        pumpsAtCheckpoint = -1;

        while (elapsed.Elapsed < duration)
        {
            if (pumpsAtCheckpoint < 0 && elapsed.Elapsed >= checkpoint)
                pumpsAtCheckpoint = source.Pumps;

            Thread.Sleep(1);
        }

        scheduler.Unregister(source);

        return source;
    }

    [Test]
    public void ComesBackPromptlyForASourceWaitingOnTheAudioThread()
    {
        // Above 20 rounds in 80 ms needs each 1 ms wait back within about 3 ms.
        TimerPrecision.RequireOneMillisecondWaitWithin(3);

        int pumps = run(true, prompt_window, TimeSpan.Zero, out _).Pumps;

        // One pump per round, so this is the round rate. The blocked wait is 1 ms, giving ~80 rounds
        // over the measured window; the idle wait of 25 ms would give ~4. Asserting well above 4 and
        // well below 80 leaves room for a loaded machine and for timer granularity, while still
        // failing outright if the blocked path ever reverts to idling.
        Assert.That(pumps, Is.GreaterThan(20),
            $"A source blocked on the audio thread was polled only {pumps} times in {prompt_window.TotalMilliseconds:F0} ms, "
            + "which is the idle rate. A seek's discard is applied at the top of the next device callback, 2.9 ms away "
            + "at the default buffer, so idling the full delay there is silence out of a voice that is already running.");
    }

    [Test]
    public void BacksOffFromASourceBlockedForLong()
    {
        var checkpoint = TimeSpan.FromMilliseconds(200);
        var duration = TimeSpan.FromMilliseconds(500);

        int pumps = run(true, duration, checkpoint, out int early).Pumps - early;

        // Past 100 ms blocked, the discard isn't coming from an output that's running (one stopped for the
        // background never applies it), so the source is asked at the idle rate: ~12 rounds in 300 ms, where
        // the blocked rate would make hundreds.
        Assert.That(pumps, Is.LessThan(40),
            $"A source blocked for over {checkpoint.TotalMilliseconds:F0} ms was still polled {pumps} times in the next "
            + $"{(duration - checkpoint).TotalMilliseconds:F0} ms: a stopped output would keep the thread waking every millisecond.");
    }

    [Test]
    public void DoesNotSpinOnASourceThatIsBlocked()
    {
        int pumps = run(true).Pumps;

        // The wait is short, not absent. Busy-polling would run into the tens of thousands, on a
        // thread that is deliberately above normal priority.
        Assert.That(pumps, Is.LessThan(2000),
            "The blocked path should wait, not spin: this thread outranks everything but the mixer.");
    }

    [Test]
    public void DoesNotDecodeForASourceThatWantsNothing()
    {
        var source = run(false);

        Assert.That(source.Pumps, Is.Zero, "A source that wants no decoding must never be pumped.");
        Assert.That(source.Asked, Is.GreaterThan(0), "The scheduler should still be looking at it each round.");
    }
}
