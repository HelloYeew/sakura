// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Input;

public partial class TestKeyBindingContainer : ManualInputManagerTestScene
{
    private enum TestAction
    {
        Select,
        Back,
        VolumeUp,
        VolumeDown,
        Jump,
    }

    private KeyBindingContainerTestContainer container;
    private RecordingHandler handler;

    private void createContainer(SimultaneousBindingMode simultaneous = SimultaneousBindingMode.None,
        KeyCombinationMatchingMode matching = KeyCombinationMatchingMode.Any,
        bool sendRepeats = false)
    {
        AddStep("create container", () =>
        {
            TestContent.Clear();

            container = new KeyBindingContainerTestContainer(simultaneous, matching, sendRepeats)
            {
                RelativeSizeAxes = Axes.Both
            };

            handler = new RecordingHandler
            {
                RelativeSizeAxes = Axes.Both
            };

            container.Add(handler);
            TestContent.Add(container);
        });
    }

    [Test]
    public void TestSingleKeyPressAndRelease()
    {
        createContainer();

        AddStep("press Enter", () => InputManager.PressKey(Key.Enter));
        AddAssert("Select pressed", () => handler.Pressed.Contains(TestAction.Select));
        AddStep("release Enter", () => InputManager.ReleaseKey(Key.Enter));
        AddAssert("Select released", () => handler.Released.Contains(TestAction.Select));
    }

    [Test]
    public void TestModifierCombination()
    {
        createContainer(matching: KeyCombinationMatchingMode.Modifiers);

        AddStep("press Ctrl+Z", () => InputManager.PressKey(Key.Z, KeyModifiers.Control));
        AddAssert("Back pressed", () => handler.Pressed.Contains(TestAction.Back));

        AddStep("fully release Ctrl+Z", () =>
        {
            // Release Z while Ctrl still held, then release Ctrl (no modifiers remaining).
            InputManager.ReleaseKey(Key.Z, KeyModifiers.Control);
            InputManager.ReleaseKey(Key.ControlLeft, KeyModifiers.None);
            handler.ClearList();
        });
        AddStep("press Z alone", () => InputManager.PressKey(Key.Z));
        AddAssert("Back NOT pressed without Ctrl", () => !handler.Pressed.Contains(TestAction.Back));
    }

    [Test]
    public void TestMouseButtonBinding()
    {
        createContainer();

        AddStep("move mouse over container", () => InputManager.MoveMouseTo(container));
        AddStep("press right mouse", () => InputManager.PressButton(MouseButton.Right));
        AddAssert("Jump pressed", () => handler.Pressed.Contains(TestAction.Jump));
        AddStep("release right mouse", () => InputManager.ReleaseButton(MouseButton.Right));
        AddAssert("Jump released", () => handler.Released.Contains(TestAction.Jump));
    }

    [Test]
    public void TestScrollBindingPressesAndReleasesImmediately()
    {
        createContainer();

        AddStep("move mouse over container", () => InputManager.MoveMouseTo(container));
        AddStep("scroll up", () => InputManager.ScrollBy(new Vector2(0, 1)));
        AddAssert("VolumeUp pressed", () => handler.Pressed.Contains(TestAction.VolumeUp));
        AddAssert("VolumeUp also released (momentary)", () => handler.Released.Contains(TestAction.VolumeUp));

        AddStep("reset", () => handler.ClearList());
        AddStep("scroll down", () => InputManager.ScrollBy(new Vector2(0, -1)));
        AddAssert("VolumeDown pressed", () => handler.Pressed.Contains(TestAction.VolumeDown));
    }

    [Test]
    public void TestGamepadBinding()
    {
        createContainer();

        AddStep("connect gamepad", () => InputManager.ConnectGamepad());
        AddStep("press South", () => InputManager.PressGamepadButton(GamepadButton.South));
        AddAssert("Select pressed", () => handler.Pressed.Contains(TestAction.Select));
        AddStep("release South", () => InputManager.ReleaseGamepadButton(GamepadButton.South));
        AddAssert("Select released", () => handler.Released.Contains(TestAction.Select));
    }

    [Test]
    public void TestRepeatSuppressedByDefault()
    {
        createContainer();

        AddStep("press Enter twice (held)", () =>
        {
            InputManager.PressKey(Key.Enter);
            // repeat
            InputManager.PressKey(Key.Enter);
        });
        AddAssert("Select pressed exactly once", () => handler.Pressed.Count(a => a == TestAction.Select) == 1);
    }

    [Test]
    public void TestSimultaneousNoneReleasesPrevious()
    {
        createContainer(simultaneous: SimultaneousBindingMode.None);

        AddStep("move mouse over container", () => InputManager.MoveMouseTo(container));
        AddStep("press Enter then right mouse", () =>
        {
            InputManager.PressKey(Key.Enter);
            InputManager.PressButton(MouseButton.Right);
        });

        // In None mode, pressing Jump should have released the still-held Select.
        AddAssert("Select was released", () => handler.Released.Contains(TestAction.Select));
        AddAssert("Jump is pressed", () => handler.Pressed.Contains(TestAction.Jump));
    }

    [Test]
    public void TestSimultaneousAllKeepsBothActive()
    {
        createContainer(simultaneous: SimultaneousBindingMode.All);

        AddStep("move mouse over container", () => InputManager.MoveMouseTo(container));
        AddStep("press Enter then right mouse", () =>
        {
            InputManager.PressKey(Key.Enter);
            InputManager.PressButton(MouseButton.Right);
        });

        AddAssert("Select still active", () => !handler.Released.Contains(TestAction.Select));
        AddAssert("both pressed", () => handler.Pressed.Contains(TestAction.Select) && handler.Pressed.Contains(TestAction.Jump));
    }

    private partial class KeyBindingContainerTestContainer : KeyBindingContainer<TestAction>
    {
        public KeyBindingContainerTestContainer(SimultaneousBindingMode simultaneous, KeyCombinationMatchingMode matching, bool sendRepeats)
        {
            SimultaneousMode = simultaneous;
            MatchingMode = matching;
            SendRepeats = sendRepeats;
        }

        public override IEnumerable<KeyBinding> DefaultKeyBindings => new[]
        {
            new KeyBinding(InputKeyExtensions.FromKey(Key.Enter), TestAction.Select),
            new KeyBinding(InputKeyExtensions.FromGamepadButton(GamepadButton.South), TestAction.Select),
            new KeyBinding(new KeyCombination(InputKey.Control, InputKeyExtensions.FromKey(Key.Z)), TestAction.Back),
            new KeyBinding(InputKey.MouseWheelUp, TestAction.VolumeUp),
            new KeyBinding(InputKey.MouseWheelDown, TestAction.VolumeDown),
            new KeyBinding(InputKey.MouseRight, TestAction.Jump),
        };
    }

    private partial class RecordingHandler : Container, IKeyBindingHandler<TestAction>
    {
        public readonly List<TestAction> Pressed = new List<TestAction>();
        public readonly List<TestAction> Released = new List<TestAction>();

        public void ClearList()
        {
            Pressed.Clear();
            Released.Clear();
        }

        public bool OnPressed(KeyBindingPressEvent<TestAction> e)
        {
            Pressed.Add(e.Action);
            return true;
        }

        public void OnReleased(KeyBindingReleaseEvent<TestAction> e)
        {
            Released.Add(e.Action);
        }
    }
}
