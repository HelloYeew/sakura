// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

public class TestScrollableContainer : ManualInputManagerTestScene
{
    private ScrollableContainer scrollContainer;
    private FlowContainer flowContent;
    private readonly List<BasicButton> buttons = new();

    private int lastClickedIndex = -1;

    [SetUp]
    public void SetUp()
    {
        buttons.Clear();
        lastClickedIndex = -1;

        AddStep("Create ScrollableContainer", () =>
        {
            TestContent.Clear();

            TestContent.Add(new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(300, 400), // Same size as the scroll container
                Color = Color.DarkSlateGray,
            });

            scrollContainer = new ScrollableContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(300, 400),
                RelativeSizeAxes = Axes.None,
                Child = flowContent = new FlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 5)
                }
            };

            for (int i = 0; i < 20; i++)
            {
                int index = i;
                var button = new BasicButton
                {
                    Size = new Vector2(280, 50),
                    Text = $"Button {index}",
                    Action = () => lastClickedIndex = index
                };

                buttons.Add(button);
                flowContent.Add(button);
            }

            TestContent.Add(scrollContainer);

            scrollContainer.Add(new Box()
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Size = new Vector2(300, 30),
                Color = Color.Red
            }.RotateTo(90, 1000)
                .Then()
                .RotateTo(0, 1000)
                .Then()
                .RotateTo(-90, 1000)
                .Loop());
        });

        AddWaitStep("Wait for UI to fully load and layout", 1000);
    }

    [Test]
    public void TestProgrammaticScrolling()
    {
        AddStep("Scroll to end immediately", () => scrollContainer.ScrollToEnd(animated: false));
        AddAssert("Scroll Y is greater than 0", () => scrollContainer.CurrentScroll.Y > 0);
        AddAssert("Scroll Y is at maximum", () => scrollContainer.CurrentScroll.Y == scrollContainer.ScrollableExtent.Y);

        AddStep("Scroll to start immediately", () => scrollContainer.ScrollToStart(animated: false));
        AddAssert("Scroll Y is 0", () => scrollContainer.CurrentScroll.Y == 0);

        AddStep("Scroll button 15 into view", () => scrollContainer.ScrollIntoView(buttons[15], animated: false));
        AddAssert("Scroll Y moved to item 15", () => scrollContainer.CurrentScroll.Y > 0);
    }

    [Ignore("Still not work, need to fix mouse move issue")]
    public void TestNormalClick()
    {
        // (We no longer need wait steps here because SetUp handles it!)
        AddStep("Move mouse to button 2", () => InputManager.MoveMouseTo(buttons[2]));
        AddStep("Click", () => InputManager.Click(MouseButton.Left));

        AddAssert("Button 2 was clicked", () => lastClickedIndex == 2);
        AddAssert("Scroll position did not change", () => scrollContainer.CurrentScroll.Y == 0);
    }

    [Ignore("Still not work, need to fix mouse move issue")]
    public void TestDragStealing()
    {
        AddStep("Reset scroll", () => scrollContainer.ScrollToStart(animated: false));
        AddStep("Reset click tracker", () => lastClickedIndex = -1);

        AddStep("Drag from button 0 to button 5", () => InputManager.Drag(buttons[0], buttons[5], MouseButton.Left));

        AddAssert("Button 0 was NOT clicked", () => lastClickedIndex == -1);
        AddAssert("Container scrolled down", () => scrollContainer.CurrentScroll.Y > 0);
    }

    [Ignore("Still not work, need to fix mouse move issue")]
    public void TestBoundsClamping()
    {
        AddStep("Force scroll way past start", () => scrollContainer.ScrollTo(new Vector2(0, -500), animated: false));

        AddUntilStep("Wait for rubber-band to snap back to 0", () => scrollContainer.CurrentScroll.Y == 0);

        AddAssert("Scroll Y clamped to 0", () => scrollContainer.CurrentScroll.Y == 0);
    }
}
