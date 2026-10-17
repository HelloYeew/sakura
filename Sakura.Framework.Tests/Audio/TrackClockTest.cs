// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.Headless;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// Which of the channel's two speed controls <see cref="TrackClock.Rate"/> drives.
/// </summary>
[TestFixture]
public class TrackClockTest
{
    [Test]
    public void RateKeepsThePitchByDefault()
    {
        var channel = new HeadlessAudioChannel(10_000);
        var clock = new TrackClock(channel) { Rate = 1.5 };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clock.PreservePitch, Is.True);
            Assert.That(channel.Tempo.Value, Is.EqualTo(1.5));
            Assert.That(channel.Frequency.Value, Is.EqualTo(1));
        }
    }

    [Test]
    public void WithoutPreservePitchRateMovesThePitch()
    {
        var channel = new HeadlessAudioChannel(10_000);
        var clock = new TrackClock(channel) { PreservePitch = false, Rate = 0.5 };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel.Frequency.Value, Is.EqualTo(0.5));
            Assert.That(channel.Tempo.Value, Is.EqualTo(1));
            Assert.That(clock.Rate, Is.EqualTo(0.5));
        }
    }

    [Test]
    public void SwitchingCarriesTheRateAcross()
    {
        var channel = new HeadlessAudioChannel(10_000);
        var clock = new TrackClock(channel) { Rate = 0.75 };

        clock.PreservePitch = false;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel.Tempo.Value, Is.EqualTo(1));
            Assert.That(channel.Frequency.Value, Is.EqualTo(0.75));
            Assert.That(clock.Rate, Is.EqualTo(0.75), "Switching changes how the speed is produced, not the speed.");
        }
    }

    [Test]
    public void RateReportsTheSpeedTheAudioActuallyMovesAt()
    {
        var channel = new HeadlessAudioChannel(10_000);
        var clock = new TrackClock(channel) { Rate = 0.5 };

        // Someone else's pitch effect, on the property the clock doesn't own.
        channel.Frequency.Value = 2;

        Assert.That(clock.Rate, Is.EqualTo(1), "The clock chain interpolates with this, so it has to be Frequency × Tempo.");
    }

    [Test]
    public void HeadlessPositionFollowsBothFrequencyAndTempo()
    {
        var channel = new HeadlessAudioChannel(10_000);
        channel.Frequency.Value = 2;
        channel.Tempo.Value = 0.25;
        channel.Play();

        channel.Update(1000);

        Assert.That(channel.CurrentTime, Is.EqualTo(500));
    }
}
