// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.Containers;

/// <summary>
/// A scroll container has to locate a drawable nested anywhere beneath it, not only a direct child,
/// and keyboard focus (not clicking) is what moves the scroll.
/// </summary>
public partial class TestScrollIntoViewDepth : ManualInputManagerTestScene
{
    private ScrollableContainer scroll = null!;
    private BasicDropdown<string> dropdown = null!;
    private BasicButton deepButton = null!;
    private BasicButton firstButton = null!;

    /// <summary>
    /// A tall panel: a button, a dropdown, filler, then a deeply-nested button right at the bottom —
    /// the shape of Robin's settings panel.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        AddStep("Create a tall scrolling panel", () =>
        {
            TestContent.Clear();

            var flow = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 6)
            };

            flow.Add(firstButton = new BasicButton { Size = new Vector2(150, 30), Text = "First", Action = () => { } });
            flow.Add(dropdown = new BasicDropdown<string> { Items = new[] { "Alpha", "Beta", "Gamma" } });

            for (int i = 0; i < 15; i++)
                flow.Add(new BasicButton { Size = new Vector2(150, 30), Text = $"Filler {i}", Action = () => { } });

            // Wrapped twice, so its own Position says nothing about where it sits in the panel.
            flow.Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Child = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Child = deepButton = new BasicButton { Size = new Vector2(150, 30), Text = "Deep", Action = () => { } }
                }
            });

            TestContent.Add(scroll = new ScrollableContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(220, 200),
                Direction = ScrollDirection.Vertical,
                Child = flow
            });
        });
    }

    /// <summary>
    /// Waits for layout, because <see cref="ScrollableContainer.ScrollTo"/> clamps against
    /// <see cref="ScrollableContainer.ScrollableExtent"/>, which is still zero on the first frame.
    /// </summary>
    private void waitForLayout() => AddUntilStep("Layout settled", () => scroll.ScrollableExtent.Y > 0);

    [Test]
    public void TestScrollsToDeeplyNestedDrawable()
    {
        waitForLayout();
        AddAssert("Starts at the top", () => scroll.CurrentScroll.Y <= 0.01f);

        AddStep("Scroll the deep button into view", () => scroll.ScrollIntoView(deepButton));
        AddUntilStep("Scrolled well down", () => scroll.CurrentScroll.Y > 100);
    }

    [Test]
    public void TestScrollingToADrawableOutsideTheContainerDoesNothing()
    {
        BasicButton outsider = null!;
        AddStep("Add a button outside the panel", () =>
            TestContent.Add(outsider = new BasicButton { Size = new Vector2(100, 30), Text = "Outside", Action = () => { } }));

        waitForLayout();
        AddStep("Scroll to the bottom", () => scroll.ScrollToEnd(false));
        float before = 0;
        AddStep("Record scroll", () => before = scroll.CurrentScroll.Y);

        AddStep("Ask it to scroll to the outsider", () => scroll.ScrollIntoView(outsider));
        AddAssert("Scroll untouched", () => Precision.AlmostEquals(scroll.CurrentScroll.Y, before, 0.5f));
    }

    /// <summary>
    /// The regression: a dropdown menu item's Position is relative to the dropdown's own menu, so
    /// feeding it to the enclosing panel used to scroll that panel to the top.
    /// </summary>
    [Test]
    public void TestOpeningDropdownDoesNotResetPanelScroll()
    {
        waitForLayout();
        AddStep("Scroll down a little", () => scroll.ScrollTo(new Vector2(0, 60), false));
        AddUntilStep("Settled", () => scroll.CurrentScroll.Y > 50);

        float before = 0;
        AddStep("Record scroll", () => before = scroll.CurrentScroll.Y);

        AddStep("Open the dropdown", () => dropdown.OpenMenu());
        AddAssert("Menu open", () => dropdown.IsMenuOpen);
        AddAssert("Panel did not jump to the top",
            () => scroll.CurrentScroll.Y > before - 5f);
    }

    [Test]
    public void TestArrowingThroughDropdownDoesNotResetPanelScroll()
    {
        waitForLayout();
        AddStep("Scroll down a little", () => scroll.ScrollTo(new Vector2(0, 60), false));
        AddUntilStep("Settled", () => scroll.CurrentScroll.Y > 50);
        AddStep("Open the dropdown", () => dropdown.OpenMenu());

        float before = 0;
        AddStep("Record scroll", () => before = scroll.CurrentScroll.Y);

        AddStep("Arrow down twice", () =>
        {
            InputManager.PressKey(Key.Down);
            InputManager.ReleaseKey(Key.Down);
            InputManager.PressKey(Key.Down);
            InputManager.ReleaseKey(Key.Down);
        });

        AddAssert("Panel still where it was", () => scroll.CurrentScroll.Y > before - 5f);
    }

    [Test]
    public void TestClickingDoesNotScroll()
    {
        waitForLayout();
        AddStep("Scroll down a little", () => scroll.ScrollTo(new Vector2(0, 60), false));
        AddUntilStep("Settled", () => scroll.CurrentScroll.Y > 50);

        float before = 0;
        AddStep("Record scroll", () => before = scroll.CurrentScroll.Y);

        AddStep("Click a visible button", () =>
        {
            InputManager.MoveMouseTo(scroll);
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("Clicking never moved the scroll", () => Precision.AlmostEquals(scroll.CurrentScroll.Y, before, 2f));
    }

    [Test]
    public void TestTabbingScrollsTheFocusedControlIntoView()
    {
        waitForLayout();
        AddStep("Focus the first button", () => InputManager.ChangeFocus(firstButton));
        AddAssert("Still at the top", () => scroll.CurrentScroll.Y <= 0.01f);

        // Ten stops in, far enough down the panel to need scrolling but short of wrapping around.
        AddStep("Tab ten stops down the panel", () =>
        {
            for (int i = 0; i < 10; i++)
            {
                InputManager.PressKey(Key.Tab);
                InputManager.ReleaseKey(Key.Tab);
            }
        });

        AddAssert("Focus moved off the first button", () => !firstButton.HasFocus);
        AddUntilStep("Panel scrolled to follow focus", () => scroll.CurrentScroll.Y > 50);
    }
}
