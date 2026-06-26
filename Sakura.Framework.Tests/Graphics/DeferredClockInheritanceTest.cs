// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Regression tests for the construction-time clock-inheritance deferral
/// after decrease allocation
/// </summary>
[TestFixture]
public class DeferredClockInheritanceTest
{
    private ManualClock manual = null!;
    private Container root = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

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

        advanceTo(1000);
    }

    private void advanceTo(double time)
    {
        manual.CurrentTime = time;
        root.UpdateSubTree();
    }

    [Test]
    public void TestNestedConstructionInheritsClockOnAttach()
    {
        // The whole tree is built before being attached anywhere, so every Add happens on an
        // unloaded container and defers clock inheritance. Attaching the outermost to the loaded
        // root must inherit the clock all the way down.
        var inner = new Container { Size = new Vector2(100) };
        var middle = new Container { Size = new Vector2(200) };
        var box = new Box { Size = new Vector2(10) };

        inner.Add(box);
        middle.Add(inner);

        // nothing in the detached tree is loaded yet.
        Assert.Multiple(() =>
        {
            Assert.That(middle.IsLoaded, Is.False);
            Assert.That(inner.IsLoaded, Is.False);
            Assert.That(box.IsLoaded, Is.False);
        });

        root.Add(middle);

        Assert.Multiple(() =>
        {
            Assert.That(middle.Clock, Is.SameAs(root.Clock));
            Assert.That(inner.Clock, Is.SameAs(root.Clock));
            Assert.That(box.Clock, Is.SameAs(root.Clock), "Deferred inheritance must reach every descendant on attach.");
        });

        advanceTo(1500);
        Assert.That(box.Clock.CurrentTime, Is.EqualTo(1500).Within(0.001), "The shared clock must drive the deepest child.");
    }

    [Test]
    public void TestChildrenInitializerInheritsClockOnAttach()
    {
        // Same as above but using the Children collection initializer (the common construction
        // pattern and the one exercised by the benchmark).
        Container middle = null!;
        Box box = null!;

        var outer = new Container
        {
            Size = new Vector2(300),
            Children = new Drawable[]
            {
                middle = new Container
                {
                    Size = new Vector2(200),
                    Children = new Drawable[]
                    {
                        box = new Box { Size = new Vector2(10) }
                    }
                }
            }
        };

        root.Add(outer);

        Assert.Multiple(() =>
        {
            Assert.That(outer.Clock, Is.SameAs(root.Clock));
            Assert.That(middle.Clock, Is.SameAs(root.Clock));
            Assert.That(box.Clock, Is.SameAs(root.Clock));
        });
    }

    [Test]
    public void TestDirectAddToLoadedRootInheritsImmediately()
    {
        // adding directly to a loaded container inherits at once.
        var box = new Box { Size = new Vector2(10) };
        root.Add(box);

        Assert.That(box.Clock, Is.SameAs(root.Clock));
    }

    [Test]
    public void TestCustomClockSurvivesDeferredConstruction()
    {
        var childManual = new ManualClock { CurrentTime = 0 };
        var custom = new FramedClock(childManual);

        var box = new Box { Size = new Vector2(10) };
        box.Clock = custom; // explicit clock => hasCustomClock

        var holder = new Container { Size = new Vector2(200) };
        holder.Add(box); // deferred (holder unloaded)
        root.Add(holder);

        Assert.That(box.Clock, Is.SameAs(custom), "An explicitly-assigned clock must survive deferred construction and attach.");

        // The custom clock is independent of the parent timeline.
        advanceTo(2000);
        Assert.That(box.Clock.CurrentTime, Is.EqualTo(0).Within(0.001));

        childManual.CurrentTime = 250;
        advanceTo(2100);
        Assert.That(box.Clock.CurrentTime, Is.EqualTo(250).Within(0.001), "The custom clock must still be processed each frame.");
    }

    [Test]
    public void TestTransformScheduledDuringConstructionRebasesOnAttach()
    {
        // A transform queued on a child while the tree is still detached must begin at attach time
        // on the shared timeline — the deferral must not break transform rebasing.
        var box = new Box { Size = new Vector2(10) };
        var holder = new Container { Size = new Vector2(200) };
        holder.Add(box); // deferred

        box.FadeOut(100); // scheduled before the tree is attached to the loaded root

        root.Add(holder); // box now inherits root.Clock and the fade rebases to attach time

        // Halfway through the fade on the shared timeline.
        advanceTo(1050);
        Assert.That(box.Alpha, Is.EqualTo(0.5f).Within(0.05f),
            "A transform queued during detached construction must begin at attach time.");

        advanceTo(1150);
        Assert.That(box.Alpha, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void TestScheduledTaskDuringConstructionRunsAfterAttach()
    {
        bool ran = false;

        var box = new Box { Size = new Vector2(10) };
        var holder = new Container { Size = new Vector2(200) };
        holder.Add(box); // deferred

        box.Schedule(() => ran = true);

        root.Add(holder);
        advanceTo(1001);

        Assert.That(ran, Is.True, "A task scheduled during detached construction must run once the tree is attached and updated.");
    }

    [Test]
    public void TestClockReassignmentReachesDeferredSubtree()
    {
        var middle = new Container { Size = new Vector2(200) };
        var box = new Box { Size = new Vector2(10) };
        middle.Add(box); // deferred
        root.Add(middle);

        var newManual = new ManualClock { CurrentTime = 5000 };
        root.Clock = new FramedClock(newManual);

        Assert.Multiple(() =>
        {
            Assert.That(middle.Clock, Is.SameAs(root.Clock));
            Assert.That(box.Clock, Is.SameAs(root.Clock), "Reassigning the root clock must propagate to a formerly-deferred subtree.");
        });
    }

    [Test]
    public void TestContainerLoadedBeforeAddingChildInheritsImmediately()
    {
        // A container that is already loaded (because it was attached first) takes the immediate
        // inheritance path when children are added afterwards.
        var holder = new Container { Size = new Vector2(200) };
        root.Add(holder); // holder is now loaded
        Assert.That(holder.IsLoaded, Is.True);

        var box = new Box { Size = new Vector2(10) };
        holder.Add(box); // immediate path

        Assert.That(box.Clock, Is.SameAs(root.Clock));
    }
}
