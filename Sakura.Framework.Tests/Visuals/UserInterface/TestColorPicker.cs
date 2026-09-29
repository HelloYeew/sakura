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

    /// <summary>
    /// A marker at the edge of the square or the end of the hue bar is drawn whole. Both used to sit
    /// inside their masked area, so pure red — full saturation and value, at the square's top-right
    /// corner and the hue bar's left end — showed a quarter of one marker and half of the other. Every
    /// fully bright colour sits on the square's top edge.
    /// </summary>
    [Test]
    public void TestMarkersAreNotClippedByTheirArea()
    {
        MarkerProbePicker probe = null!;

        AddStep("Add a rounded probe picker", () =>
        {
            TestContent.Clear();
            TestContent.Add(probe = new MarkerProbePicker
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                SaturationValueCornerRadius = 8,
                HueBarCornerRadius = 12,
            });
        });
        AddUntilStep("Laid out", () => probe.HueSlider.DrawRectangle.Width > 0);
        AddStep("Pure red", () => probe.Current.Value = Color.Red);

        AddAssert("Nothing masks the square's marker", () => !maskedBetween(probe.SquareMarker, probe));
        AddAssert("Nothing masks the hue marker", () => !maskedBetween(probe.HueMarker, probe));

        // Still rounded: the gradients are clipped, only the markers are not.
        AddAssert("The square still masks its gradients", () =>
            probe.SaturationValueArea is Container { Masking: true, CornerRadius: 8 });
        AddAssert("And so does the hue bar", () =>
            probe.HueSlider is Container { Masking: true, CornerRadius: 12 });
    }

    /// <summary>
    /// An initializer value for <see cref="ColorPicker.Spacing"/> is used. The layout was built from it
    /// in the constructor, which runs before initializers are assigned, so it was silently ignored.
    /// </summary>
    [Test]
    public void TestSpacingFromAnInitialiserIsUsed()
    {
        BasicColorPicker spaced = null!;

        AddStep("Add a picker spaced at 30", () =>
        {
            TestContent.Clear();
            TestContent.Add(spaced = new BasicColorPicker { Spacing = 30 });
        });
        AddUntilStep("Laid out", () => spaced.HueSlider.DrawRectangle.Width > 0);

        AddAssert("30 between the square and the hue bar", () =>
        {
            float squareBottom = spaced.SaturationValueArea.ToScreenSpace(new Vector2(0, spaced.SaturationValueArea.DrawHeight)).Y;
            float hueTop = spaced.HueSlider.ToScreenSpace(Vector2.Zero).Y;
            return System.Math.Abs(hueTop - squareBottom - 30) < 0.5f;
        });
    }

    /// <summary>
    /// A picker relatively sized on X fills its container's width, square, hue bar and hex row alike,
    /// and follows the container when it resizes. A fixed width was all a picker could have, and one
    /// placed in a scroll area that insets its content for the scrollbar overflowed it and was clipped.
    /// </summary>
    [Test]
    public void TestFillsItsWidthWhenRelativelySized()
    {
        Container frame = null!;
        BasicColorPicker filling = null!;

        AddStep("Add a filling picker in a 260-wide frame", () =>
        {
            TestContent.Clear();
            TestContent.Add(frame = new Container
            {
                Width = 260,
                AutoSizeAxes = Axes.Y,
                Child = filling = new BasicColorPicker
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
            });
        });

        AddUntilStep("The square fills it", () => System.Math.Abs(filling.SaturationValueArea.DrawWidth - 260) < 0.5f);
        AddAssert("So does the hue bar", () => System.Math.Abs(filling.HueSlider.DrawWidth - 260) < 0.5f);
        AddAssert("And the hex row ends where the square does", () => rightOf(filling.HexInput!.Parent!) <= rightOf(filling.SaturationValueArea) + 0.5f);

        AddStep("Narrow the frame to 200", () => frame.Width = 200);
        AddUntilStep("The square follows", () => System.Math.Abs(filling.SaturationValueArea.DrawWidth - 200) < 0.5f);
        AddAssert("And the hex row still fits", () => rightOf(filling.HexInput!.Parent!) <= rightOf(filling.SaturationValueArea) + 0.5f);
    }

    private static float rightOf(Drawable d) => d.ToScreenSpace(new Vector2(d.DrawWidth, 0)).X;

    private static bool maskedBetween(Drawable drawable, Drawable root)
    {
        for (var parent = drawable.Parent; parent != null && parent != root; parent = parent.Parent)
        {
            if (parent is Container { Masking: true })
                return true;
        }

        return false;
    }

    /// <summary>
    /// Keeps hold of the markers it creates, which the picker otherwise keeps private.
    /// </summary>
    private partial class MarkerProbePicker : BasicColorPicker
    {
        public Drawable SquareMarker { get; private set; } = null!;
        public Drawable HueMarker { get; private set; } = null!;

        protected override Drawable CreateSaturationValueMarker() => SquareMarker = base.CreateSaturationValueMarker();

        protected override Drawable CreateHueMarker() => HueMarker = base.CreateHueMarker();
    }
}
