// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface.KeyBinding;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;
using Path = System.IO.Path;

namespace Sakura.Framework.Tests.Visuals.Input;

public partial class TestKeyBindingPanel : ManualInputManagerTestScene
{
    private enum GameAction
    {
        Select,
        Back,
        VolumeUp,
        VolumeDown,
    }

    private TemporaryStorage storage;
    private KeyBindingStore<GameAction> store;
    private BasicKeyBindingPanel<GameAction> panel;

    private BasicKeyBindingRow<GameAction> firstRow => (BasicKeyBindingRow<GameAction>)panel.Children[0];

    private static KeyBinding[] defaults() => new[]
    {
        new KeyBinding(InputKeyExtensions.FromKey(Key.Enter), GameAction.Select),
        new KeyBinding(new KeyCombination(InputKey.Control, InputKeyExtensions.FromKey(Key.Z)), GameAction.Back),
        new KeyBinding(InputKey.MouseWheelUp, GameAction.VolumeUp),
        new KeyBinding(InputKey.MouseWheelDown, GameAction.VolumeDown),
    };

    [SetUp]
    public void SetUp()
    {
        AddStep("create panel", () =>
        {
            TestContent.Clear();

            storage = new TemporaryStorage(Path.Combine(Path.GetTempPath(), "sakura-kb-vis-" + Guid.NewGuid().ToString("N")));
            store = new KeyBindingStore<GameAction>(storage, defaults());

            // The visible panel under test — uses the default Basic visuals.
            panel = new BasicKeyBindingPanel<GameAction>(store)
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                // Panel is RelativeSizeAxes.X + AutoSizeAxes.Y internally; only adjust the relative
                // width here. Do NOT set Size.Y / RelativeSizeAxes.Y — that would defeat auto-height
                // and collapse the rows' hit areas.
                Width = 0.6f,
                SlotsPerAction = 2
            };

            TestContent.Add(panel);
        });
    }

    [Test]
    public void TestRebindEscapeIsBindable()
    {
        // Regression: Escape must be bindable, not hijacked as a cancel. Driven through real input
        // on the real panel: click the slot's capture button, then press Escape.
        AddStep("click slot 0 capture button", () =>
        {
            InputManager.MoveMouseTo(firstRow.CaptureButton(0));
            InputManager.Click(MouseButton.Left);
        });
        AddStep("press Escape", () =>
        {
            InputManager.PressKey(Key.Escape);
            InputManager.ReleaseKey(Key.Escape);
        });
        AddAssert("Select bound to Escape", () =>
            store.GetCombinations(GameAction.Select).Count == 1
            && store.GetCombinations(GameAction.Select)[0] == KeyCombination.Parse("Escape"));
        AddStep("Delete storage", () => storage.Dispose());
    }

    [Test]
    public void TestClearSlotUnbinds()
    {
        AddStep("click slot 0 clear button", () =>
        {
            InputManager.MoveMouseTo(firstRow.ClearButton(0));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Select has no bindings", () => store.GetCombinations(GameAction.Select).Count == 0);
        AddStep("Delete storage", () => storage.Dispose());
    }

    [Test]
    public void TestSecondSlotIndependent()
    {
        AddStep("click slot 1 capture button", () =>
        {
            InputManager.MoveMouseTo(firstRow.CaptureButton(1));
            InputManager.Click(MouseButton.Left);
        });
        AddStep("press F", () =>
        {
            InputManager.PressKey(Key.F);
            InputManager.ReleaseKey(Key.F);
        });
        AddAssert("Select has two bindings", () => store.GetCombinations(GameAction.Select).Count == 2);
        AddAssert("slot 0 still Enter, slot 1 is F", () =>
            store.GetCombinations(GameAction.Select)[0] == (KeyCombination)InputKeyExtensions.FromKey(Key.Enter)
            && store.GetCombinations(GameAction.Select)[1] == KeyCombination.Parse("F"));
        AddStep("Delete storage", () => storage.Dispose());
    }

    [Test]
    public void TestResetAll()
    {
        AddStep("override Select", () => store.SetCombinations(GameAction.Select, new[] { KeyCombination.Parse("Space") }));
        AddStep("reset all", () => panel.ResetAll());
        AddAssert("Select back to default", () => !store.IsOverridden(GameAction.Select));
        AddStep("Delete storage", () => storage.Dispose());
    }
}
