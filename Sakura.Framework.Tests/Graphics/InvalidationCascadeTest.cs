// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests for the targeted invalidation model: a drawable's geometry change recomputes only
/// itself (and notifies parents that lay out around children), a parent's geometry change
/// recomputes its whole subtree, and depth changes re-sort draw order without recomputation.
/// </summary>
[TestFixture]
public class InvalidationCascadeTest
{
    /// <summary>
    /// Counts geometry recomputations for cascade assertions.
    /// </summary>
    private partial class CountingBox : Box
    {
        public int TransformUpdates;

        public Vertex[] VerticesForAssert => Vertices;

        // From outside the framework assembly, `protected internal` members are
        // overridden as `protected`.
        protected override void UpdateTransforms()
        {
            TransformUpdates++;
            base.UpdateTransforms();
        }
    }

    private partial class CountingContainer : Container
    {
        public int TransformUpdates;

        protected override void UpdateTransforms()
        {
            TransformUpdates++;
            base.UpdateTransforms();
        }
    }

    /// <summary>
    /// Mimics <c>SpriteText</c>: measures and assigns its own size during its own update
    /// pass (while its invalidation flags are still set). Regression case for layout
    /// containers missing such size changes.
    /// </summary>
    private partial class SelfMeasuringBox : Box
    {
        private Vector2 measuredSize;
        private bool measured = true;

        /// <summary>Queues a new "measured" size to be applied during the next update.</summary>
        public Vector2 MeasuredSize
        {
            set
            {
                measuredSize = value;
                measured = false;
                Invalidate(InvalidationFlags.DrawInfo);
            }
        }

        protected override void UpdateTransforms()
        {
            if (!measured)
            {
                measured = true;
                Size = measuredSize; // self-resize while already dirty, like SpriteText.computeLayout
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
        root.LoadComplete();
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

    [Test]
    public void TestSiblingsNotRecomputedWhenOneChildMoves()
    {
        var holder = new CountingContainer { Size = new Vector2(500, 500) };
        var mover = new CountingBox { Size = new Vector2(50) };
        var sibling = new CountingBox { Position = new Vector2(100, 0), Size = new Vector2(50) };

        holder.Add(mover);
        holder.Add(sibling);
        root.Add(holder);
        settle();

        mover.TransformUpdates = sibling.TransformUpdates = holder.TransformUpdates = 0;

        mover.Position = new Vector2(10, 10);
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(mover.TransformUpdates, Is.EqualTo(1), "The moved drawable must recompute.");
            Assert.That(sibling.TransformUpdates, Is.Zero, "Siblings must not recompute when an unrelated child moves.");
            Assert.That(holder.TransformUpdates, Is.Zero, "A non-layout parent must not recompute when a child moves.");
        });

        Assert.That(mover.DrawRectangle.X, Is.EqualTo(10).Within(0.01f));
    }

    [Test]
    public void TestCleanFrameRecomputesNothing()
    {
        var holder = new CountingContainer { Size = new Vector2(500, 500) };
        var box = new CountingBox { Size = new Vector2(50) };
        holder.Add(box);
        root.Add(holder);
        settle();

        box.TransformUpdates = holder.TransformUpdates = 0;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(box.TransformUpdates, Is.Zero);
            Assert.That(holder.TransformUpdates, Is.Zero);
        });
    }

    [Test]
    public void TestParentGeometryChangeRecomputesSubtree()
    {
        var holder = new CountingContainer { Size = new Vector2(500, 500) };
        var a = new CountingBox { Size = new Vector2(50) };
        var b = new CountingBox { Position = new Vector2(100, 0), Size = new Vector2(50) };

        holder.Add(a);
        holder.Add(b);
        root.Add(holder);
        settle();

        a.TransformUpdates = b.TransformUpdates = 0;

        holder.Position = new Vector2(30, 40);
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(a.TransformUpdates, Is.EqualTo(1), "Children must recompute when their parent's geometry changes.");
            Assert.That(b.TransformUpdates, Is.EqualTo(1));
            Assert.That(a.DrawRectangle.X, Is.EqualTo(30).Within(0.01f));
            Assert.That(b.DrawRectangle.X, Is.EqualTo(130).Within(0.01f));
        });
    }

    [Test]
    public void TestAutoSizeParentGrowsWhenChildResizes()
    {
        var panel = new Container { AutoSizeAxes = Axes.Both };
        var box = new Box { Size = new Vector2(50, 40) };
        panel.Add(box);
        root.Add(panel);
        settle();

        Assert.That(panel.DrawSize.X, Is.EqualTo(50).Within(0.01f));

        box.Size = new Vector2(120, 40);
        frame();
        frame(); // size change is observed by the parent on the following pass

        Assert.Multiple(() =>
        {
            Assert.That(panel.Size.X, Is.EqualTo(120).Within(0.01f), "An auto-size parent must react to a child's resize.");
            Assert.That(panel.DrawSize.X, Is.EqualTo(120).Within(0.01f));
        });
    }

    [Test]
    public void TestAutoSizeParentGrowsWhenChildMoves()
    {
        var panel = new Container { AutoSizeAxes = Axes.Both };
        var box = new Box { Size = new Vector2(50, 40) };
        panel.Add(box);
        root.Add(panel);
        settle();

        box.Position = new Vector2(70, 0);
        frame();
        frame();

        Assert.That(panel.Size.X, Is.EqualTo(120).Within(0.01f), "An auto-size parent must react to a child's movement.");
    }

    [Test]
    public void TestChainedAutoSizePropagatesThroughLevels()
    {
        var outer = new Container { AutoSizeAxes = Axes.Both };
        var inner = new Container { AutoSizeAxes = Axes.Both };
        var box = new Box { Size = new Vector2(50, 40) };

        inner.Add(box);
        outer.Add(inner);
        root.Add(outer);
        settle();

        Assert.That(outer.Size.X, Is.EqualTo(50).Within(0.01f));

        box.Size = new Vector2(90, 40);

        // One frame per auto-size level, plus one to settle.
        frame();
        frame();
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(inner.Size.X, Is.EqualTo(90).Within(0.01f));
            Assert.That(outer.Size.X, Is.EqualTo(90).Within(0.01f), "Auto-size must propagate through nested auto-size containers.");
        });
    }

    [Test]
    public void TestFlowContainerReflowsOnChildResize()
    {
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Horizontal,
            Size = new Vector2(500, 100)
        };

        var first = new Box { Size = new Vector2(50, 50) };
        var second = new Box { Size = new Vector2(50, 50) };

        flow.Add(first);
        flow.Add(second);
        root.Add(flow);
        settle();

        Assert.That(second.Position.X, Is.EqualTo(50).Within(0.01f));

        first.Size = new Vector2(80, 50);
        frame();
        frame();

        Assert.That(second.Position.X, Is.EqualTo(80).Within(0.01f), "A flow container must re-layout when a child's size changes.");
    }

    [Test]
    public void TestFlowReflowsWhenChildMeasuresItselfDuringUpdate()
    {
        // Regression: SpriteText-style drawables size themselves inside their own update
        // pass; the flow must still be notified and re-layout on the following frame.
        var flow = new FlowContainer
        {
            Direction = FlowDirection.Horizontal,
            Size = new Vector2(500, 100)
        };

        var first = new SelfMeasuringBox();
        var second = new SelfMeasuringBox();

        flow.Add(first);
        flow.Add(second);
        root.Add(flow);
        settle();

        // Both children measured 0x0 initially → stacked at x=0.
        Assert.That(second.Position.X, Is.EqualTo(0).Within(0.01f));

        // Children "finish measuring" (e.g. font becomes available).
        first.MeasuredSize = new Vector2(70, 30);
        second.MeasuredSize = new Vector2(40, 30);
        settle();

        Assert.Multiple(() =>
        {
            Assert.That(first.Size.X, Is.EqualTo(70).Within(0.01f));
            Assert.That(second.Position.X, Is.EqualTo(70).Within(0.01f),
                "The flow must re-layout after a child measures itself during its own update.");
        });
    }

    [Test]
    public void TestAutoSizeGrowsWhenChildMeasuresItselfDuringUpdate()
    {
        var panel = new Container { AutoSizeAxes = Axes.Both };
        var text = new SelfMeasuringBox();
        panel.Add(text);
        root.Add(panel);
        settle();

        Assert.That(panel.DrawSize.X, Is.EqualTo(0).Within(0.01f));

        text.MeasuredSize = new Vector2(150, 20);
        settle();

        Assert.That(panel.DrawSize.X, Is.EqualTo(150).Within(0.01f),
            "An auto-size parent must grow when a child measures itself during its own update.");
    }

    [Test]
    public void TestAutoSizeReactsToChildVisibilityChange()
    {
        // this test came from dropdown regression: an auto-size container with a hidden child (menu).
        // UpdateAutoSize skips hidden children, so showing/hiding the child must
        // re-trigger the parent's layout.
        var dropdown = new Container { AutoSizeAxes = Axes.Y, Size = new Vector2(200, 0) };
        var header = new Box { Size = new Vector2(200, 30) };
        var menu = new Box { Position = new Vector2(0, 30), Size = new Vector2(200, 90), Alpha = 0 };

        dropdown.Add(header);
        dropdown.Add(menu);
        root.Add(dropdown);
        settle();

        Assert.That(dropdown.DrawSize.Y, Is.EqualTo(30).Within(0.01f), "A hidden child must not contribute to auto-size.");

        menu.Show();
        frame();
        frame();

        Assert.That(dropdown.DrawSize.Y, Is.EqualTo(120).Within(0.01f), "Showing a child must grow the auto-size parent.");

        menu.Hide();
        frame();
        frame();

        Assert.That(dropdown.DrawSize.Y, Is.EqualTo(30).Within(0.01f), "Hiding a child must shrink the auto-size parent.");
    }

    [Test]
    public void TestChildrenOfHiddenContainerHealOnShow()
    {
        // Items added to a hidden menu must have valid screen geometry once it is shown
        // (the hidden container never computes transforms while hidden).
        var menu = new Container { Position = new Vector2(100, 50), Size = new Vector2(200, 90), Alpha = 0 };
        root.Add(menu);
        settle();

        var item = new Box { Position = new Vector2(0, 30), Size = new Vector2(200, 30) };
        menu.Add(item);
        settle();

        menu.Show();
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(item.DrawRectangle.X, Is.EqualTo(100).Within(0.01f), "Items added while hidden must position correctly on show.");
            Assert.That(item.DrawRectangle.Y, Is.EqualTo(80).Within(0.01f));
        });
    }

    [Test]
    public void TestFadeUsesColourFastPathWithoutGeometryRegeneration()
    {
        var box = new CountingBox
        {
            Position = new Vector2(40, 30),
            Size = new Vector2(50)
        };
        root.Add(box);
        settle();

        box.TransformUpdates = 0;

        box.Alpha = 0.5f;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(box.TransformUpdates, Is.Zero, "A colour-only change must not regenerate geometry.");
            Assert.That(box.DrawAlpha, Is.EqualTo(0.5f).Within(0.001f), "DrawAlpha must still update on the colour fast path.");
            Assert.That(box.VerticesForAssert[0].Color.W, Is.EqualTo(0.5f).Within(0.001f), "Vertex alpha must be rewritten on the colour fast path.");
            Assert.That(box.DrawRectangle.X, Is.EqualTo(40).Within(0.01f), "Geometry must be untouched by a colour-only change.");
        });
    }

    [Test]
    public void TestContainerFadeTransformCascadesToChildren()
    {
        // The screen-transition pattern: a container fades in via a transform while its
        // children stay at alpha 1. The children's DrawAlpha must track the parent's fade
        // every frame — including frames where nothing else invalidates them.
        var screen = new Container { Size = new Vector2(400, 300), Alpha = 0, AlwaysPresent = true };
        var content = new Box { Size = new Vector2(100) };
        screen.Add(content);
        root.Add(screen);
        settle();

        Assert.That(content.DrawAlpha, Is.EqualTo(0).Within(0.001f));

        screen.FadeIn(100);

        // Halfway through the fade (linear easing): child must track the parent.
        manual.CurrentTime += 50;
        root.UpdateSubTree();
        Assert.That(content.DrawAlpha, Is.EqualTo(screen.DrawAlpha).Within(0.001f), "Child DrawAlpha must track the parent's fade mid-transform.");
        Assert.That(content.DrawAlpha, Is.EqualTo(0.5f).Within(0.05f));

        // After completion the child must be fully visible.
        manual.CurrentTime += 100;
        root.UpdateSubTree();
        root.UpdateSubTree();
        Assert.That(content.DrawAlpha, Is.EqualTo(1f).Within(0.001f), "Child DrawAlpha must reach 1 when the parent's fade completes.");
    }

    [Test]
    public void TestContainerFadeOutCascadesZeroAlphaToChildren()
    {
        // a container (without AlwaysPresent) fading itself out to exactly zero alpha
        // must still tell its children their effective alpha dropped to zero. Container.Update()
        // previously returned early as soon as its own Alpha reached zero, before ever reaching the
        // Colour-invalidation cascade so a child's cached DrawAlpha stayed stuck at its last
        // nonzero value forever (until the parent faded back in, which isn't affected by the same
        // early return).
        var screen = new Container { Size = new Vector2(400, 300) };
        var content = new Box { Size = new Vector2(100) };
        screen.Add(content);
        root.Add(screen);
        settle();

        Assert.That(content.DrawAlpha, Is.EqualTo(1f).Within(0.001f));

        screen.FadeOut(100);

        manual.CurrentTime += 150;
        root.UpdateSubTree();
        root.UpdateSubTree();

        Assert.Multiple(() =>
        {
            Assert.That(screen.Alpha, Is.EqualTo(0f).Within(0.001f));
            Assert.That(content.DrawAlpha, Is.EqualTo(0f).Within(0.001f),
                "Child DrawAlpha must reach 0 when a (non-AlwaysPresent) parent fades itself out to invisible.");
        });
    }

    [Test]
    public void TestContainerMoveTransformCascadesToChildren()
    {
        var screen = new Container { Position = new Vector2(0, 300), Size = new Vector2(400, 300) };
        var content = new Box { Position = new Vector2(10, 10), Size = new Vector2(100) };
        screen.Add(content);
        root.Add(screen);
        settle();

        Assert.That(content.DrawRectangle.Y, Is.EqualTo(310).Within(0.01f));

        screen.MoveTo(Vector2.Zero, 100);

        manual.CurrentTime += 150;
        root.UpdateSubTree();
        root.UpdateSubTree();

        Assert.Multiple(() =>
        {
            Assert.That(screen.Position.Y, Is.EqualTo(0).Within(0.01f));
            Assert.That(content.DrawRectangle.Y, Is.EqualTo(10).Within(0.01f),
                "Children must follow a parent animated by transforms to its final position.");
        });
    }

    [Test]
    public void TestGeometryChangeWhileHiddenAppliesOnShow()
    {
        var box = new Box { Size = new Vector2(50) };
        root.Add(box);
        settle();

        box.Hide();
        frame();

        box.Position = new Vector2(300, 200);
        frame();

        box.Show();
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(box.DrawRectangle.X, Is.EqualTo(300).Within(0.01f), "A move performed while hidden must apply once shown.");
            Assert.That(box.DrawRectangle.Y, Is.EqualTo(200).Within(0.01f));
        });
    }

    [Test]
    public void TestDepthChangeReordersDrawNodes()
    {
        var holder = new Container { Size = new Vector2(500, 500) };
        var a = new Box { Position = new Vector2(0, 0), Size = new Vector2(50) };
        var b = new Box { Position = new Vector2(100, 0), Size = new Vector2(50) };

        holder.Add(a);
        holder.Add(b);
        root.Add(holder);
        settle();

        var holderNode = (ContainerDrawNode)((ContainerDrawNode)root.GenerateDrawNodeSubtree(0)).Children[0];
        Assert.That(holderNode.Children[0].DrawRectangle.X, Is.EqualTo(0).Within(0.01f), "Equal depths must preserve insertion order.");

        // Raising a's depth should draw it after b.
        a.Depth = 1;
        frame();

        holderNode = (ContainerDrawNode)((ContainerDrawNode)root.GenerateDrawNodeSubtree(1)).Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(holderNode.Children[0].DrawRectangle.X, Is.EqualTo(100).Within(0.01f), "Lower depth must be drawn first after a depth change.");
            Assert.That(holderNode.Children[1].DrawRectangle.X, Is.EqualTo(0).Within(0.01f));
        });
    }

    [Test]
    public void TestReAddedSubtreeRecomputesAgainstNewParent()
    {
        var oldParent = new Container { Position = new Vector2(0, 0), Size = new Vector2(400, 400) };
        var newParent = new Container { Position = new Vector2(200, 100), Size = new Vector2(400, 400) };
        var subtree = new Container { Size = new Vector2(100, 100) };
        var box = new Box { Position = new Vector2(10, 10), Size = new Vector2(20) };

        subtree.Add(box);
        oldParent.Add(subtree);
        root.Add(oldParent);
        root.Add(newParent);
        settle();

        Assert.That(box.DrawRectangle.X, Is.EqualTo(10).Within(0.01f));

        oldParent.Remove(subtree);
        newParent.Add(subtree);
        settle();

        Assert.Multiple(() =>
        {
            Assert.That(box.DrawRectangle.X, Is.EqualTo(210).Within(0.01f), "A re-parented (previously clean) subtree must fully recompute against its new parent.");
            Assert.That(box.DrawRectangle.Y, Is.EqualTo(110).Within(0.01f));
        });
    }
}
