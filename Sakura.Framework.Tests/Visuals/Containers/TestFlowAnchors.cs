// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

/// <summary>
/// Flows keeping their children's anchors: a centred and a right-aligned column, a row whose children
/// centre vertically in it, and a wrapping flow aligning each line on its own. The pulsing box scales
/// about its centre, not its corner.
/// </summary>
public partial class TestFlowAnchors : TestScene
{
    private FlowContainer column = null!;
    private FlowContainer row = null!;
    private FlowContainer wrapping = null!;

    private Box columnCentred = null!;
    private Box columnRight = null!;
    private Box rowCentred = null!;
    private Box pulse = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Build flows", () =>
        {
            Clear();

            Add(frame(new Vector2(40, 40), column = new FlowContainer
            {
                Direction = FlowDirection.Vertical,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(0, 8),
                Children = new Drawable[]
                {
                    box(200, 30, Anchor.TopLeft, Color.SteelBlue),
                    columnCentred = box(90, 30, Anchor.TopCentre, Color.SeaGreen),
                    columnRight = box(60, 30, Anchor.TopRight, Color.DarkOrange),
                    pulse = new Box { Size = new Vector2(50, 30), Anchor = Anchor.TopCentre, Origin = Anchor.Centre, Color = Color.HotPink }
                }
            }));

            Add(frame(new Vector2(320, 40), row = new FlowContainer
            {
                Direction = FlowDirection.Horizontal,
                Size = new Vector2(360, 100),
                Spacing = new Vector2(10, 0),
                Children = new Drawable[]
                {
                    box(60, 30, Anchor.TopLeft, Color.SteelBlue),
                    rowCentred = box(60, 30, Anchor.CentreLeft, Color.SeaGreen),
                    box(60, 30, Anchor.BottomLeft, Color.DarkOrange)
                }
            }));

            Add(frame(new Vector2(40, 300), wrapping = new FlowContainer
            {
                Direction = FlowDirection.Horizontal,
                Size = new Vector2(260, 220),
                Spacing = new Vector2(6),
                Alignment = FlowAlignment.Center,
                Children = new Drawable[]
                {
                    box(80, 60, Anchor.TopLeft, Color.SteelBlue),
                    box(80, 20, Anchor.CentreLeft, Color.SeaGreen),
                    box(80, 20, Anchor.BottomLeft, Color.DarkOrange),
                    box(120, 40, Anchor.TopLeft, Color.MediumPurple),
                    box(60, 10, Anchor.CentreLeft, Color.Crimson)
                }
            }));

            pulse.Loop(d => d.ScaleTo(1.6f, 400, Easing.OutQuint).Then().ScaleTo(1f, 400, Easing.InQuint), 800);
        });
    }

    /// <summary>A flow on a faint backdrop, so the room it aligns within shows.</summary>
    private static Container frame(Vector2 position, FlowContainer flow) => new Container
    {
        Position = position,
        AutoSizeAxes = Axes.Both,
        Children = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Color = Color.DarkSlateGray },
            flow
        }
    };

    private static Box box(float width, float height, Anchor anchor, Color color) => new Box
    {
        Size = new Vector2(width, height),
        Anchor = anchor,
        Origin = anchor,
        Color = color
    };

    private static float offsetX(Drawable child, Drawable flow) => child.DrawRectangle.X - flow.DrawRectangle.X;
    private static float offsetY(Drawable child, Drawable flow) => child.DrawRectangle.Y - flow.DrawRectangle.Y;
    private static bool near(float a, float b) => Math.Abs(a - b) < 0.5f;

    [Test]
    public void TestAlignment()
    {
        AddAssert("Column: centred under the widest", () => near(offsetX(columnCentred, column), 55));
        AddAssert("Column: flush right", () => near(offsetX(columnRight, column), 140));
        AddAssert("Row: centred in the row's height", () => near(offsetY(rowCentred, row), 35));
        AddAssert("Anchors weren't reset", () => columnCentred.Anchor == Anchor.TopCentre && rowCentred.Anchor == Anchor.CentreLeft);
    }

    [Test]
    public void TestChangesRealign()
    {
        AddStep("Widen the centred box", () => columnCentred.Width = 150);
        AddAssert("Still centred", () => near(offsetX(columnCentred, column), 25));

        AddStep("Make the row taller", () => row.Height = 160);
        AddAssert("Re-centred in the new height", () => near(offsetY(rowCentred, row), 65));

        AddStep("Align the wrapping lines to the end", () => wrapping.Alignment = FlowAlignment.End);
        AddStep("Narrow the wrapping flow", () => wrapping.Width = 200);
    }
}
