// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Timing;

[TestFixture]
public class FramedClockTest
{
    private TestAdjustableClock source = null!;
    private FramedClock framed = null!;

    [SetUp]
    public void SetUp()
    {
        source = new TestAdjustableClock();
        framed = new FramedClock(source);
    }

    [Test]
    public void TestTimeDoesNotMoveUntilProcessFrame()
    {
        source.Start();
        double timeBefore = framed.CurrentTime;
        source.AdvanceBy(100);
        Assert.That(framed.CurrentTime, Is.EqualTo(timeBefore), "Time must not change until ProcessFrame is called");
    }

    [Test]
    public void TestProcessFrameAdvancesTime()
    {
        source.Start();
        source.AdvanceBy(100);
        framed.ProcessFrame();
        Assert.That(framed.CurrentTime, Is.EqualTo(100).Within(1));
    }

    [Test]
    public void TestElapsedFrameTimeReflectsSourceDelta()
    {
        source.Start();
        source.AdvanceBy(16);
        framed.ProcessFrame();
        Assert.That(framed.ElapsedFrameTime, Is.EqualTo(16).Within(1));
    }

    [Test]
    public void TestElapsedFrameTimeIsZeroWhenStopped()
    {
        source.Start();
        source.AdvanceBy(50);
        framed.Stop();
        framed.ProcessFrame();
        Assert.That(framed.ElapsedFrameTime, Is.EqualTo(0));
    }

    [Test]
    public void TestRateScalesElapsedTime()
    {
        source.Start();
        framed.Rate = 2.0;
        source.AdvanceBy(50);
        framed.ProcessFrame();
        Assert.That(framed.CurrentTime, Is.EqualTo(100).Within(1));
    }

    [Test]
    public void TestStartFromZeroIgnoresSourceCurrentTime()
    {
        source.AdvanceBy(5000);
        var zeroed = new FramedClock(source, startFromZero: true);
        Assert.That(zeroed.CurrentTime, Is.EqualTo(0).Within(1));
    }

    [Test]
    public void TestChangeSourceUpdatesToNewSource()
    {
        source.Start();
        source.AdvanceBy(200);
        framed.ProcessFrame();
        Assert.That(framed.CurrentTime, Is.EqualTo(200).Within(1));

        var secondSource = new TestAdjustableClock();
        secondSource.AdvanceBy(500);
        framed.ChangeSource(secondSource);
        secondSource.AdvanceBy(100);
        framed.ProcessFrame();

        // After source change, elapsed is measured from the new source's position at change time.
        Assert.That(framed.ElapsedFrameTime, Is.EqualTo(100).Within(1));
    }
}
