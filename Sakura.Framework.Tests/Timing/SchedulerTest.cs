// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Timing;

/// <summary>
/// Tests for <see cref="Scheduler"/> ordering, repetition, cancellation and re-entrancy
/// (for behavior check and sanity check after implement the binary-insertion / range-removal rework).
/// </summary>
[TestFixture]
public class SchedulerTest
{
    private ManualClock clock = null!;
    private Scheduler scheduler = null!;

    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock
        {
            CurrentTime = 0
        };
        scheduler = new Scheduler(clock);
    }

    private void advanceTo(double time)
    {
        clock.CurrentTime = time;
        scheduler.Update();
    }

    [Test]
    public void TestTasksRunInExecutionTimeOrder()
    {
        var order = new List<int>();

        scheduler.AddDelayed(() => order.Add(3), 300);
        scheduler.AddDelayed(() => order.Add(1), 100);
        scheduler.AddDelayed(() => order.Add(2), 200);

        advanceTo(400);

        Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void TestTaskDoesNotRunBeforeDelay()
    {
        bool ran = false;
        scheduler.AddDelayed(() => ran = true, 100);

        advanceTo(99);
        Assert.That(ran, Is.False);

        advanceTo(100);
        Assert.That(ran, Is.True);
    }

    [Test]
    public void TestRepeatingTaskRefires()
    {
        int count = 0;
        scheduler.AddRepeating(() => count++, 0, 100);

        advanceTo(0);
        Assert.That(count, Is.EqualTo(1));

        advanceTo(100);
        Assert.That(count, Is.EqualTo(2));

        advanceTo(350);
        // Repeats are scheduled relative to their previous execution time.
        Assert.That(count, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void TestCancelPreventsExecution()
    {
        bool ran = false;
        var task = scheduler.AddDelayed(() => ran = true, 100);

        // Cancel both before and after the pending list is merged.
        scheduler.Update();
        scheduler.Cancel(task);

        advanceTo(200);
        Assert.That(ran, Is.False);
    }

    [Test]
    public void TestTaskAddedDuringExecutionDoesNotThrow()
    {
        bool nestedRan = false;

        scheduler.Add(() => scheduler.Add(() => nestedRan = true));

        Assert.DoesNotThrow(() => advanceTo(1));

        // The nested task runs on the following update.
        advanceTo(2);
        Assert.That(nestedRan, Is.True);
    }

    [Test]
    public void TestTaskCancellingAnotherDuringExecutionDoesNotThrow()
    {
        bool secondRan = false;
        ScheduledTask second = null!;

        scheduler.AddDelayed(() => scheduler.Cancel(second), 10);
        second = scheduler.AddDelayed(() => secondRan = true, 500);

        Assert.DoesNotThrow(() => advanceTo(20));

        advanceTo(600);
        Assert.That(secondRan, Is.False, "A task cancelled by an earlier task must not run.");
    }
}
