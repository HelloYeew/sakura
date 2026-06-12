// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Cursor;

public partial class TestTooltip : ManualInputManagerTestScene
{
    [SetUp]
    public void SetUp()
    {
        AddStep("Clear content", () => TestContent.Clear());
        AddStep("Add tooltip container", () => InputManager.Add(new TooltipContainer()));
    }

    [Test]
    public void TestBasicTooltip()
    {
        AddStep("Add box with tooltip", () =>
        {
            TestContent.Add(new TooltipBox("Hover me!")
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                TooltipText = "Hello, I am a tooltip!",
                Size = new Vector2(160, 60),
                Color = Color.SteelBlue
            });
        });

        AddStep("Move mouse to box", () => InputManager.MoveMouseTo(TestContent.Children[0]));
        AddWaitStep("Wait for tooltip to appear (>220 ms)", 400);
    }

    [Test]
    public void TestMultipleTooltips()
    {
        AddStep("Add several tooltip boxes", () =>
        {
            var flow = new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(20, 0)
            };

            flow.Add(new TooltipBox("Button A")
            {
                TooltipText = "This is Button A",
                Size = new Vector2(120, 50),
                Color = Color.DarkGreen
            });
            flow.Add(new TooltipBox("Button B")
            {
                TooltipText = "This is Button B — with more text!",
                Size = new Vector2(120, 50),
                Color = Color.DarkRed
            });
            flow.Add(new TooltipBox("Button C")
            {
                TooltipText = "Tooltip for C",
                Size = new Vector2(120, 50),
                Color = Color.DarkOrange
            });

            TestContent.Add(flow);
        });

        AddStep("Move to Button A", () => InputManager.MoveMouseTo(
            ((FlowContainer)TestContent.Children[0]).Children[0]));
        AddWaitStep("Wait for tooltip A", 400);

        AddStep("Move to Button B", () => InputManager.MoveMouseTo(
            ((FlowContainer)TestContent.Children[0]).Children[1]));
        AddWaitStep("Wait for tooltip B", 400);

        AddStep("Move to Button C", () => InputManager.MoveMouseTo(
            ((FlowContainer)TestContent.Children[0]).Children[2]));
        AddWaitStep("Wait for tooltip C", 400);
    }

    [Test]
    public void TestNoTooltipWhenTextEmpty()
    {
        TooltipBox box = null!;

        AddStep("Add box with no tooltip text", () =>
        {
            TestContent.Add(box = new TooltipBox("No tooltip here")
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                TooltipText = null,
                Size = new Vector2(160, 60),
                Color = Color.DimGray
            });
        });

        AddStep("Move mouse to box", () => InputManager.MoveMouseTo(box));
        AddWaitStep("wait and no tooltip should appear", 400);
    }

    [Test]
    public void TestTooltipHidesOnLeave()
    {
        TooltipBox box = null!;

        AddStep("Add box", () =>
        {
            TestContent.Add(box = new TooltipBox("Stay or leave?")
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                TooltipText = "I disappear when you leave!",
                Size = new Vector2(180, 60),
                Color = Color.MediumPurple
            });
        });

        AddStep("Hover box", () => InputManager.MoveMouseTo(box));
        AddWaitStep("Wait for tooltip", 400);
        AddStep("Move away", () => InputManager.MoveMouseTo(new Vector2(0, 0)));
        AddWaitStep("Tooltip should hide", 300);
    }

    [Test]
    public void TestNestedTooltip()
    {
        AddStep("Add nested containers", () =>
        {
            var outer = new TooltipBox("Outer")
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                TooltipText = "Outer tooltip",
                Size = new Vector2(200, 120),
                Color = Color.DarkBlue
            };

            var inner = new TooltipBox("Inner")
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                TooltipText = "Inner tooltip (takes priority)",
                Size = new Vector2(100, 50),
                Color = Color.CornflowerBlue
            };

            outer.Add(inner);
            TestContent.Add(outer);
        });

        AddStep("Hover inner box", () =>
        {
            var outer = TestContent.Children[0] as Container;
            InputManager.MoveMouseTo(outer!.Children[0]);
        });
        AddWaitStep("Wait for inner tooltip", 400);
    }

    private partial class TooltipBox : Container, IHasTooltip
    {
        public string? TooltipText { get; set; }

        private readonly Box background;
        private readonly SpriteText label;

        public Color Color
        {
            get => background.Color;
            set => background.Color = value;
        }

        public TooltipBox(string text)
        {
            Masking = true;
            CornerRadius = 6;

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(1)
                },
                label = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = text,
                    Font = FontUsage.Default.With(size: 15),
                    Color = Color.White
                }
            };
        }
    }
}
