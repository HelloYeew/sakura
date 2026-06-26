// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

[TestFixture]
public class ContainerAutoSizeEasingTest
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
    }

    private void frame(double advanceMs = 16)
    {
        manual.CurrentTime += advanceMs;
        root.UpdateSubTree();
    }

    private void settle()
    {
        for (int i = 0; i < 3; i++)
            frame();
    }

    /// <summary>
    /// Replacement of NUnit's <c>.Within()</c> with Sakura's Vector2
    /// </summary>
    private static void assertSize(Vector2 actual, float x, float y, string? message = null)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(x).Within(0.01f), message);
            Assert.That(actual.Y, Is.EqualTo(y).Within(0.01f), message);
        });
    }

    [Test]
    public void TestDefaultsAreUnchanged()
    {
        var panel = new Container { AutoSizeAxes = Axes.Both };

        Assert.Multiple(() =>
        {
            Assert.That(panel.AutoSizeDuration, Is.EqualTo(0), "AutoSizeDuration must default to 0 (instant).");
            Assert.That(panel.AutoSizeEasing, Is.EqualTo(Easing.None), "AutoSizeEasing must default to None.");
        });
    }

    [Test]
    public void TestZeroDurationSnapsInstantly()
    {
        var panel = new Container { AutoSizeAxes = Axes.Both };
        var box = new Box { Size = new Vector2(50, 40) };
        panel.Add(box);
        root.Add(panel);
        settle();

        assertSize(panel.Size, 50, 40);

        box.Size = new Vector2(120, 90);
        frame();
        frame();

        assertSize(panel.Size, 120, 90, "A zero-duration auto-size must snap to the new size.");
    }

    [Test]
    public void TestAnimatedAutoSizeIsGradualThenSettlesExactly()
    {
        var panel = new Container
        {
            AutoSizeAxes = Axes.Both,
            AutoSizeDuration = 200,
            AutoSizeEasing = Easing.None
        };
        var box = new Box
        {
            Size = new Vector2(100, 100)
        };
        panel.Add(box);
        root.Add(panel);
        settle();

        manual.CurrentTime += 400;
        root.UpdateSubTree();
        root.UpdateSubTree();
        assertSize(panel.Size, 100, 100);

        box.Size = new Vector2(300, 300);

        frame();
        double startTime = manual.CurrentTime;

        manual.CurrentTime = startTime + 100;
        root.UpdateSubTree();

        Assert.Multiple(() =>
        {
            Assert.That(panel.Size.X, Is.GreaterThan(120f), "Animated auto-size must have started growing by the midpoint.");
            Assert.That(panel.Size.X, Is.LessThan(280f), "Animated auto-size must not have completed at the midpoint.");
        });

        manual.CurrentTime = startTime + 400;
        root.UpdateSubTree();
        root.UpdateSubTree();

        assertSize(panel.Size, 300, 300, "Animated auto-size must settle exactly on the computed target.");
    }

    [Test]
    public void TestAnimatedAutoSizeShrinks()
    {
        var panel = new Container
        {
            AutoSizeAxes = Axes.Both,
            AutoSizeDuration = 200
        };
        var big = new Box { Size = new Vector2(300, 200) };
        var small = new Box { Size = new Vector2(50, 50) };
        panel.Add(big);
        panel.Add(small);
        root.Add(panel);
        settle();

        manual.CurrentTime += 400;
        root.UpdateSubTree();
        root.UpdateSubTree();
        assertSize(panel.Size, 300, 200);

        panel.Remove(big);
        frame();
        manual.CurrentTime += 400;
        root.UpdateSubTree();
        root.UpdateSubTree();

        assertSize(panel.Size, 50, 50, "Animated auto-size must shrink to the new (smaller) target.");
    }

    [Test]
    public void TestRetargetMidAnimationSettlesOnLatestTarget()
    {
        var panel = new Container
        {
            AutoSizeAxes = Axes.Both,
            AutoSizeDuration = 200
        };
        var box = new Box
        {
            Size = new Vector2(100, 100)
        };
        panel.Add(box);
        root.Add(panel);
        settle();

        manual.CurrentTime += 400;
        root.UpdateSubTree();
        root.UpdateSubTree();
        assertSize(panel.Size, 100, 100);

        box.Size = new Vector2(400, 400);
        frame();
        manual.CurrentTime += 50; // partway
        root.UpdateSubTree();
        Assert.That(panel.Size.X, Is.LessThan(400f));

        box.Size = new Vector2(250, 250);
        frame();
        manual.CurrentTime += 400;
        root.UpdateSubTree();
        root.UpdateSubTree();

        assertSize(panel.Size, 250, 250, "A mid-animation re-target must settle on the latest target.");
    }

    [Test]
    public void TestSwitchingDurationToZeroCancelsAnimation()
    {
        var panel = new Container
        {
            AutoSizeAxes = Axes.Both,
            AutoSizeDuration = 500
        };
        var box = new Box { Size = new Vector2(100, 100) };
        panel.Add(box);
        root.Add(panel);
        settle();

        box.Size = new Vector2(300, 300);
        frame();
        manual.CurrentTime += 50;
        root.UpdateSubTree();
        Assert.That(panel.Size.X, Is.LessThan(300f), "Animation should be in progress.");

        panel.AutoSizeDuration = 0;
        frame();
        frame();

        assertSize(panel.Size, 300, 300, "Setting AutoSizeDuration to 0 must cancel the animation and snap to the target.");
    }

    [Test]
    public void TestNonAutoSizedContainerIgnoresDuration()
    {
        var panel = new Container
        {
            Size = new Vector2(200, 150),
            AutoSizeDuration = 500,
            AutoSizeEasing = Easing.OutQuint
        };
        var box = new Box { Size = new Vector2(50) };
        panel.Add(box);
        root.Add(panel);
        settle();

        assertSize(panel.Size, 200, 150, "A non-auto-sized container keeps its explicit size.");

        box.Size = new Vector2(400, 400);
        frame();
        frame();

        assertSize(panel.Size, 200, 150, "A non-auto-sized container must ignore child size changes entirely.");
    }
}
