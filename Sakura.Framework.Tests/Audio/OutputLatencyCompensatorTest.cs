// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// The maths that turns a mix cursor into an audible position, on its own.
/// </summary>
/// <remarks>
/// Tested apart from either channel because both use it and because the interesting cases — a rate
/// change mid-buffer, a missed reset — are awkward to provoke through a real voice and trivial to
/// state here.
/// </remarks>
[TestFixture]
public class OutputLatencyCompensatorTest
{
    [Test]
    public void SubtractsTheQueueDepth()
    {
        var compensator = new OutputLatencyCompensator();

        Assert.That(compensator.Compensate(100, 20, 1.0), Is.EqualTo(80).Within(1e-9),
            "A position 100 ms into the source with 20 ms still queued is 80 ms of audible music.");
    }

    [Test]
    public void ReportsZeroRatherThanNegativeBeforeAnythingIsAudible()
    {
        var compensator = new OutputLatencyCompensator();

        Assert.That(compensator.Compensate(5, 20, 1.0), Is.Zero,
            "For the first buffer of a track nothing has reached the listener, and the position for that is zero, not -15.");
    }

    [Test]
    public void ScalesTheSubtractionByPlaybackRate()
    {
        var compensator = new OutputLatencyCompensator();

        Assert.That(compensator.Compensate(1000, 20, 2.0), Is.EqualTo(960).Within(1e-9),
            "At double speed a 20 ms buffer of output was made from 40 ms of the song.");

        compensator.Reset();

        Assert.That(compensator.Compensate(1000, 20, 0.5), Is.EqualTo(990).Within(1e-9),
            "And at half speed, from 10 ms of it.");
    }

    [Test]
    public void AdvancesAsTheQueueDrainsEvenWhileTheCursorIsStill()
    {
        var compensator = new OutputLatencyCompensator();

        // What a paused-but-still-draining device looks like: the mixer has stopped producing, but
        // what it already produced is still being heard.
        Assert.That(compensator.Compensate(100, 20, 1.0), Is.EqualTo(80).Within(1e-9));
        Assert.That(compensator.Compensate(100, 10, 1.0), Is.EqualTo(90).Within(1e-9));
        Assert.That(compensator.Compensate(100, 0, 1.0), Is.EqualTo(100).Within(1e-9));
    }

    [Test]
    public void HoldsPositionRatherThanSteppingBackwards()
    {
        var compensator = new OutputLatencyCompensator();

        double before = compensator.Compensate(1000, 20, 1.0);

        // A rate change applies to the whole queue at once, even though the queued audio was mixed at
        // the old rate. Uncorrected that reads as the song jumping backwards by most of a buffer.
        double after = compensator.Compensate(1000, 20, 2.0);

        Assert.That(after, Is.EqualTo(before),
            "A clock fed a position that moves backwards snaps instead of interpolating.");
    }

    [Test]
    public void ResumesAdvancingOnceTheRealPositionCatchesUp()
    {
        var compensator = new OutputLatencyCompensator();

        compensator.Compensate(1000, 20, 1.0);
        compensator.Compensate(1000, 20, 2.0);

        Assert.That(compensator.Compensate(1060, 20, 2.0), Is.EqualTo(1020).Within(1e-9),
            "The hold is a floor, not a freeze: once the source has moved past it the real value is reported again.");
    }

    [Test]
    public void ResetLetsThePositionJumpBackwards()
    {
        var compensator = new OutputLatencyCompensator();

        compensator.Compensate(50_000, 20, 1.0);
        compensator.Reset();

        Assert.That(compensator.Compensate(1000, 20, 1.0), Is.EqualTo(980).Within(1e-9),
            "A seek is the position genuinely moving, and Reset is how a caller says so.");
    }

    [Test]
    public void RecoversWithinOneBufferFromAMissedReset()
    {
        var compensator = new OutputLatencyCompensator();

        compensator.Compensate(50_000, 20, 1.0);

        // Deliberately no Reset: this is the bug being guarded against, not the supported path.
        Assert.That(compensator.Compensate(1000, 20, 1.0), Is.EqualTo(980).Within(1e-9),
            "A backwards jump larger than the window that could have produced it is a real reposition, "
            + "so a forgotten Reset costs a glitch rather than freezing the position for the rest of the track.");
    }
}
