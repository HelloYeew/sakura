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
/// Tests for exact hit-testing: <see cref="Drawable.Contains"/> must respect the full
/// transform (rotation/shear, including inherited ones) rather than just the screen AABB.
/// </summary>
[TestFixture]
public class HitTestTest
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

    private void settle()
    {
        for (int i = 0; i < 3; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }
    }

    [Test]
    public void TestAxisAlignedContains()
    {
        var box = new Box { Position = new Vector2(100, 100), Size = new Vector2(50) };
        root.Add(box);
        settle();

        Assert.Multiple(() =>
        {
            Assert.That(box.Contains(new Vector2(120, 120)), Is.True);
            Assert.That(box.Contains(new Vector2(100, 100)), Is.True, "The top-left corner is contained.");
            Assert.That(box.Contains(new Vector2(151, 120)), Is.False);
            Assert.That(box.Contains(new Vector2(99, 120)), Is.False);
        });
    }

    [Test]
    public void TestRotatedDrawableContains()
    {
        // A 100x100 box rotated 45° around its centre at (200, 200) forms a diamond
        // with half-diagonal ~70.7px. Its AABB spans ±70.7 on both axes.
        var box = new Box
        {
            Position = new Vector2(200, 200),
            Size = new Vector2(100),
            Origin = Anchor.Centre,
            Rotation = 45
        };
        root.Add(box);
        settle();

        Assert.Multiple(() =>
        {
            Assert.That(box.Contains(new Vector2(200, 200)), Is.True, "The centre of a rotated drawable is contained.");
            Assert.That(box.Contains(new Vector2(200, 140)), Is.True, "A point inside the diamond is contained.");
            Assert.That(box.Contains(new Vector2(245, 155)), Is.False,
                "A point inside the screen AABB but outside the rotated quad must NOT be contained.");
            Assert.That(box.Contains(new Vector2(155, 245)), Is.False);
        });
    }

    [Test]
    public void TestChildOfRotatedParentContains()
    {
        // The child itself is axis-aligned in local space, but inherits the parent's rotation
        // through the matrix chain — Rotation == 0 alone must not skip the exact test.
        var parent = new Container
        {
            Position = new Vector2(200, 200),
            Size = new Vector2(100),
            Origin = Anchor.Centre,
            Rotation = 45
        };
        var child = new Box { RelativeSizeAxes = Axes.Both, Size = new Vector2(1) };
        parent.Add(child);
        root.Add(parent);
        settle();

        Assert.Multiple(() =>
        {
            Assert.That(child.Contains(new Vector2(200, 200)), Is.True);
            Assert.That(child.Contains(new Vector2(245, 155)), Is.False,
                "A child of a rotated parent must hit-test against its true (rotated) shape.");
        });
    }
}
