// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.UserInterface;

public partial class TestColorPicker : ManualInputManagerTestScene
{
    private BasicColorPicker picker = null!;
    private SpriteText stateText = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add picker", () =>
        {
            TestContent.Add(stateText = new SpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Margin = new MarginPadding(8),
                Text = "Current: (none)",
                Color = Color.White
            });

            picker = new BasicColorPicker
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            };

            picker.Current.ValueChanged += e => stateText.Text = $"Current: {e.NewValue.ToHex()}";

            TestContent.Add(picker);
        });

        AddUntilStep("wait for layout", () => picker.HueSlider.DrawRectangle.Width > 0);
    }

    private void clickAt(Drawable target, float fx, float fy)
    {
        var rect = target.DrawRectangle;
        InputManager.MoveMouseTo(new Vector2(rect.X + fx * rect.Width, rect.Y + fy * rect.Height));
        InputManager.Click(MouseButton.Left);
    }

    [Test]
    public void TestDragSaturationValue()
    {
        // Start pinned to white (top-left of the square = zero saturation, full value).
        AddStep("Reset to white", () => picker.Current.Value = Color.White);
        AddAssert("Is white", () => picker.Current.Value == Color.White);

        // Click the centre of the square: saturation ~0.5, value ~0.5.
        AddStep("Click square centre", () => clickAt(picker.SaturationValueArea, 0.5f, 0.5f));
        AddAssert("No longer white", () => picker.Current.Value != Color.White);
        AddAssert("Value darkened below full", () =>
        {
            ColorExtensions.ToHSV(picker.Current.Value, out _, out _, out float v);
            return v < 0.95f;
        });
    }

    [Test]
    public void TestDragHue()
    {
        // Pick a saturated, bright color first so hue is meaningful.
        AddStep("Pick a saturated color", () => clickAt(picker.SaturationValueArea, 0.9f, 0.1f));

        AddStep("Click hue far-left (red)", () => clickAt(picker.HueSlider, 0.02f, 0.5f));
        AddAssert("Hue near red", () =>
        {
            ColorExtensions.ToHSV(picker.Current.Value, out float h, out _, out _);
            return h < 0.1f || h > 0.9f;
        });

        AddStep("Click hue middle (cyan)", () => clickAt(picker.HueSlider, 0.5f, 0.5f));
        AddAssert("Hue near cyan (0.5)", () =>
        {
            ColorExtensions.ToHSV(picker.Current.Value, out float h, out _, out _);
            return h > 0.4f && h < 0.6f;
        });
    }

    [Test]
    public void TestDragUpdatesContinuously()
    {
        AddStep("Choose saturated color", () => clickAt(picker.SaturationValueArea, 0.9f, 0.1f));

        Color afterLeft = default;
        Color afterRight = default;

        // Drag across the hue bar, the color must change while dragging (not just on release).
        AddStep("Drag hue left→right", () =>
        {
            var rect = picker.HueSlider.DrawRectangle;
            float y = rect.Y + rect.Height / 2f;
            InputManager.MoveMouseTo(new Vector2(rect.X + rect.Width * 0.05f, y));
            InputManager.PressButton(MouseButton.Left);
            InputManager.MoveMouseTo(new Vector2(rect.X + rect.Width * 0.2f, y));
            afterLeft = picker.Current.Value;
            InputManager.MoveMouseTo(new Vector2(rect.X + rect.Width * 0.8f, y));
            afterRight = picker.Current.Value;
            InputManager.ReleaseButton(MouseButton.Left);
        });

        AddAssert("color changed mid-drag", () => afterLeft != afterRight);
        AddAssert("Hue tracked toward the right", () =>
        {
            ColorExtensions.ToHSV(afterLeft, out float hl, out _, out _);
            ColorExtensions.ToHSV(afterRight, out float hr, out _, out _);
            return hr > hl;
        });
    }

    [Test]
    public void TestInteractingReleasesHexFocus()
    {
        // Focus the hex field, then interact with the square: focus must drop and the hex text must
        // reflect the new color immediately (even on a single click, no drag).
        AddStep("Focus hex input", () =>
        {
            InputManager.MoveMouseTo(picker.HexInput);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Hex focused", () => picker.HexInput!.HasFocus);

        AddStep("Single click square", () => clickAt(picker.SaturationValueArea, 0.6f, 0.4f));
        AddAssert("Hex no longer focused", () => !picker.HexInput!.HasFocus);
        AddAssert("Hex text matches color", () => picker.HexInput!.Text.Value == picker.Current.Value.ToHex());
    }

    [Test]
    public void TestExternalValue()
    {
        AddStep("Set to blue", () => picker.Current.Value = Color.Blue);
        AddAssert("Current is blue", () => picker.Current.Value == Color.Blue);

        AddStep("Set to green", () => picker.Current.Value = Color.Green);
        AddAssert("Current is green", () => picker.Current.Value == Color.Green);
    }

    [Test]
    public void TestHexInputUpdatesLive()
    {
        AddStep("Focus hex input + clear", () =>
        {
            InputManager.MoveMouseTo(picker.HexInput);
            InputManager.Click(MouseButton.Left);
            picker.HexInput.Text.Value = "";
        });

        // Type a full hex, the color should track the moment the value becomes valid.
        AddStep("Type 00FF00", () => InputManager.TypeText("00FF00"));
        AddAssert("Current is green", () => picker.Current.Value == ColorExtensions.FromHex("00FF00"));
    }

    [Test]
    public void TestHexInputLengthLimited()
    {
        AddAssert("Length limit is 9", () => picker.HexInput.LengthLimit == 9);

        AddStep("Focus + clear", () =>
        {
            InputManager.MoveMouseTo(picker.HexInput);
            InputManager.Click(MouseButton.Left);
            picker.HexInput.Text.Value = "";
        });

        AddStep("Type overly long text", () => InputManager.TypeText("0123456789ABCDEF"));
        AddAssert("Truncated to limit", () => picker.HexInput.Text.Value.Length <= 9);
    }

    [Test]
    public void TestBindExternalReactive()
    {
        var external = new Reactive<Color>(Color.White);

        AddStep("Bind external reactive", () => picker.Current.BindTo(external));

        AddStep("Set external to magenta", () => external.Value = Color.Magenta);
        AddAssert("Picker follows external", () => picker.Current.Value == Color.Magenta);
    }

    [Test]
    public void TestHueThenSaturationKeepsHue()
    {
        // dragging the value/saturation down to black must not lose the chosen hue,
        // because HSV is kept authoritative independently of the (hue-less) black color.
        AddStep("Choose green hue", () => clickAt(picker.HueSlider, 1f / 3f, 0.5f));
        AddStep("Drag square to black corner", () => clickAt(picker.SaturationValueArea, 0f, 1f));

        AddStep("Return square to full color", () => clickAt(picker.SaturationValueArea, 1f, 0f));
        AddAssert("Hue is still green-ish", () =>
        {
            ColorExtensions.ToHSV(picker.Current.Value, out float h, out _, out _);
            return h > 0.25f && h < 0.42f;
        });
    }
}
