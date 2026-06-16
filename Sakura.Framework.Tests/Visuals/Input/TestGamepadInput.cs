// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Input;

public partial class TestGamepadInput : ManualInputManagerTestScene
{
    private GamepadVisualiser visualiser;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add gamepad visualiser", () =>
        {
            TestContent.Clear();
            TestContent.Add(visualiser = new GamepadVisualiser
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            });
        });
    }

    [Test]
    public void TestGamepadConnectAndDisconnect()
    {
        AddStep("Connect gamepad 0", () => InputManager.ConnectGamepad(deviceId: 0, name: "Test Controller"));
        AddAssert("Status shows connected", () => visualiser.StatusText.Contains("Connected"));

        AddStep("Disconnect gamepad 0", () => InputManager.DisconnectGamepad(deviceId: 0));
        AddAssert("Status shows disconnected", () => visualiser.StatusText.Contains("Disconnected"));
    }

    [Test]
    public void TestFaceButtons()
    {
        AddStep("Connect gamepad", () => InputManager.ConnectGamepad());

        AddStep("Press South (A)", () => InputManager.PressGamepadButton(GamepadButton.South));
        AddAssert("South highlighted", () => visualiser.IsButtonHighlighted(GamepadButton.South));

        AddStep("Release South (A)", () => InputManager.ReleaseGamepadButton(GamepadButton.South));
        AddAssert("South released", () => !visualiser.IsButtonHighlighted(GamepadButton.South));

        AddStep("Tap East (B)", () => InputManager.TapGamepadButton(GamepadButton.East));
        AddStep("Tap West (X)", () => InputManager.TapGamepadButton(GamepadButton.West));
        AddStep("Tap North (Y)", () => InputManager.TapGamepadButton(GamepadButton.North));
    }

    [Test]
    public void TestDPad()
    {
        AddStep("Connect gamepad", () => InputManager.ConnectGamepad());

        AddStep("Press DPad Up", () => InputManager.PressGamepadButton(GamepadButton.DPadUp));
        AddAssert("DPad Up highlighted", () => visualiser.IsButtonHighlighted(GamepadButton.DPadUp));
        AddStep("Release DPad Up", () => InputManager.ReleaseGamepadButton(GamepadButton.DPadUp));

        AddStep("Tap DPad Down", () => InputManager.TapGamepadButton(GamepadButton.DPadDown));
        AddStep("Tap DPad Left", () => InputManager.TapGamepadButton(GamepadButton.DPadLeft));
        AddStep("Tap DPad Right", () => InputManager.TapGamepadButton(GamepadButton.DPadRight));
    }

    [Test]
    public void TestShoulderAndTriggers()
    {
        AddStep("Connect gamepad", () => InputManager.ConnectGamepad());

        AddStep("Press Left Shoulder", () => InputManager.PressGamepadButton(GamepadButton.LeftShoulder));
        AddStep("Release Left Shoulder", () => InputManager.ReleaseGamepadButton(GamepadButton.LeftShoulder));

        AddStep("Press Right Shoulder", () => InputManager.PressGamepadButton(GamepadButton.RightShoulder));
        AddStep("Release Right Shoulder", () => InputManager.ReleaseGamepadButton(GamepadButton.RightShoulder));

        AddStep("Pull Left Trigger halfway", () => InputManager.MoveGamepadAxis(GamepadAxis.LeftTrigger, 0.5f));
        AddAssert("Left trigger at 0.5", () => visualiser.GetAxisValue(GamepadAxis.LeftTrigger) == 0.5f);

        AddStep("Pull Left Trigger fully", () => InputManager.MoveGamepadAxis(GamepadAxis.LeftTrigger, 1.0f));
        AddAssert("Left trigger at 1.0", () => visualiser.GetAxisValue(GamepadAxis.LeftTrigger) == 1.0f);

        AddStep("Release Left Trigger", () => InputManager.MoveGamepadAxis(GamepadAxis.LeftTrigger, 0f));

        AddStep("Pull Right Trigger fully", () => InputManager.MoveGamepadAxis(GamepadAxis.RightTrigger, 1.0f));
        AddStep("Release Right Trigger", () => InputManager.MoveGamepadAxis(GamepadAxis.RightTrigger, 0f));
    }

    [Test]
    public void TestAnalogSticks()
    {
        AddStep("Connect gamepad", () => InputManager.ConnectGamepad());

        AddStep("Left stick right", () => InputManager.MoveGamepadAxis(GamepadAxis.LeftX, 1.0f));
        AddAssert("Left X at 1.0", () => visualiser.GetAxisValue(GamepadAxis.LeftX) == 1.0f);

        AddStep("Left stick down", () => InputManager.MoveGamepadAxis(GamepadAxis.LeftY, 1.0f));
        AddStep("Left stick centre", () =>
        {
            InputManager.MoveGamepadAxis(GamepadAxis.LeftX, 0f);
            InputManager.MoveGamepadAxis(GamepadAxis.LeftY, 0f);
        });

        AddStep("Right stick diagonal", () =>
        {
            InputManager.MoveGamepadAxis(GamepadAxis.RightX, 0.7f);
            InputManager.MoveGamepadAxis(GamepadAxis.RightY, -0.7f);
        });
        AddStep("Right stick centre", () =>
        {
            InputManager.MoveGamepadAxis(GamepadAxis.RightX, 0f);
            InputManager.MoveGamepadAxis(GamepadAxis.RightY, 0f);
        });
    }

    [Test]
    public void TestEventCounting()
    {
        AddStep("Connect gamepad", () => InputManager.ConnectGamepad());

        AddStep("Tap South 3 times", () =>
        {
            InputManager.TapGamepadButton(GamepadButton.South);
            InputManager.TapGamepadButton(GamepadButton.South);
            InputManager.TapGamepadButton(GamepadButton.South);
        });
        AddAssert("Button event count is 6 (3 down + 3 up)", () => visualiser.ButtonEventCount == 6);

        AddStep("Move left stick X", () => InputManager.MoveGamepadAxis(GamepadAxis.LeftX, 0.5f));
        AddAssert("Axis event count is at least 1", () => visualiser.AxisEventCount >= 1);
    }

    [Test]
    public void TestMultipleGamepads()
    {
        AddStep("Connect gamepad 0", () => InputManager.ConnectGamepad(deviceId: 0, name: "Player 1"));
        AddStep("Connect gamepad 1", () => InputManager.ConnectGamepad(deviceId: 1, name: "Player 2"));

        AddStep("P1 presses South", () => InputManager.TapGamepadButton(GamepadButton.South, deviceId: 0));
        AddStep("P2 presses South", () => InputManager.TapGamepadButton(GamepadButton.South, deviceId: 1));

        AddStep("Disconnect gamepad 1", () => InputManager.DisconnectGamepad(deviceId: 1));
        AddStep("Disconnect gamepad 0", () => InputManager.DisconnectGamepad(deviceId: 0));
    }

    private partial class GamepadVisualiser : Container
    {
        public string StatusText => statusLabel.Text;
        public int ButtonEventCount { get; private set; }
        public int AxisEventCount { get; private set; }

        public bool IsButtonHighlighted(GamepadButton button) =>
            buttonIndicators.TryGetValue(button, out var box) && box.Alpha > 0.9f;

        public float GetAxisValue(GamepadAxis axis) =>
            axisValues.TryGetValue(axis, out float v) ? v : 0f;

        private readonly Dictionary<GamepadButton, Box> buttonIndicators = new Dictionary<GamepadButton, Box>();
        private readonly Dictionary<GamepadAxis, float> axisValues = new Dictionary<GamepadAxis, float>();
        private readonly Dictionary<GamepadAxis, SpriteText> axisLabels = new Dictionary<GamepadAxis, SpriteText>();

        private SpriteText statusLabel;
        private SpriteText eventCountLabel;
        private SpriteText lastEventLabel;
        private FlowContainer buttonRow;
        private FlowContainer axisColumn;

        public GamepadVisualiser()
        {
            AutoSizeAxes = Axes.Both;
        }

        public override void Load()
        {
            base.Load();

            Children = new Drawable[]
            {
                new FlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 12),
                    Children = new Drawable[]
                    {
                        // Status row
                        statusLabel = new SpriteText
                        {
                            Text = "No gamepad connected",
                            Color = Color.Gray,
                            Font = FontUsage.Default.With(size: 18)
                        },

                        // Button grid
                        new SpriteText
                        {
                            Text = "Buttons",
                            Color = Color.White,
                            Font = FontUsage.Default.With(size: 14)
                        },
                        buttonRow = new FlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FlowDirection.Horizontal,
                            Spacing = new Vector2(6, 0)
                        },

                        // Axis readouts
                        new SpriteText
                        {
                            Text = "Axes",
                            Color = Color.White,
                            Font = FontUsage.Default.With(size: 14)
                        },
                        axisColumn = new FlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FlowDirection.Vertical,
                            Spacing = new Vector2(0, 4)
                        },

                        // Event counters
                        eventCountLabel = new SpriteText
                        {
                            Text = "Button events: 0   Axis events: 0",
                            Color = Color.LightGray,
                            Font = FontUsage.Default.With(size: 14)
                        },
                        lastEventLabel = new SpriteText
                        {
                            Text = "Last event: —",
                            Color = Color.LightGray,
                            Font = FontUsage.Default.With(size: 14)
                        },
                    }
                }
            };

            // Build button indicators
            var buttons = new[]
            {
                GamepadButton.South, GamepadButton.East, GamepadButton.West, GamepadButton.North,
                GamepadButton.DPadUp, GamepadButton.DPadDown, GamepadButton.DPadLeft, GamepadButton.DPadRight,
                GamepadButton.LeftShoulder, GamepadButton.RightShoulder,
                GamepadButton.LeftStick, GamepadButton.RightStick,
                GamepadButton.Start, GamepadButton.Back,
            };

            foreach (var btn in buttons)
            {
                var indicator = new ButtonIndicator(btn);
                buttonIndicators[btn] = indicator.Background;
                buttonRow.Add(indicator);
            }

            // Build axis readouts
            foreach (GamepadAxis axis in System.Enum.GetValues<GamepadAxis>())
            {
                if (axis == GamepadAxis.Unknown) continue;

                var label = new SpriteText
                {
                    Text = $"{axis}: 0.000",
                    Color = Color.LightBlue,
                    Font = FontUsage.Default.With(size: 13)
                };
                axisLabels[axis] = label;
                axisColumn.Add(label);
            }
        }

        private void updateEventCountLabel()
        {
            eventCountLabel.Text = $"Button events: {ButtonEventCount}   Axis events: {AxisEventCount}";
        }

        public override void OnGamepadConnected(GamepadConnectedEvent e)
        {
            statusLabel.Text = $"Connected — id={e.DeviceId}  \"{e.Name}\"";
            statusLabel.Color = Color.LightGreen;
            base.OnGamepadConnected(e);
        }

        public override void OnGamepadDisconnected(GamepadDisconnectedEvent e)
        {
            statusLabel.Text = $"Disconnected — id={e.DeviceId}";
            statusLabel.Color = Color.IndianRed;
            base.OnGamepadDisconnected(e);
        }

        public override bool OnGamepadButtonDown(GamepadButtonEvent e)
        {
            ButtonEventCount++;
            updateEventCountLabel();
            lastEventLabel.Text = $"Last event: {e.Button} DOWN";
            lastEventLabel.Color = Color.Yellow;

            if (buttonIndicators.TryGetValue(e.Button, out var box))
                box.Alpha = 1f;

            return base.OnGamepadButtonDown(e);
        }

        public override bool OnGamepadButtonUp(GamepadButtonEvent e)
        {
            ButtonEventCount++;
            updateEventCountLabel();
            lastEventLabel.Text = $"Last event: {e.Button} UP";
            lastEventLabel.Color = Color.White;

            if (buttonIndicators.TryGetValue(e.Button, out var box))
                box.Alpha = 0.25f;

            return base.OnGamepadButtonUp(e);
        }

        public override bool OnGamepadAxisMotion(GamepadAxisEvent e)
        {
            AxisEventCount++;
            updateEventCountLabel();
            axisValues[e.Axis] = e.Value;
            lastEventLabel.Text = $"Last event: {e.Axis} = {e.Value:+0.000;-0.000;0.000}";
            lastEventLabel.Color = Color.LightBlue;

            if (axisLabels.TryGetValue(e.Axis, out var label))
                label.Text = $"{e.Axis}: {e.Value:+0.000;-0.000;0.000}";

            return base.OnGamepadAxisMotion(e);
        }

        private partial class ButtonIndicator : Container
        {
            public Box Background { get; }

            public ButtonIndicator(GamepadButton button)
            {
                Size = new Vector2(52, 52);
                Masking = true;
                CornerRadius = 6;

                Children = new Drawable[]
                {
                    Background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(1),
                        Color = buttonColor(button),
                        Alpha = 0.25f
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = buttonLabel(button),
                        Color = Color.White,
                        Font = FontUsage.Default.With(size: 11)
                    }
                };
            }

            private static string buttonLabel(GamepadButton b) => b switch
            {
                GamepadButton.South => "A",
                GamepadButton.East => "B",
                GamepadButton.West => "X",
                GamepadButton.North => "Y",
                GamepadButton.DPadUp => "↑",
                GamepadButton.DPadDown => "↓",
                GamepadButton.DPadLeft => "←",
                GamepadButton.DPadRight => "→",
                GamepadButton.LeftShoulder => "LB",
                GamepadButton.RightShoulder => "RB",
                GamepadButton.LeftStick => "L3",
                GamepadButton.RightStick => "R3",
                GamepadButton.Start => "▶",
                GamepadButton.Back => "◀",
                _ => b.ToString()
            };

            private static Color buttonColor(GamepadButton b) => b switch
            {
                GamepadButton.South => Color.LimeGreen,
                GamepadButton.East => Color.IndianRed,
                GamepadButton.West => Color.CornflowerBlue,
                GamepadButton.North => Color.Goldenrod,
                GamepadButton.DPadUp
                    or GamepadButton.DPadDown
                    or GamepadButton.DPadLeft
                    or GamepadButton.DPadRight => Color.SteelBlue,
                GamepadButton.LeftShoulder
                    or GamepadButton.RightShoulder => Color.MediumPurple,
                _ => Color.Gray
            };
        }
    }
}
