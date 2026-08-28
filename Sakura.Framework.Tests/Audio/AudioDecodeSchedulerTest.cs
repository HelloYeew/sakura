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

    private static StubSource run(bool wantsDecode)
    {
        var source = new StubSource(wantsDecode);

        using var scheduler = new AudioDecodeScheduler();
        scheduler.Register(source);

        var elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < measure_over)
            Thread.Sleep(1);

        scheduler.Unregister(source);

        return source;
    }

    [Test]
    public void ComesBackPromptlyForASourceWaitingOnTheAudioThread()
    {
        int pumps = run(true).Pumps;

        // One pump per round, so this is the round rate. The blocked wait is 1 ms, giving ~200 rounds
        // over the measured window; the idle wait of 5 ms would give ~40. Asserting well above 40 and
        // well below 200 leaves room for a loaded machine and for timer granularity, while still
        // failing outright if the blocked path ever reverts to idling.
        Assert.That(pumps, Is.GreaterThan(60),
            $"A source blocked on the audio thread was polled only {pumps} times in {measure_over.TotalMilliseconds:F0} ms, "
            + "which is the idle rate. A seek's discard is applied at the top of the next device callback, 2.9 ms away "
            + "at the default buffer, so idling the full delay there is silence out of a voice that is already running.");
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
