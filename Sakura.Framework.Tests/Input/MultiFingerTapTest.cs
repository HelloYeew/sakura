// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Input;

[TestFixture]
public class MultiFingerTapTest
{
    private MultiFingerTap tap = null!;

    [SetUp]
    public void SetUp() => tap = new MultiFingerTap();

    private static TouchEvent finger(int index, float x = 0, float y = 0, bool cancelled = false)
        => new TouchEvent((TouchSource)index, new Vector2(x == 0 ? 100 + index * 50 : x, y == 0 ? 300 : y), cancelled: cancelled);

    private void down(int fingers, double time = 0, bool taken = false)
    {
        for (int i = 0; i < fingers; i++)
            tap.TouchDown(finger(i), taken, time + i * 10);
    }

    /// <returns>What the last finger's lift reported.</returns>
    private int up(int fingers, double time, out Vector2 centre)
    {
        centre = Vector2.Zero;
        int result = 0;

        for (int i = 0; i < fingers; i++)
            result = tap.TouchUp(finger(i), time + i * 10, out centre);

        return result;
    }

    [Test]
    public void FingersTappingTogetherAreCountedWhenTheLastLifts()
    {
        down(4);

        Assert.That(tap.TouchUp(finger(0), 150, out _), Is.Zero, "counted before the last finger lifted");
        Assert.That(tap.TouchUp(finger(1), 160, out _), Is.Zero);
        Assert.That(tap.TouchUp(finger(2), 170, out _), Is.Zero);
        Assert.That(tap.TouchUp(finger(3), 180, out var centre), Is.EqualTo(4));
        Assert.That(centre, Is.EqualTo(new Vector2(175, 300)));
    }

    [Test]
    public void SlowTapIsNotOne()
    {
        down(4);

        Assert.That(up(4, MultiFingerTap.MAX_MILLISECONDS, out _), Is.Zero);
    }

    [Test]
    public void FingerThatMovesSpoilsIt()
    {
        down(4);
        tap.TouchMove(new TouchEvent(TouchSource.Touch2, new Vector2(200, 300 + TouchMouseEmulation.SLOP + 1)));

        Assert.That(up(4, 150, out _), Is.Zero);
    }

    [Test]
    public void TakenFingerSpoilsIt()
    {
        down(3);
        tap.TouchDown(finger(3), true, 40);

        Assert.That(up(4, 150, out _), Is.Zero);
    }

    [Test]
    public void CancelledFingerSpoilsIt()
    {
        down(4);
        tap.TouchUp(finger(0, cancelled: true), 150, out _);

        Assert.That(up(4, 160, out _), Is.Zero);
    }

    [Test]
    public void NextTapStartsAfresh()
    {
        down(4);
        tap.TouchMove(new TouchEvent(TouchSource.Touch1, new Vector2(500, 500)));
        up(4, 150, out _);

        down(4, time: 1000);

        Assert.That(up(4, 1150, out _), Is.EqualTo(4));
    }
}
