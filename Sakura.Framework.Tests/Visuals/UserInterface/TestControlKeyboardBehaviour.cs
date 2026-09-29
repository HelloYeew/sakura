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

namespace Sakura.Framework.Tests.Visuals.UserInterface;

/// <summary>
/// The keyboard behaviour the built-in controls are expected to have out of the box: dropdown
/// navigation, Escape-to-dismiss, scroll-to-focus, and colour picking without a pointer.
/// </summary>
public partial class TestControlKeyboardBehaviour : ManualInputManagerTestScene
{
    private void press(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        AddStep($"Press {(modifiers == KeyModifiers.None ? "" : modifiers + "+")}{key}", () =>
        {
            InputManager.PressKey(key, modifiers);
            InputManager.ReleaseKey(key, modifiers);
        });

    #region Dropdown

    private BasicDropdown<string> dropdown = null!;

    private void createDropdown() =>
        AddStep("Create dropdown", () =>
        {
            TestContent.Clear();
            TestContent.Add(dropdown = new BasicDropdown<string>
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Items = new[] { "Alpha", "Beta", "Gamma" }
            });
        });

    [Test]
    public void TestDropdownOpensOntoCurrentSelection()
    {
        createDropdown();
        AddStep("Select Beta", () => dropdown.Current.Value = "Beta");
        AddStep("Open the menu", () => dropdown.OpenMenu());

        AddAssert("Menu open", () => dropdown.IsMenuOpen);
        AddAssert("Focus landed on Beta", () => dropdown.MenuItems[1].HasFocus);
    }

    [Test]
    public void TestDropdownArrowsWalkItems()
    {
        createDropdown();
        AddStep("Open the menu", () => dropdown.OpenMenu());
        AddAssert("First item focused", () => dropdown.MenuItems[0].HasFocus);

        press(Key.Down);
        AddAssert("Second item focused", () => dropdown.MenuItems[1].HasFocus);

        press(Key.Down);
        AddAssert("Third item focused", () => dropdown.MenuItems[2].HasFocus);

        press(Key.Down);
        AddAssert("Wraps to the first", () => dropdown.MenuItems[0].HasFocus);

        press(Key.Up);
        AddAssert("Wraps back to the last", () => dropdown.MenuItems[2].HasFocus);
    }

    [Test]
    public void TestDropdownEnterSelectsFocusedItem()
    {
        createDropdown();
        AddStep("Open the menu", () => dropdown.OpenMenu());
        press(Key.Down);
        press(Key.Enter);

        AddAssert("Value committed", () => dropdown.Current.Value == "Beta");
        AddAssert("Menu closed", () => !dropdown.IsMenuOpen);
        AddAssert("Focus returned to the header", () => dropdown.Header.HasFocus);
    }

    [Test]
    public void TestDropdownEscapeClosesWithoutSelecting()
    {
        createDropdown();
        AddStep("Select Alpha", () => dropdown.Current.Value = "Alpha");
        AddStep("Open the menu", () => dropdown.OpenMenu());
        press(Key.Down);
        press(Key.Escape);

        AddAssert("Menu closed", () => !dropdown.IsMenuOpen);
        AddAssert("Value unchanged", () => dropdown.Current.Value == "Alpha");
        AddAssert("Focus returned to the header", () => dropdown.Header.HasFocus);
    }

    [Test]
    public void TestDropdownTrapsTabWhileOpen()
    {
        AddStep("Create a dropdown next to a button", () =>
        {
            TestContent.Clear();
            TestContent.Add(new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    dropdown = new BasicDropdown<string> { Items = new[] { "Alpha", "Beta" } },
                    outsideButton = new BasicButton { Size = new Vector2(120, 30), Text = "Outside", Action = () => { } }
                }
            });
        });

        AddStep("Open the menu", () => dropdown.OpenMenu());
        AddAssert("Dropdown is trapping", () => dropdown.TrapsTabTraversal);

        press(Key.Tab);
        press(Key.Tab);
        press(Key.Tab);
        AddAssert("Tab never escaped to the outside button", () => !outsideButton.HasFocus);

        press(Key.Escape);
        AddAssert("No longer trapping once closed", () => !dropdown.TrapsTabTraversal);
    }

    private BasicButton outsideButton = null!;

    [Test]
    public void TestClosedDropdownIsASingleTabStop()
    {
        AddStep("Create a dropdown next to a button", () =>
        {
            TestContent.Clear();
            TestContent.Add(new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    dropdown = new BasicDropdown<string> { Items = new[] { "Alpha", "Beta" } },
                    outsideButton = new BasicButton { Size = new Vector2(120, 30), Text = "Outside", Action = () => { } }
                }
            });
        });

        press(Key.Tab);
        AddAssert("Header focused", () => dropdown.Header.HasFocus);

        // The hidden menu items must not each be their own stop.
        press(Key.Tab);
        AddAssert("Straight on to the outside button", () => outsideButton.HasFocus);
    }

    #endregion

    #region Overlay

    private TestOverlay overlay = null!;

    [Test]
    public void TestEscapeDismissesOverlay()
    {
        AddStep("Show an overlay", () =>
        {
            TestContent.Clear();
            TestContent.Add(overlay = new TestOverlay());
            overlay.Show();
        });
        AddAssert("Overlay visible", () => overlay.State == Visibility.Visible);

        press(Key.Escape);
        AddAssert("Escape dismissed it", () => overlay.State == Visibility.Hidden);
    }

    [Test]
    public void TestOverlayCanOptOutOfEscape()
    {
        AddStep("Show a non-dismissable overlay", () =>
        {
            TestContent.Clear();
            TestContent.Add(overlay = new TestOverlay { AllowEscape = false });
            overlay.Show();
        });

        press(Key.Escape);
        AddAssert("Still visible", () => overlay.State == Visibility.Visible);
    }

    #endregion

    #region Scroll to focus

    [Test]
    public void TestFocusScrollsTargetIntoView()
    {
        ScrollableContainer scroll = null!;
        BasicButton lastButton = null!;

        AddStep("Create a scrolling list taller than its viewport", () =>
        {
            TestContent.Clear();

            var flow = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 4)
            };

            for (int i = 0; i < 20; i++)
            {
                var button = new BasicButton { Size = new Vector2(150, 30), Text = $"Item {i}", Action = () => { } };
                flow.Add(button);
                lastButton = button;
            }

            TestContent.Add(scroll = new ScrollableContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(200, 150),
                Direction = ScrollDirection.Vertical,
                Child = flow
            });
        });

        AddUntilStep("Layout settled", () => scroll.ScrollableExtent.Y > 0);
        AddAssert("Starts scrolled to the top", () => scroll.CurrentScroll.Y <= 0.01f);

        // Driven by Tab, not ChangeFocus: only keyboard traversal scrolls, so that clicking a
        // partly-visible control does not yank the list out from under the cursor.
        AddStep("Tab through the list", () =>
        {
            for (int i = 0; i < 20; i++)
            {
                InputManager.PressKey(Key.Tab);
                InputManager.ReleaseKey(Key.Tab);
            }
        });
        AddUntilStep("Scrolled down to follow focus", () => scroll.CurrentScroll.Y > 0.01f);
        AddAssert("Focus reached the far end", () => lastButton.HasFocus);
    }

    #endregion

    #region Colour picker

    [Test]
    public void TestColorPickerRespondsToArrowKeys()
    {
        BasicColorPicker picker = null!;

        AddStep("Create a picker", () =>
        {
            TestContent.Clear();
            TestContent.Add(picker = new BasicColorPicker
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            });
        });

        AddStep("Focus it and start from mid grey-ish red", () =>
        {
            InputManager.ChangeFocus(picker);
            picker.Current.Value = Color.FromArgb(255, 128, 64, 64);
        });

        Color before = default;
        AddStep("Record the colour", () => before = picker.Current.Value);

        press(Key.Right);
        AddAssert("Right changed the colour", () => picker.Current.Value != before);

        AddStep("Record again", () => before = picker.Current.Value);
        press(Key.Up);
        AddAssert("Up changed the colour", () => picker.Current.Value != before);

        AddStep("Record again", () => before = picker.Current.Value);
        press(Key.Right, KeyModifiers.Shift);
        AddAssert("Shift+Right moved hue", () => picker.Current.Value != before);
    }

    [Test]
    public void TestColorPickerIsReachableByTab()
    {
        BasicColorPicker picker = null!;

        AddStep("Create a picker", () =>
        {
            TestContent.Clear();
            TestContent.Add(picker = new BasicColorPicker { Anchor = Anchor.Centre, Origin = Anchor.Centre });
        });

        press(Key.Tab);
        AddAssert("Picker took focus", () => picker.HasFocus);
    }

    #endregion

    private partial class TestOverlay : FocusedOverlayContainer
    {
        public bool AllowEscape = true;

        public override bool CloseOnEscape => AllowEscape;

        public TestOverlay()
        {
            Size = new Vector2(200);
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            Child = new Box { RelativeSizeAxes = Axes.Both, Color = Color.SlateGray };
        }

        protected override void PopIn() => Alpha = 1;

        protected override void PopOut() => Alpha = 0;
    }
}
