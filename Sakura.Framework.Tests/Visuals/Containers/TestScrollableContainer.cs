// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
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

            // 1. Add the background to the TestContent instead!
            TestContent.Add(new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(300, 400), // Same size as the scroll container
                Color = Color.DarkSlateGray,
            });

            // 2. Create the scroll container normally
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

    // TODO: Manual input test for user interaction
}
