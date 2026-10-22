// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Timing;

[TestFixture]
public class TapCalibrationTest
{
    private const double interval = 600;

    // A steady hand: a few milliseconds either way of where the player means to tap.
    private static readonly double[] jitter = [-12, 7, -3, 15, -9, 4, 11, -14, 2, -6, 9, -1];

    /// <summary>
    /// Taps <paramref name="count"/> beats in a row, starting from <paramref name="firstBeat"/>, each
    /// <paramref name="late"/> after its beat give or take <see cref="jitter"/>.
    /// </summary>
    private static TapCalibration tap(double late, int count = 24, int firstBeat = 3, int warmUp = 0)
    {
        var calibration = new TapCalibration(interval) { WarmUpTaps = warmUp };

        for (int i = 0; i < count; i++)
            calibration.Add((firstBeat + i) * interval + late + jitter[i % jitter.Length]);

        return calibration;
    }

    private static double jitterMean(int count)
    {
        double sum = 0;

        for (int i = 0; i < count; i++)
            sum += jitter[i % jitter.Length];

        return sum / count;
    }

    [TestCase(0)]
    [TestCase(35)]
    [TestCase(-40)]
    public void TestOffsetIsWhereTheTapsLand(double late)
    {
        var calibration = tap(late);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(calibration.Offset, Is.EqualTo(late + jitterMean(24)).Within(0.001));
            Assert.That(calibration.Counted, Is.EqualTo(24));
            Assert.That(calibration.Spread, Is.InRange(5, 15));
        }
    }

    /// <summary>
    /// Bluetooth headphones on an output that can't say how late it is: taps close to half a beat late, some landing
    /// just past it, where they're nearest the next beat instead.
    /// </summary>
    [TestCase(290)]
    [TestCase(-295)]
    public void TestADelayNearHalfABeatHoldsTogether(double late)
    {
        var calibration = tap(late);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(calibration.Offset, Is.EqualTo(late + jitterMean(24)).Within(0.001));
            Assert.That(calibration.Counted, Is.EqualTo(24));
        }
    }

    [Test]
    public void TestTheWarmUpDoesntCount()
    {
        var calibration = new TapCalibration(interval);

        // Finding the beat: all over the place.
        foreach (double wild in new[] { 3 * interval + 200, 4 * interval - 150, 5 * interval + 90, 6 * interval - 60 })
            calibration.Add(wild);

        Assert.That(calibration.Offset, Is.Null);

        for (int i = 0; i < 10; i++)
            calibration.Add((7 + i) * interval + 20);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(calibration.Counted, Is.EqualTo(10));
            Assert.That(calibration.Offset, Is.EqualTo(20).Within(0.001));
            Assert.That(calibration.Spread, Is.EqualTo(0).Within(0.001));
        }
    }

    [Test]
    public void TestStrayTapsDontCount()
    {
        var calibration = new TapCalibration(interval) { WarmUpTaps = 0 };

        for (int i = 0; i < 20; i++)
            calibration.Add(i * interval + 25);

        // A doubled tap, and one on a beat that was missed and caught up.
        calibration.Add(5 * interval + 230);
        calibration.Add(9 * interval - 200);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(calibration.Counted, Is.EqualTo(20));
            Assert.That(calibration.Offset, Is.EqualTo(25).Within(0.001));
        }
    }

    [Test]
    public void TestBeatsAreCountedFromTheFirstBeat()
    {
        var calibration = new TapCalibration(interval, firstBeat: 1234) { WarmUpTaps = 0 };

        calibration.Add(1234 + 10);
        calibration.Add(1234 - 3 * interval + 10);
        calibration.Add(1234 + 40 * interval + 10);

        Assert.That(calibration.Offset, Is.EqualTo(10).Within(0.001));
    }

    [Test]
    public void TestNothingToSayBeforeTaps()
    {
        var calibration = new TapCalibration(interval) { WarmUpTaps = 0 };

        Assert.That(calibration.Offset, Is.Null);

        calibration.Add(interval + 12);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(calibration.Offset, Is.EqualTo(12).Within(0.001));
            Assert.That(calibration.Spread, Is.Null, "one tap has no spread");
        }
    }

    /// <summary>
    /// Touch is in both runs, so it drops out: what's left is how much later the music is heard than the screen seen.
    /// </summary>
    [Test]
    public void TestSoundAfterPicture()
    {
        const double touch = 30;
        const double sound = 15;
        const double picture = 40;

        Assert.That(TapCalibration.SoundAfterPicture(sound + touch, picture + touch), Is.EqualTo(sound - picture));
    }

    [TestCase(0)]
    [TestCase(-100)]
    public void TestABeatNeedsAnInterval(double beatInterval)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TapCalibration(beatInterval));
    }
}
