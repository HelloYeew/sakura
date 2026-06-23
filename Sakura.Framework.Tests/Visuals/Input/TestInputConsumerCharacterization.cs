// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

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

namespace Sakura.Framework.Tests.Visuals.Input;

public partial class TestInputConsumerCharacterization : ManualInputManagerTestScene
{
    private BasicTextBox textBox = null!;
    private BasicSliderBar<float> slider = null!;
    private RecordingScrollableContainer scroll = null!;
    private FlowContainer scrollContent = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Lay out the three consumers", () =>
        {
            TestContent.Clear();

            TestContent.Add(textBox = new BasicTextBox
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Position = new Vector2(0, 20),
                Width = 300,
                Height = 40
            });

            TestContent.Add(slider = new BasicSliderBar<float>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(200, 20),
                MinValue = 0,
                MaxValue = 100
            });

            scroll = new RecordingScrollableContainer
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Position = new Vector2(0, -20),
                Size = new Vector2(200, 150),
                RelativeSizeAxes = Axes.None,
                Child = scrollContent = new FlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 5)
                }
            };

            for (int i = 0; i < 20; i++)
            {
                scrollContent.Add(new Box
                {
                    Size = new Vector2(180, 40),
                    Color = Color.SteelBlue
                });
            }

            TestContent.Add(scroll);
        });

        AddWaitStep("Wait for layout", 200);
    }

    [Test]
    public void TestTextBoxClickFocusAndType()
    {
        AddAssert("Textbox empty", () => textBox.Text.Value == "");
        AddStep("Focus textbox", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Textbox focused", () => textBox.HasFocus);

        AddStep("Type", () => InputManager.TypeText("Sakura"));
        AddAssert("Text captured", () => textBox.Text.Value == "Sakura");

        AddStep("Backspace", () => InputManager.PressKey(Key.BackSpace));
        AddAssert("Last char removed", () => textBox.Text.Value == "Sakur");
    }

    [Test]
    public void TestSliderClickAndDrag()
    {
        AddStep("Click slider centre", () =>
        {
            InputManager.MoveMouseTo(slider);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Value ~50", () => Precision.AlmostEquals(slider.Current.Value, 50f, 2f));

        AddStep("Drag to start", () =>
            InputManager.Drag(
                new Vector2(slider.DrawRectangle.Center.X, slider.DrawRectangle.Center.Y),
                new Vector2(slider.DrawRectangle.X, slider.DrawRectangle.Center.Y)));
        AddAssert("Value at min", () => slider.Current.Value == slider.MinValue);
    }

    [Test]
    public void TestScrollOverContainerScrolls()
    {
        AddAssert("Starts at top", () => scroll.CurrentScroll.Y == 0);
        AddAssert("Has room to scroll", () => scroll.ScrollableExtent.Y > 0);

        AddStep("Move cursor over the container", () => InputManager.MoveMouseTo(scroll));
        AddStep("Reset scroll receipt", () => scroll.ScrollCount = 0);
        AddStep("Scroll the wheel down", () => InputManager.ScrollBy(new Vector2(0, -1)));

        AddAssert("Container received the scroll", () => scroll.ScrollCount == 1);
        AddUntilStep("Scrolled down", () => scroll.CurrentScroll.Y > 0);
    }

    [Test]
    public void TestScrollAwayFromContainerDoesNotScrollIt()
    {
        AddStep("Move cursor onto the textbox (away from scroll area)", () => InputManager.MoveMouseTo(textBox));
        AddStep("Reset scroll receipt", () => scroll.ScrollCount = 0);
        AddStep("Scroll", () => InputManager.ScrollBy(new Vector2(0, -1)));

        AddAssert("Container did not receive the scroll", () => scroll.ScrollCount == 0);
        AddAssert("Scroll container unaffected", () => scroll.CurrentScroll.Y == 0);
    }

    /// <summary>
    /// A <see cref="ScrollableContainer"/> that records scroll receipt so positional routing can be
    /// asserted deterministically, without depending on momentum animation timing.
    /// </summary>
    private partial class RecordingScrollableContainer : ScrollableContainer
    {
        public int ScrollCount;

        public override bool OnScroll(ScrollEvent e)
        {
            ScrollCount++;
            return base.OnScroll(e);
        }
    }
}
