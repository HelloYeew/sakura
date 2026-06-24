// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.Containers;

public partial class TestScrollableContainer : ManualInputManagerTestScene
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
                Size = new Vector2(300, 400),
                Color = Color.DarkSlateGray
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

    [Test]
    public void TestWheelScrollMovesContent()
    {
        AddAssert("Starts at top", () => scrollContainer.CurrentScroll.Y == 0);
        AddAssert("Has room to scroll", () => scrollContainer.ScrollableExtent.Y > 0);

        AddStep("Move cursor over the container", () => InputManager.MoveMouseTo(scrollContainer));
        AddStep("Scroll wheel down", () => InputManager.ScrollBy(new Vector2(0, -1)));
        AddUntilStep("Scrolled down", () => scrollContainer.CurrentScroll.Y > 0);

        AddStep("Scroll wheel up repeatedly", () =>
        {
            for (int i = 0; i < 10; i++)
                InputManager.ScrollBy(new Vector2(0, 1));
        });
        AddUntilStep("Returned to top", () => Precision.AlmostEquals(scrollContainer.CurrentScroll.Y, 0f, 0.5f));
    }

    [Test]
    public void TestAnimatedProgrammaticScrolling()
    {
        AddStep("Animate scroll to end", () => scrollContainer.ScrollToEnd());
        AddUntilStep("Reaches the bottom", () =>
            Precision.AlmostEquals(scrollContainer.CurrentScroll.Y, scrollContainer.ScrollableExtent.Y, 0.5f));

        AddStep("Animate scroll to start", () => scrollContainer.ScrollToStart());
        AddUntilStep("Reaches the top", () => Precision.AlmostEquals(scrollContainer.CurrentScroll.Y, 0f, 0.5f));
    }

    [Test]
    public void TestHoverFollowsScrolledContent()
    {
        BasicButton hoveredBefore = null!;

        AddStep("Hover the centre of the container", () => InputManager.MoveMouseTo(scrollContainer));
        AddUntilStep("A button is hovered", () => buttons.Exists(b => b.IsHovered));
        AddStep("Record which button is hovered", () => hoveredBefore = buttons.Find(b => b.IsHovered)!);

        // Scroll without moving the cursor: a different button is now physically under it.
        AddStep("Scroll down a few ticks", () =>
        {
            for (int i = 0; i < 3; i++)
                InputManager.ScrollBy(new Vector2(0, -1));
        });
        AddUntilStep("Content has scrolled", () => scrollContainer.CurrentScroll.Y > 0);

        AddAssert("Exactly one button is hovered", () => buttons.FindAll(b => b.IsHovered).Count == 1);
        AddAssert("The hovered button changed", () => !ReferenceEquals(buttons.Find(b => b.IsHovered), hoveredBefore));
        AddAssert("The previously hovered button is no longer hovered", () => !hoveredBefore.IsHovered);
    }

    [Test]
    public void TestWheelScrollClampsAtExtent()
    {
        AddStep("Move cursor over the container", () => InputManager.MoveMouseTo(scrollContainer));
        AddStep("Scroll far past the bottom", () =>
        {
            for (int i = 0; i < 50; i++)
                InputManager.ScrollBy(new Vector2(0, -1));
        });
        AddUntilStep("Settles at the maximum extent", () =>
            Precision.AlmostEquals(scrollContainer.CurrentScroll.Y, scrollContainer.ScrollableExtent.Y, 0.5f));
    }

    [Test]
    public void TestClickThroughToButton()
    {
        AddStep("Scroll to start", () => scrollContainer.ScrollToStart(animated: false));
        AddWaitStep("Let layout settle", 100);

        AddStep("Click the top of the viewport", () =>
        {
            var rect = scrollContainer.DrawRectangle;
            InputManager.MoveMouseTo(new Vector2(rect.Center.X, rect.Y + 25));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("First button received the click", () => lastClickedIndex == 0);
    }

    [Test]
    public void TestScrolledButtonIsClickable()
    {
        AddStep("Scroll to end", () => scrollContainer.ScrollToEnd(animated: false));
        AddWaitStep("Let layout settle", 100);

        AddStep("Click the bottom of the viewport", () =>
        {
            var rect = scrollContainer.DrawRectangle;
            InputManager.MoveMouseTo(new Vector2(rect.Center.X, rect.Y + rect.Height - 25));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Last button received the click", () => lastClickedIndex == buttons.Count - 1);
    }
}
