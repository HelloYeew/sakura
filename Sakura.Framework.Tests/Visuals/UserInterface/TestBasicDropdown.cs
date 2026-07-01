// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.UserInterface;

public partial class TestBasicDropdown : ManualInputManagerTestScene
{
    private BasicDropdown<string> dropdown;
    private SpriteText selectedText;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add dropdown and status text", () =>
        {
            TestContent.Add(selectedText = new SpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Text = "Selected: None",
                Color = Color.White,
                Size = new Vector2(200, 30),
            });

            TestContent.Add(dropdown = new BasicDropdown<string>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.TopCentre,
                Items = new[]
                {
                    "A",
                    "B",
                    "C"
                }
            });

            dropdown.Current.ValueChanged += e => selectedText.Text = $"Selected: {e.NewValue}";
        });
    }

    [Test]
    public void TestMenuToggleAndSelection()
    {
        AddAssert("No value selected initially", () => dropdown.Current.Value == null);

        AddStep("Move to dropdown header", () => InputManager.MoveMouseTo(dropdown.Header));
        AddStep("Click dropdown to open", () => InputManager.Click(MouseButton.Left));
        AddWaitStep("Wait a bit", 50);

        AddStep("Move to B item", () => InputManager.MoveMouseTo(dropdown.MenuItems[1]));
        AddStep("Click B", () => InputManager.Click(MouseButton.Left));

        AddAssert("B was selected", () => dropdown.Current.Value == "B");
    }

    [Test]
    public void TestChangeItems()
    {
        AddStep("Change items", () => dropdown.Items = new[] { "X", "Y", "Z" });

        AddStep("Move to dropdown header", () => InputManager.MoveMouseTo(dropdown.Header));
        AddStep("Click dropdown to open", () => InputManager.Click(MouseButton.Left));

        AddStep("Move to Y item", () => InputManager.MoveMouseTo(dropdown.MenuItems[1]));
        AddStep("Click Y", () => InputManager.Click(MouseButton.Left));

        AddAssert("Y was selected", () => dropdown.Current.Value == "Y");
    }

    [Test]
    public void TestChangeItemsDuringOpen()
    {
        AddStep("Move to dropdown header", () => InputManager.MoveMouseTo(dropdown.Header));
        AddStep("Click dropdown to open", () => InputManager.Click(MouseButton.Left));

        AddStep("Change items while open", () => dropdown.Items = new[]
        {
            "X", "Y", "Z"
        });

        AddStep("Move to Y item", () => InputManager.MoveMouseTo(dropdown.MenuItems[1]));
        AddStep("Click Y", () => InputManager.Click(MouseButton.Left));

        AddAssert("Y was selected", () => dropdown.Current.Value == "Y");
        AddStep("Move to dropdown header", () => InputManager.MoveMouseTo(dropdown.Header));
        AddStep("Click dropdown to open again", () => InputManager.Click(MouseButton.Left));
        AddStep("Add more items", () => dropdown.Items = new[]
        {
            "X", "Y", "Z", "W"
        });
        AddStep("Move to W item", () => InputManager.MoveMouseTo(dropdown.MenuItems[3]));
        AddStep("Click W", () => InputManager.Click(MouseButton.Left));

        AddAssert("W was selected", () => dropdown.Current.Value == "W");
    }

    [Test]
    public void TestManyItemsClampHeight()
    {
        AddStep("Add many items", () => dropdown.Items = Enumerable.Range(0, 20).Select(i => $"Item {i}").ToArray());

        AddStep("Move to dropdown header", () => InputManager.MoveMouseTo(dropdown.Header));
        AddStep("Click dropdown to open", () => InputManager.Click(MouseButton.Left));
        AddWaitStep("Wait a bit", 5);

        AddAssert("Menu height is clamped to MaxHeight", () =>
            dropdown.DrawSize.Y <= 30 + dropdown.MaxHeight + 1);
    }

    [Test]
    public void TestScrollToSelectBottomItem()
    {
        AddStep("Add many items", () => dropdown.Items = Enumerable.Range(0, 20).Select(i => $"Item {i}").ToArray());

        AddStep("Move to dropdown header", () => InputManager.MoveMouseTo(dropdown.Header));
        AddStep("Click dropdown to open", () => InputManager.Click(MouseButton.Left));
        AddWaitStep("Wait a bit", 5);

        AddStep("Scroll last item into view", () => dropdown.ScrollItemIntoView(19));
        AddWaitStep("Wait for scroll to settle", 100);

        AddStep("Move to last item", () => InputManager.MoveMouseTo(dropdown.MenuItems[19]));
        AddStep("Click last item", () => InputManager.Click(MouseButton.Left));

        AddAssert("Scrolled-to item was selected", () => dropdown.Current.Value == "Item 19");
    }
}
