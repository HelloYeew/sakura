// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Timing;

/// <summary>
/// Tests for the clock-sharing model: drawables inherit their parent's clock by reference,
/// explicitly-assigned clocks are preserved and processed, and transforms / scheduler tasks
/// created before a drawable is added are rebased onto the parent timeline at add time.
/// </summary>
[TestFixture]
public class DrawableClockSharingTest
{
    private ManualClock manual = null!;
    private Container root = null!;

    [OneTimeSetUp]
    public void InitializeLogger()
    {
        Logger.Initialize();
    }

    [OneTimeTearDown]
    public void ShutdownLogger()
    {
        Logger.Shutdown();
    }

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };

        root.Load();
        root.LoadComplete();

        // settle one frame so the root clock has processed its source.
        advanceTo(1000);
    }

    private void advanceTo(double time)
    {
        manual.CurrentTime = time;
        root.UpdateSubTree();
    }

    [Test]
    public void TestChildInheritsParentClockReference()
    {
        var box = new Box { Size = new Vector2(10) };
        root.Add(box);

        Assert.That(box.Clock, Is.SameAs(root.Clock));
    }

    [Test]
    public void TestClockSharedThroughNestedContainers()
    {
        var inner = new Container { Size = new Vector2(100) };
        var middle = new Container { Size = new Vector2(200) };
        var box = new Box { Size = new Vector2(10) };

        inner.Add(box);
        middle.Add(inner);
        root.Add(middle);

        Assert.Multiple(() =>
        {
            Assert.That(middle.Clock, Is.SameAs(root.Clock));
            Assert.That(inner.Clock, Is.SameAs(root.Clock));
            Assert.That(box.Clock, Is.SameAs(root.Clock));
        });

        advanceTo(1500);

        Assert.That(box.Clock.CurrentTime, Is.EqualTo(1500).Within(0.001));
    }

    [Test]
    public void TestCustomClockPreservedOnAddAndProcessed()
    {
        var childManual = new ManualClock { CurrentTime = 0 };
        var custom = new FramedClock(childManual);

        var box = new Box { Size = new Vector2(10) };
        box.Clock = custom;
        root.Add(box);

        Assert.That(box.Clock, Is.SameAs(custom), "An explicitly-assigned clock must survive being added to a container.");

        // Advance only the parent's timeline: the custom clock must not move.
        advanceTo(2000);
        Assert.That(box.Clock.CurrentTime, Is.EqualTo(0).Within(0.001));

        // Advance the custom clock's source: the drawable must process it during UpdateSubTree.
        childManual.CurrentTime = 250;
        advanceTo(2100);

        Assert.Multiple(() =>
        {
            Assert.That(box.Clock.CurrentTime, Is.EqualTo(250).Within(0.001));
            Assert.That(root.Clock.CurrentTime, Is.EqualTo(2100).Within(0.001));
        });
    }

    [Test]
    public void TestClockReassignmentPropagatesToDescendants()
    {
        var middle = new Container { Size = new Vector2(200) };
        var box = new Box { Size = new Vector2(10) };
        middle.Add(box);
        root.Add(middle);

        var newManual = new ManualClock { CurrentTime = 5000 };
        root.Clock = new FramedClock(newManual);

        Assert.Multiple(() =>
        {
            Assert.That(middle.Clock, Is.SameAs(root.Clock));
            Assert.That(box.Clock, Is.SameAs(root.Clock));
        });
    }

    [Test]
    public void TestTransformScheduledBeforeAddStartsAtAddTime()
    {
        var box = new Box { Size = new Vector2(10) };

        // Scheduled on the drawable's own (standalone) timeline, before it has a parent.
        box.FadeOut(100);

        root.Add(box);

        // Halfway through the fade on the parent timeline.
        advanceTo(1050);
        Assert.That(box.Alpha, Is.EqualTo(0.5f).Within(0.01f), "A transform scheduled before Add should begin at the time the drawable was added.");

        advanceTo(1150);
        Assert.That(box.Alpha, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void TestTransformAfterAddCompletesOnSharedTimeline()
    {
        var box = new Box { Size = new Vector2(10) };
        root.Add(box);

        box.MoveTo(new Vector2(100, 50), 100);

        advanceTo(1200);

        Assert.Multiple(() =>
        {
            Assert.That(box.Position.X, Is.EqualTo(100).Within(0.01f));
            Assert.That(box.Position.Y, Is.EqualTo(50).Within(0.01f));
        });
    }

    [Test]
    public void TestScheduledTaskBeforeAddRunsAfterAdd()
    {
        bool ran = false;

        var box = new Box { Size = new Vector2(10) };
        box.Schedule(() => ran = true);

        root.Add(box);
        advanceTo(1001);

        Assert.That(ran, Is.True, "A task scheduled before Add should execute once the drawable is updated in the tree.");
    }

    [Test]
    public void TestDelayedTaskHonorsDelayOnInheritedClock()
    {
        var box = new Box { Size = new Vector2(10) };
        root.Add(box);

        bool ran = false;
        box.Scheduler.AddDelayed(() => ran = true, 100);

        advanceTo(1050);
        Assert.That(ran, Is.False, "A delayed task must not run before its delay has elapsed.");

        advanceTo(1101);
        Assert.That(ran, Is.True, "A delayed task must run once its delay has elapsed on the inherited clock.");
    }

    [Test]
    public void TestExpireRemovesDrawableWhenTransformsComplete()
    {
        var box = new Box { Size = new Vector2(10) };
        root.Add(box);

        box.FadeOut(100);
        box.Expire();

        advanceTo(1050);
        Assert.That(root.Contains(box), Is.True, "An expiring drawable must survive until its transforms complete.");

        advanceTo(1150);
        Assert.That(root.Contains(box), Is.False, "An expired drawable must be removed once its lifetime ends.");
    }
}
