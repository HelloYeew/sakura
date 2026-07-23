// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Regression test of https://github.com/HelloYeew/sakura/pull/152
/// </summary>
[TestFixture]
public class FlowContainerPaddingTest
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

    // Flow layout + auto-size + relative resolution need several passes to settle.
    private void settle()
    {
        for (int i = 0; i < 5; i++)
            frame();
    }

    /// <summary>
    /// The exact scenario measured in the fix plan: a 260-wide panel → vertical
    /// <see cref="FlowContainer"/> (relative width, auto height, 15px padding) → a relatively-sized
    /// child. The child width is 260 − 30 = 230, its left edge sits 15px (one padding) inside the
    /// flow, and its right edge lands on the inner content edge (flow.Right − 15), NOT the flow's
    /// outer edge.
    /// </summary>
    [Test]
    public void TestRelativeChildHonoursPaddingOnce()
    {
        Container flow = null!;
        Container child = null!;

        var panel = new Container
        {
            Position = new Vector2(40, 30),
            Size = new Vector2(260, 380),
            Child = flow = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Padding = new MarginPadding(15),
                Child = child = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 40
                }
            }
        };

        root.Add(panel);
        settle();

        float flowLeft = flow.DrawRectangle.X;
        float flowRight = flow.DrawRectangle.X + flow.DrawRectangle.Width;

        Assert.Multiple(() =>
        {
            // Content width = flow width (260) minus both horizontal paddings (30).
            Assert.That(child.DrawSize.X, Is.EqualTo(230).Within(0.01f),
                "Relative child width must be flow width minus total horizontal padding.");

            // Left edge is exactly ONE Padding.Left inside the flow (30 = 2x15 was the bug).
            Assert.That(child.DrawRectangle.X - flowLeft, Is.EqualTo(15).Within(0.01f),
                "Child left edge must sit exactly one Padding.Left inside the flow, not two.");

            // Right edge lands on the inner content edge, not the flow's outer edge.
            float childRight = child.DrawRectangle.X + child.DrawSize.X;
            Assert.That(childRight, Is.EqualTo(flowRight - 15).Within(0.01f),
                "Relative child right edge must stop at the inner content edge (flow.Right - Padding.Right).");
            Assert.That(childRight, Is.LessThan(flowRight),
                "Relative child must not overflow past the flow's outer edge.");
        });
    }

    /// <summary>
    /// A plain (non-relative) child in a padded flow must start at exactly <c>Padding.Left/Top</c>
    /// relative to the flow — proving the fix applies to all flow children, not only relatively-sized
    /// ones.
    /// </summary>
    [Test]
    public void TestNonRelativeChildStartsAtPadding()
    {
        Container flow = null!;
        Box child = null!;

        var panel = new Container
        {
            Position = new Vector2(40, 30),
            Size = new Vector2(260, 380),
            Child = flow = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Padding = new MarginPadding(15),
                Child = child = new Box
                {
                    Size = new Vector2(50, 40)
                }
            }
        };

        root.Add(panel);
        settle();

        Assert.Multiple(() =>
        {
            Assert.That(child.DrawRectangle.X - flow.DrawRectangle.X, Is.EqualTo(15).Within(0.01f),
                "Non-relative child left edge must equal Padding.Left.");
            Assert.That(child.DrawRectangle.Y - flow.DrawRectangle.Y, Is.EqualTo(15).Within(0.01f),
                "Non-relative child top edge must equal Padding.Top.");
        });
    }

    /// <summary>
    /// An auto-sizing flow must still wrap its content plus BOTH padding edges. With a single 40px-tall
    /// row and 15px padding the auto height is 40 + 30 = 70 — unchanged by the position fix.
    /// </summary>
    [Test]
    public void TestAutoSizeIncludesBothPaddingEdges()
    {
        Container flow = null!;

        var panel = new Container
        {
            Position = new Vector2(40, 30),
            Size = new Vector2(260, 380),
            Child = flow = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Padding = new MarginPadding(15),
                Child = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 40
                }
            }
        };

        root.Add(panel);
        settle();

        Assert.Multiple(() =>
        {
            Assert.That(flow.DrawSize.X, Is.EqualTo(260).Within(0.01f), "Relative-width flow must fill the panel width.");
            Assert.That(flow.DrawSize.Y, Is.EqualTo(70).Within(0.01f), "Auto height must be content (40) + top + bottom padding (30).");
        });
    }

    /// <summary>
    /// Follow-up to Bug 2: a flow child's OWN <see cref="Drawable.Margin"/> used to be double-applied
    /// too — <c>PerformLayout</c> baked <c>Margin.Left/Top</c> into the child's <c>Position</c> while
    /// <see cref="Drawable.UpdateTransforms"/> added it again. With a flow <c>Padding(15)</c> and a
    /// child <c>Margin(10)</c>, the child must land 25px (15 + 10) inside the flow, not 35px (15 + 2x10).
    /// </summary>
    [Test]
    public void TestChildMarginAppliedOnce()
    {
        Container flow = null!;
        Box child = null!;

        var panel = new Container
        {
            Position = new Vector2(40, 30),
            Size = new Vector2(260, 380),
            Child = flow = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Padding = new MarginPadding(15),
                Child = child = new Box
                {
                    Size = new Vector2(50, 40),
                    Margin = new MarginPadding(10)
                }
            }
        };

        root.Add(panel);
        settle();

        Assert.Multiple(() =>
        {
            // Padding.Left (15) + Margin.Left (10) = 25. The bug produced 35 (15 + 2x10).
            Assert.That(child.DrawRectangle.X - flow.DrawRectangle.X, Is.EqualTo(25).Within(0.01f),
                "Child left edge must be Padding.Left + Margin.Left, with the margin applied exactly once.");
            Assert.That(child.DrawRectangle.Y - flow.DrawRectangle.Y, Is.EqualTo(25).Within(0.01f),
                "Child top edge must be Padding.Top + Margin.Top, with the margin applied exactly once.");

            // Auto height wraps the child's full margin box plus both paddings: 40 + 2x10 + 2x15 = 90.
            Assert.That(flow.DrawSize.Y, Is.EqualTo(40 + 20 + 30).Within(0.01f),
                "Auto height must include the child's full margin box (40 + 2x10) plus both paddings (30).");
        });
    }

    /// <summary>
    /// An auto-sizing flow on BOTH axes fits its widest/tallest content plus padding on each side.
    /// </summary>
    [Test]
    public void TestAutoSizeBothAxesAroundContent()
    {
        Container flow = null!;

        var panel = new Container
        {
            Position = new Vector2(40, 30),
            Size = new Vector2(400, 400),
            Child = flow = new FlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 6),
                Padding = new MarginPadding(15),
                Children = new Drawable[]
                {
                    new Box { Size = new Vector2(120, 40) },
                    new Box { Size = new Vector2(80, 30) }
                }
            }
        };

        root.Add(panel);
        settle();

        Assert.Multiple(() =>
        {
            // Widest child (120) + both paddings (30).
            Assert.That(flow.DrawSize.X, Is.EqualTo(120 + 30).Within(0.01f));
            // 40 + 6 spacing + 30 = 76 content, + both paddings (30) = 106.
            Assert.That(flow.DrawSize.Y, Is.EqualTo(40 + 6 + 30 + 30).Within(0.01f));
        });
    }
}
