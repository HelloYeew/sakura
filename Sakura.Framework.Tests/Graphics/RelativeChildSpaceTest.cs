// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Headless behavioural tests for the relative-child coordinate space
/// (<see cref="Container.RelativeChildSize"/> / <see cref="Container.RelativeChildOffset"/>)
/// </summary>
[TestFixture]
public class RelativeChildSpaceTest
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

    private void frame() => frame(16);

    private void frame(double advanceMs)
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
    /// Asserts a <see cref="Vector2"/> componentwise. NUnit's <c>.Within()</c> tolerance is not
    /// supported on the framework's custom <see cref="Vector2"/>, so comparisons assert
    /// <c>.X</c>/<c>.Y</c> individually.
    /// </summary>
    private static void assertVector(Vector2 actual, float x, float y, string? message = null)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(x).Within(0.01f), message);
            Assert.That(actual.Y, Is.EqualTo(y).Within(0.01f), message);
        });
    }

    [Test]
    public void TestDefaults()
    {
        var container = new Container { Size = new Vector2(400) };

        assertVector(container.RelativeChildSize, 1, 1, "RelativeChildSize must default to One.");
        assertVector(container.RelativeChildOffset, 0, 0, "RelativeChildOffset must default to Zero.");
    }

    [Test]
    public void TestDefaultRelativeResolutionUnchanged()
    {
        // With the defaults, a relatively-sized/positioned child must resolve exactly as it did
        // before the feature existed: size = relative * ChildSize, position = relative * ChildSize.
        var container = new Container { Position = new Vector2(0, 0), Size = new Vector2(400) };
        var child = new Box
        {
            RelativeSizeAxes = Axes.Both,
            RelativePositionAxes = Axes.Both,
            Size = new Vector2(0.5f),
            Position = new Vector2(0.25f)
        };
        container.Add(child);
        root.Add(container);
        settle();

        assertVector(child.DrawSize, 200, 200, "0.5 of 400px = 200px.");
        Assert.Multiple(() =>
        {
            Assert.That(child.DrawRectangle.X, Is.EqualTo(100).Within(0.01f), "0.25 of 400px = 100px from the container's left.");
            Assert.That(child.DrawRectangle.Y, Is.EqualTo(100).Within(0.01f));
        });
    }

    [Test]
    public void TestRelativeChildSizeScalesRelativeSize()
    {
        var container = new Container { Size = new Vector2(400) };
        var child = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.5f)
        };
        container.Add(child);
        root.Add(container);
        settle();

        assertVector(child.DrawSize, 200, 200);

        // Doubling the relative space halves the resolved pixel size for the same relative value.
        container.RelativeChildSize = new Vector2(2);
        frame();
        frame();
        assertVector(child.DrawSize, 100, 100, "RelativeChildSize (2,2) maps relative 0.5 to 0.5/2*400 = 100px.");

        // Halving the relative space doubles it.
        container.RelativeChildSize = new Vector2(0.5f);
        frame();
        frame();
        assertVector(child.DrawSize, 400, 400, "RelativeChildSize (0.5,0.5) maps relative 0.5 to 0.5/0.5*400 = 400px.");
    }

    [Test]
    public void TestRelativeChildSizeScalesRelativePosition()
    {
        var container = new Container { Position = new Vector2(0, 0), Size = new Vector2(400) };
        var child = new Box
        {
            RelativePositionAxes = Axes.Both,
            Size = new Vector2(20),
            Position = new Vector2(0.5f)
        };
        container.Add(child);
        root.Add(container);
        settle();

        // Default: relative position 0.5 -> 200px.
        Assert.That(child.DrawRectangle.X, Is.EqualTo(200).Within(0.01f));

        container.RelativeChildSize = new Vector2(2);
        frame();
        frame();

        // 0.5 / 2 * 400 = 100px.
        Assert.That(child.DrawRectangle.X, Is.EqualTo(100).Within(0.01f),
            "RelativeChildSize must scale relative position as well as relative size.");
    }

    [Test]
    public void TestRelativeChildOffsetShiftsPositionNotSize()
    {
        var container = new Container { Position = new Vector2(0, 0), Size = new Vector2(400) };
        var child = new Box
        {
            RelativeSizeAxes = Axes.Both,
            RelativePositionAxes = Axes.Both,
            Size = new Vector2(0.5f),
            Position = new Vector2(0.25f)
        };
        container.Add(child);
        root.Add(container);
        settle();

        Assert.That(child.DrawRectangle.X, Is.EqualTo(100).Within(0.01f));
        assertVector(child.DrawSize, 200, 200);

        // Offset by 0.25: child at relative position 0.25 now resolves to pixel 0.
        container.RelativeChildOffset = new Vector2(0.25f);
        frame();
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(child.DrawRectangle.X, Is.EqualTo(0).Within(0.01f),
                "RelativeChildOffset must shift the relative-space origin (0.25 - 0.25) * 400 = 0.");
            Assert.That(child.DrawRectangle.Y, Is.EqualTo(0).Within(0.01f));
        });
        assertVector(child.DrawSize, 200, 200, "RelativeChildOffset must not affect resolved size.");
    }

    [Test]
    public void TestNonRelativeChildIsUnaffected()
    {
        // A child with absolute (non-relative) position/size must ignore the relative-space settings.
        var container = new Container
        {
            Position = new Vector2(0, 0),
            Size = new Vector2(400),
            RelativeChildSize = new Vector2(2),
            RelativeChildOffset = new Vector2(0.3f)
        };
        var child = new Box
        {
            Size = new Vector2(60),
            Position = new Vector2(40, 30)
        };
        container.Add(child);
        root.Add(container);
        settle();

        assertVector(child.DrawSize, 60, 60, "Absolute size must be unaffected.");
        Assert.Multiple(() =>
        {
            Assert.That(child.DrawRectangle.X, Is.EqualTo(40).Within(0.01f), "Absolute position must be unaffected.");
            Assert.That(child.DrawRectangle.Y, Is.EqualTo(30).Within(0.01f));
        });
    }

    [Test]
    public void TestZeroRelativeChildSizeThrows()
    {
        var container = new Container { Size = new Vector2(400) };

        Assert.Multiple(() =>
        {
            Assert.Throws<System.ArgumentException>(() => container.RelativeChildSize = new Vector2(0, 1),
                "Zero on the X axis must be rejected.");
            Assert.Throws<System.ArgumentException>(() => container.RelativeChildSize = new Vector2(1, 0),
                "Zero on the Y axis must be rejected.");
            Assert.Throws<System.ArgumentException>(() => container.RelativeChildSize = Vector2.Zero,
                "Zero on both axes must be rejected.");
        });

        // The rejected assignments must not have mutated the property.
        assertVector(container.RelativeChildSize, 1, 1);
    }

    [Test]
    public void TestChangingRelativeChildSizeCascadesToChildren()
    {
        // Changing the relative space must invalidate and re-resolve every child the next pass.
        var container = new Container { Size = new Vector2(400) };
        var a = new Box { RelativeSizeAxes = Axes.Both, Size = new Vector2(0.5f) };
        var b = new Box { RelativeSizeAxes = Axes.Both, Size = new Vector2(0.25f) };
        container.Add(a);
        container.Add(b);
        root.Add(container);
        settle();

        Assert.That(a.DrawSize.X, Is.EqualTo(200).Within(0.01f));
        Assert.That(b.DrawSize.X, Is.EqualTo(100).Within(0.01f));

        container.RelativeChildSize = new Vector2(2);
        frame();
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(a.DrawSize.X, Is.EqualTo(100).Within(0.01f), "Child A must re-resolve after the relative space changes.");
            Assert.That(b.DrawSize.X, Is.EqualTo(50).Within(0.01f), "Child B must re-resolve after the relative space changes.");
        });
    }

    [Test]
    public void TestRelativeChildSizeRespectsPadding()
    {
        // The relative space is built on ChildSize (DrawSize minus padding), so padding must still
        // apply on top of RelativeChildSize scaling.
        var container = new Container
        {
            Size = new Vector2(400),
            Padding = new MarginPadding(50) // content area = 300x300
        };
        var child = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1f)
        };
        container.Add(child);
        root.Add(container);
        settle();

        assertVector(child.DrawSize, 300, 300, "Relative 1.0 fills the padded content area (300px), not the full 400px.");

        container.RelativeChildSize = new Vector2(2);
        frame();
        frame();

        assertVector(child.DrawSize, 150, 150, "RelativeChildSize scales within the padded content area: 1.0/2*300 = 150px.");
    }
}
