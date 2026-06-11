// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

/// <summary>
/// Visual verification of the targeted invalidation model: auto-size containers react to
/// child geometry changes, flow containers re-layout, and unrelated siblings are untouched
/// when a drawable moves.
/// </summary>
public partial class TestLayoutInvalidation : TestScene
{
    private Container autoSizePanel = null!;
    private Box growingBox = null!;

    private FlowContainer flow = null!;
    private Box flowFirst = null!;
    private Box flowSecond = null!;

    private Box mover = null!;
    private Box sibling = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Build scene", () =>
        {
            Clear();

            // Auto-size panel with a dark background that stretches with it.
            autoSizePanel = new Container
            {
                Position = new Vector2(50, 50),
                AutoSizeAxes = Axes.Both
            };
            autoSizePanel.Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = Color.DarkSlateGray
            });
            autoSizePanel.Add(growingBox = new Box
            {
                Size = new Vector2(60),
                Color = Color.SeaGreen
            });
            Add(autoSizePanel);

            // Horizontal flow of three boxes.
            flow = new FlowContainer
            {
                Position = new Vector2(50, 200),
                Size = new Vector2(500, 80),
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(5, 0)
            };
            flow.Add(flowFirst = new Box { Size = new Vector2(50), Color = Color.SteelBlue });
            flow.Add(flowSecond = new Box { Size = new Vector2(50), Color = Color.DarkOrange });
            flow.Add(new Box { Size = new Vector2(50), Color = Color.MediumPurple });
            Add(flow);

            // A moving box and a static sibling.
            Add(mover = new Box
            {
                Position = new Vector2(50, 350),
                Size = new Vector2(50),
                Color = Color.Crimson
            });
            Add(sibling = new Box
            {
                Position = new Vector2(200, 350),
                Size = new Vector2(50),
                Color = Color.White
            });
        });
    }

    [Test]
    public void TestAutoSizeGrowsWithChild()
    {
        AddStep("Grow child to 140px", () => growingBox.ResizeTo(new Vector2(140, 60), 300));
        AddUntilStep("Panel grew with child", () => autoSizePanel.DrawSize.X >= 139);

        AddStep("Shrink child back", () => growingBox.ResizeTo(new Vector2(60), 300));
        AddUntilStep("Panel shrank with child", () => autoSizePanel.DrawSize.X <= 61);
    }

    [Test]
    public void TestFlowReflowsOnChildResize()
    {
        AddAssert("Second box starts after first", () => flowSecond.Position.X >= 54);

        AddStep("Grow first flow child", () => flowFirst.ResizeTo(new Vector2(120, 50), 300));
        AddUntilStep("Second box shifted right", () => flowSecond.Position.X >= 124);

        AddStep("Shrink first flow child", () => flowFirst.ResizeTo(new Vector2(50), 300));
        AddUntilStep("Second box shifted back", () => flowSecond.Position.X <= 56);
    }

    [Test]
    public void TestSiblingUnaffectedByMovingDrawable()
    {
        // DrawRectangle is in screen space (the scene itself is offset inside the test
        // browser), so compare against a captured snapshot rather than absolute values.
        float siblingX = 0;
        float siblingY = 0;

        AddStep("Capture sibling position", () =>
        {
            siblingX = sibling.DrawRectangle.X;
            siblingY = sibling.DrawRectangle.Y;
        });

        AddStep("Move red box", () => mover.MoveTo(new Vector2(50, 450), 300));
        AddUntilStep("Red box arrived", () => mover.Position.Y >= 449);

        AddAssert("Static sibling never moved", () =>
            System.Math.Abs(sibling.DrawRectangle.X - siblingX) < 0.01f &&
            System.Math.Abs(sibling.DrawRectangle.Y - siblingY) < 0.01f);
    }
}
