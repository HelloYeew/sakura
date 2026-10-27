// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.TransformSequenceExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Under a masking container, a child's geometry is built by the masking pass (to test it against the mask)
/// before the child updates. These tests check that it's built once per change rather than once by that pass
/// and again by the child's own update, and that what's built still matches the same tree without a mask.
/// </summary>
[TestFixture]
public class MaskingPassTransformsTest
{
    private partial class CountingBox : Box
    {
        public int TransformUpdates;

        public Vertex[] VerticesForAssert => Vertices;

        // protected internal, not protected: InternalsVisibleTo makes the base member's internal half
        // visible here, so the override has to match its full accessibility.
        protected internal override void UpdateTransforms()
        {
            TransformUpdates++;
            base.UpdateTransforms();
        }
    }

    private partial class CountingContainer : Container
    {
        public int TransformUpdates;

        protected internal override void UpdateTransforms()
        {
            TransformUpdates++;
            base.UpdateTransforms();
        }
    }

    /// <summary>Moves its target from its own update, the way a playfield places its notes.</summary>
    private partial class PositioningContainer : CountingContainer
    {
        public Drawable? Target;

        public override void Update()
        {
            base.Update();

            if (Target != null)
                Target.Y += 1;
        }
    }

    /// <summary>Resizes itself while it's being built, like <c>SpriteText</c> measuring its text.</summary>
    private partial class SelfMeasuringBox : CountingBox
    {
        private Vector2? pendingSize;

        public Vector2 MeasuredSize
        {
            set
            {
                pendingSize = value;
                Invalidate(InvalidationFlags.DrawInfo);
            }
        }

        protected internal override void UpdateTransforms()
        {
            if (pendingSize is Vector2 size)
            {
                pendingSize = null;
                Size = size;
            }

            base.UpdateTransforms();
        }
    }

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
        root.CompleteLoad();
    }

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    private void settle()
    {
        for (int i = 0; i < 3; i++)
            frame();
    }

    private Container addMask(bool masked = true)
    {
        var mask = new Container { Size = new Vector2(600, 500), Masking = masked };
        root.Add(mask);
        return mask;
    }

    private static void reset(params CountingBox[] boxes)
    {
        foreach (var b in boxes)
            b.TransformUpdates = 0;
    }

    [Test]
    public void TestChildOfMovingParentBuiltOncePerFrame()
    {
        var holder = new CountingContainer { Size = new Vector2(200) };
        var inner = new CountingContainer { Size = new Vector2(100) };
        var box = new CountingBox { Size = new Vector2(50) };
        inner.Add(box);
        holder.Add(inner);
        addMask().Add(holder);
        holder.MoveTo(new Vector2(300, 300), 100_000);
        settle();

        holder.TransformUpdates = inner.TransformUpdates = box.TransformUpdates = 0;

        for (int i = 0; i < 10; i++)
            frame();

        Assert.Multiple(() =>
        {
            Assert.That(holder.TransformUpdates, Is.EqualTo(10));
            Assert.That(inner.TransformUpdates, Is.EqualTo(10), "The masking pass and the container's own update must not both build it.");
            Assert.That(box.TransformUpdates, Is.EqualTo(10), "The masking pass and the box's own update must not both build it.");
        });
    }

    [Test]
    public void TestChildMovedBeforeTheMaskingPassBuiltOncePerFrame()
    {
        var mask = addMask();
        var outside = new CountingBox { Size = new Vector2(50) };
        mask.Add(outside);

        var positioner = new PositioningContainer { Size = new Vector2(400) };
        var placed = new CountingBox { Size = new Vector2(50) };
        positioner.Add(placed);
        positioner.Target = placed;
        mask.Add(positioner);
        settle();

        reset(outside, placed);

        for (int i = 0; i < 10; i++)
        {
            // Set before the update traversal, as an input handler would.
            outside.X += 1;
            frame();
        }

        Assert.Multiple(() =>
        {
            Assert.That(outside.TransformUpdates, Is.EqualTo(10));
            Assert.That(placed.TransformUpdates, Is.EqualTo(10));
            Assert.That(outside.DrawRectangle.X, Is.EqualTo(10).Within(0.01f));
            Assert.That(placed.DrawRectangle.Y, Is.EqualTo(13).Within(0.01f), "Three settle frames and ten measured frames.");
        });
    }

    [Test]
    public void TestRebuiltWhenChangedAfterTheMaskingPass()
    {
        var holder = new CountingContainer { Size = new Vector2(200) };
        var mover = new CountingBox { Size = new Vector2(50) };
        var fader = new CountingBox { Size = new Vector2(50) };
        var scheduled = new CountingBox { Size = new Vector2(50) };
        holder.Add(mover);
        holder.Add(fader);
        holder.Add(scheduled);
        addMask().Add(holder);
        settle();

        holder.MoveTo(new Vector2(100, 0), 160);
        mover.MoveTo(new Vector2(0, 100), 160);
        fader.FadeTo(0.5f, 160);
        reset(mover, fader, scheduled);

        // Each runs after the masking pass has built the child for its parent's move this frame.
        scheduled.Scheduler.Add(() => scheduled.X = 40);
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(mover.TransformUpdates, Is.EqualTo(2), "Its own move came after the masking pass built it.");
            Assert.That(fader.TransformUpdates, Is.EqualTo(2), "Its own fade came after the masking pass built it.");
            Assert.That(scheduled.TransformUpdates, Is.EqualTo(2), "Its scheduled move came after the masking pass built it.");
            Assert.That(scheduled.DrawRectangle.X, Is.EqualTo(holder.DrawRectangle.X + 40).Within(0.01f));
            Assert.That(mover.DrawRectangle.Y, Is.EqualTo(mover.Y).Within(0.01f));
        });
    }

    [Test]
    public void TestSizeMeasuredDuringTheBuildIsBuiltAgain()
    {
        var holder = new CountingContainer { Size = new Vector2(200) };
        var text = new SelfMeasuringBox { Size = new Vector2(10) };
        holder.Add(text);
        addMask().Add(holder);
        settle();

        holder.X = 5;
        text.MeasuredSize = new Vector2(80, 20);
        text.TransformUpdates = 0;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(text.TransformUpdates, Is.EqualTo(2), "The size it measured while being built invalidates it again.");
            Assert.That(text.DrawRectangle.Width, Is.EqualTo(80).Within(0.01f));
            Assert.That(text.DrawRectangle.X, Is.EqualTo(5).Within(0.01f));
        });
    }

    [Test]
    public void TestMaskedAwayAndHiddenChildrenNotRebuiltEveryFrame()
    {
        var mask = addMask();
        var outside = new CountingBox { Size = new Vector2(50), Position = new Vector2(1000, 0) };
        var hidden = new CountingBox { Size = new Vector2(50), Alpha = 0 };
        var hiddenParent = new CountingContainer { Size = new Vector2(100), Alpha = 0 };
        var inHiddenParent = new CountingBox { Size = new Vector2(50) };
        hiddenParent.Add(inHiddenParent);
        mask.Add(outside);
        mask.Add(hidden);
        mask.Add(hiddenParent);
        settle();

        reset(outside, hidden, inHiddenParent);
        hiddenParent.TransformUpdates = 0;

        for (int i = 0; i < 10; i++)
            frame();

        Assert.Multiple(() =>
        {
            Assert.That(outside.IsMaskedAway, Is.True);
            Assert.That(outside.TransformUpdates, Is.Zero, "Nothing changed it since the masking pass built it.");
            Assert.That(hidden.TransformUpdates, Is.Zero, "Nothing changed it since the masking pass built it.");
            Assert.That(hiddenParent.TransformUpdates, Is.Zero, "Nothing changed it since the masking pass built it.");
            Assert.That(inHiddenParent.TransformUpdates, Is.Zero, "Nothing changed it since the masking pass built it.");
        });
    }

    [Test]
    public void TestHiddenChildShownWithItsCurrentGeometry()
    {
        var hiddenParent = new CountingContainer { Size = new Vector2(100), Alpha = 0 };
        var box = new CountingBox { Size = new Vector2(50) };
        hiddenParent.Add(box);
        addMask().Add(hiddenParent);
        settle();

        // Moved while hidden, then left alone for a while before being shown.
        hiddenParent.Position = new Vector2(30, 40);
        box.Position = new Vector2(5, 0);
        settle();

        hiddenParent.Alpha = 1;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(box.DrawRectangle.X, Is.EqualTo(35).Within(0.01f));
            Assert.That(box.DrawRectangle.Y, Is.EqualTo(40).Within(0.01f));
            Assert.That(box.DrawAlpha, Is.EqualTo(1).Within(0.001f));
            Assert.That(box.VerticesForAssert[0].Color.W, Is.EqualTo(1).Within(0.001f));
        });
    }

    [Test]
    public void TestStaticListNotRebuiltAndScrollingListBuiltOncePerItem()
    {
        var scroll = new ScrollableContainer { Size = new Vector2(400, 400) };
        var boxes = new List<CountingBox>();

        for (int i = 0; i < 100; i++)
        {
            var b = new CountingBox { Size = new Vector2(50), Y = i * 60 };
            boxes.Add(b);
            scroll.Add(b);
        }

        root.Add(scroll);

        for (int i = 0; i < 30; i++)
            frame();

        reset(boxes.ToArray());

        for (int i = 0; i < 10; i++)
            frame();

        Assert.That(boxes.Sum(b => b.TransformUpdates), Is.Zero, "A list that isn't moving builds nothing, on screen or off it.");

        scroll.ScrollTo(new Vector2(0, 3000));
        frame();
        reset(boxes.ToArray());

        for (int i = 0; i < 10; i++)
            frame();

        Assert.That(boxes.Select(b => b.TransformUpdates), Is.All.EqualTo(10), "While scrolling, each item is built once per frame.");

        // Scrolled to the far end: the first item was masked away the whole way, and is built where it now is.
        scroll.ScrollTo(new Vector2(0, 0), false);
        frame();
        frame();

        Assert.That(boxes[0].IsMaskedAway, Is.False);
        Assert.That(boxes[0].DrawRectangle.Y, Is.EqualTo(scroll.DrawRectangle.Y).Within(0.01f));
    }

    /// <summary>
    /// The same tree with and without a mask big enough to hold all of it: a mask only decides what's drawn,
    /// so every vertex must come out the same on every frame, whichever pass built it.
    /// </summary>
    [Test]
    public void TestMaskedTreeMatchesUnmaskedTree()
    {
        var masked = buildMixedTree(addMask());
        var unmasked = buildMixedTree(addMask(false));

        for (int f = 0; f < 40; f++)
        {
            frame();

            for (int i = 0; i < masked.Count; i++)
            {
                var a = masked[i];
                var b = unmasked[i];

                Assert.That(a.DrawRectangle, Is.EqualTo(b.DrawRectangle), $"Frame {f}, box {i}: draw rectangle.");

                for (int v = 0; v < 4; v++)
                {
                    Assert.That(a.VerticesForAssert[v].Position, Is.EqualTo(b.VerticesForAssert[v].Position), $"Frame {f}, box {i}, vertex {v}: position.");
                    Assert.That(a.VerticesForAssert[v].Color, Is.EqualTo(b.VerticesForAssert[v].Color), $"Frame {f}, box {i}, vertex {v}: color.");
                }
            }
        }
    }

    private List<CountingBox> buildMixedTree(Container mask)
    {
        var boxes = new List<CountingBox>();

        var holder = new CountingContainer { Size = new Vector2(300) };
        var positioner = new PositioningContainer { Size = new Vector2(200), Position = new Vector2(50, 50) };
        holder.Add(positioner);
        mask.Add(holder);

        CountingBox add(Container parent, Vector2 position)
        {
            var b = new CountingBox { Size = new Vector2(20), Position = position };
            parent.Add(b);
            boxes.Add(b);
            return b;
        }

        add(holder, new Vector2(0, 0));
        var mover = add(holder, new Vector2(10, 10));
        var fader = add(holder, new Vector2(20, 20));
        var scheduled = add(holder, new Vector2(30, 30));
        var placed = add(positioner, new Vector2(0, 0));
        add(positioner, new Vector2(40, 0));
        positioner.Target = placed;

        // Moves and fades that start and stop on different frames, so some frames change only the parent,
        // some only a child, and some both.
        holder.MoveTo(new Vector2(100, 50), 320);
        holder.TransformSequence().Delay(400).FadeTo(0.6f, 160);
        mover.TransformSequence().Delay(160).MoveTo(new Vector2(60, 10), 240);
        fader.FadeTo(0.2f, 480);
        positioner.TransformSequence().Delay(240).RotateTo(15, 160);

        for (int i = 0; i < 6; i++)
        {
            int step = i;
            scheduled.Scheduler.AddDelayed(() => scheduled.X = 30 + step * 7, 50 + step * 90);
        }

        return boxes;
    }
}
