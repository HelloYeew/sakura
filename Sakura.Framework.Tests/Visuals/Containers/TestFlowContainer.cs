// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.Containers;

public partial class TestFlowContainer : TestScene
{
    private FlowContainer flow = null!;
    private Container frame = null!;

    private const float child_size = 80;
    private const float spacing = 10;
    private const int child_count = 3;
    private const float frame_width = 600;

    /// <summary>
    /// Total flow-axis extent of all children plus the gaps between them.
    /// </summary>
    private const float content_width = child_count * child_size + (child_count - 1) * spacing;

    private static readonly Color[] child_colors =
    {
        Color.Crimson,
        Color.Chartreuse,
        Color.CornflowerBlue,
        Color.DarkOrange,
    };

    [SetUp]
    public void SetUp()
    {
        AddStep("Create flow", () =>
        {
            Clear();

            Add(frame = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(frame_width, 300),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = Color.DarkSlateGray
                    },
                    flow = new FlowContainer
                    {
                        // fixed full-width container so there is free space to align within.
                        RelativeSizeAxes = Axes.X,
                        Width = 1,
                        AutoSizeAxes = Axes.Y,
                        Direction = FlowDirection.Horizontal,
                        Spacing = new Vector2(spacing, 0),
                        Children = child_colors.Take(child_count).Select(c => (Drawable)new Box
                        {
                            Size = new Vector2(child_size),
                            Color = c
                        }).ToArray()
                    }
                }
            });
        });
    }

    private float firstChildX => flow.Children.First().Position.X;

    [Test]
    public void TestStartAlignment()
    {
        AddStep("Set Start", () => flow.Alignment = FlowAlignment.Start);
        // default behavior: first child packed at the left edge (plus padding, which is zero here).
        AddAssert("First child at left", () => Precision.AlmostEquals(firstChildX, 0));
    }

    [Test]
    public void TestCenterAlignment()
    {
        AddStep("Set Center", () => flow.Alignment = FlowAlignment.Center);
        // free space is split evenly on both sides.
        float expected = (frame_width - content_width) / 2f;
        AddAssert("First child centered", () => Precision.AlmostEquals(firstChildX, expected));
    }

    [Test]
    public void TestEndAlignment()
    {
        AddStep("Set End", () => flow.Alignment = FlowAlignment.End);
        // all free space pushed to the left so the row ends at the right edge.
        float expected = frame_width - content_width;
        AddAssert("First child at end offset", () => Precision.AlmostEquals(firstChildX, expected));
    }

    [Test]
    public void TestAutoSizeIgnoresAlignment()
    {
        // an auto-sizing flow axis fits content exactly, so there is no free
        // space to distribute and alignment must have no effect.
        AddStep("Auto-size both axes", () => flow.AutoSizeAxes = Axes.Both);
        AddStep("Set Center", () => flow.Alignment = FlowAlignment.Center);
        AddAssert("First child still at left", () => Precision.AlmostEquals(firstChildX, 0));
    }

    [Test]
    public void TestCycleAlignment()
    {
        // visual sanity check: step through each alignment by hand.
        AddStep("Start", () => flow.Alignment = FlowAlignment.Start);
        AddStep("Center", () => flow.Alignment = FlowAlignment.Center);
        AddStep("End", () => flow.Alignment = FlowAlignment.End);
    }
}
