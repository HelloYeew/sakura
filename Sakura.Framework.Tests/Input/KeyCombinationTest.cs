// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Input;

public class KeyCombinationTest
{
    private static InputKey k(Key key) => InputKeyExtensions.FromKey(key);

    [Test]
    public void TestParseSingleKey()
    {
        var combo = KeyCombination.Parse("A");
        Assert.That(combo.Keys, Has.Length.EqualTo(1));
        Assert.That(combo.Contains(k(Key.A)), Is.True);
    }

    [Test]
    public void TestParseModifierCombination()
    {
        var combo = KeyCombination.Parse("Control+A");
        Assert.That(combo.Keys, Has.Length.EqualTo(2));
        Assert.That(combo.Contains(InputKey.Control), Is.True);
        Assert.That(combo.Contains(k(Key.A)), Is.True);
    }

    [Test]
    public void TestParseIsOrderIndependent()
    {
        Assert.That(KeyCombination.Parse("Control+A"), Is.EqualTo(KeyCombination.Parse("A+Control")));
    }

    [Test]
    public void TestPhysicalModifierFoldsToLogical()
    {
        // A combination declared with a side-specific modifier is matched by either side.
        var combo = new KeyCombination(InputKeyExtensions.FromKey(Key.ControlLeft), k(Key.A));
        Assert.That(combo.Contains(InputKey.Control), Is.True);

        var pressed = new[] { InputKeyExtensions.FromKey(Key.ControlRight), k(Key.A) };
        Assert.That(combo.IsPressed(pressed, KeyCombinationMatchingMode.Exact), Is.True);
    }

    [Test]
    public void TestMatchingModeAnyIgnoresExtraKeys()
    {
        var combo = KeyCombination.Parse("A");
        var pressed = new[] { k(Key.A), k(Key.B) };

        Assert.That(combo.IsPressed(pressed, KeyCombinationMatchingMode.Any), Is.True);
        Assert.That(combo.IsPressed(pressed, KeyCombinationMatchingMode.Exact), Is.False);
    }

    [Test]
    public void TestMatchingModeModifiersRequiresExactModifierSet()
    {
        var ctrlA = KeyCombination.Parse("Control+A");

        var ctrlAPressed = new[] { InputKey.Control, k(Key.A) };
        var ctrlShiftAPressed = new[] { InputKey.Control, InputKey.Shift, k(Key.A) };

        Assert.That(ctrlA.IsPressed(ctrlAPressed, KeyCombinationMatchingMode.Modifiers), Is.True);
        // Extra modifier breaks a Modifiers-mode match...
        Assert.That(ctrlA.IsPressed(ctrlShiftAPressed, KeyCombinationMatchingMode.Modifiers), Is.False);
        // ...but not an Any-mode match.
        Assert.That(ctrlA.IsPressed(ctrlShiftAPressed, KeyCombinationMatchingMode.Any), Is.True);
    }

    [Test]
    public void TestMatchingModeModifiersAllowsExtraNonModifierKeys()
    {
        var ctrlA = KeyCombination.Parse("Control+A");
        var pressed = new[] { InputKey.Control, k(Key.A), k(Key.B) };

        Assert.That(ctrlA.IsPressed(pressed, KeyCombinationMatchingMode.Modifiers), Is.True);
    }

    [Test]
    public void TestEmptyCombinationNeverPressed()
    {
        Assert.That(KeyCombination.NONE.IsPressed(new[] { k(Key.A) }, KeyCombinationMatchingMode.Any), Is.False);
    }

    [Test]
    public void TestMouseAndScrollParsing()
    {
        Assert.That(KeyCombination.Parse("MouseLeft").Contains(InputKey.MouseLeft), Is.True);
        Assert.That(KeyCombination.Parse("Alt+MouseWheelUp").Contains(InputKey.MouseWheelUp), Is.True);
        Assert.That(KeyCombination.Parse("Alt+MouseWheelUp").Contains(InputKey.Alt), Is.True);
    }

    [Test]
    public void TestScrollDeltaMapping()
    {
        Assert.That(InputKeyExtensions.FromScrollDelta(new Vector2(0, 1)).vertical, Is.EqualTo(InputKey.MouseWheelUp));
        Assert.That(InputKeyExtensions.FromScrollDelta(new Vector2(0, -1)).vertical, Is.EqualTo(InputKey.MouseWheelDown));
        Assert.That(InputKeyExtensions.FromScrollDelta(new Vector2(1, 0)).horizontal, Is.EqualTo(InputKey.MouseWheelRight));
        Assert.That(InputKeyExtensions.FromScrollDelta(new Vector2(-1, 0)).horizontal, Is.EqualTo(InputKey.MouseWheelLeft));
    }

    [Test]
    public void TestRoundTripToString()
    {
        var combo = KeyCombination.Parse("Control+A");
        Assert.That(KeyCombination.Parse(combo.ToString()), Is.EqualTo(combo));
    }

    [Test]
    public void TestKeyboardKeysDisplayByNameNotNumber()
    {
        // Regression: keyboard InputKeys (e.g. KeyboardFirst + Key.A) must render as "A", not "1xxx".
        var combo = new KeyCombination(InputKeyExtensions.FromKey(Key.A));
        Assert.That(combo.ToString(), Is.EqualTo("A"));

        var withMod = new KeyCombination(InputKey.Control, InputKeyExtensions.FromKey(Key.S));
        Assert.That(withMod.ToString(), Does.Contain("Control"));
        Assert.That(withMod.ToString(), Does.Contain("S"));
        Assert.That(withMod.ToString(), Does.Not.Contain("100"));
    }

    [Test]
    public void TestReadableNameForEachDeviceType()
    {
        Assert.That(InputKeyExtensions.FromKey(Key.Space).GetReadableName(), Is.EqualTo("Space"));
        Assert.That(InputKey.MouseLeft.GetReadableName(), Is.EqualTo("MouseLeft"));
        Assert.That(InputKey.MouseWheelUp.GetReadableName(), Is.EqualTo("MouseWheelUp"));
        Assert.That(InputKeyExtensions.FromGamepadButton(GamepadButton.South).GetReadableName(), Is.EqualTo("GamepadSouth"));
    }

    [Test]
    public void TestGamepadRoundTrip()
    {
        var combo = new KeyCombination(InputKeyExtensions.FromGamepadButton(GamepadButton.South));
        Assert.That(KeyCombination.Parse(combo.ToString()), Is.EqualTo(combo));
    }

    [Test]
    public void TestRoundTripAcrossDevices()
    {
        var combo = new KeyCombination(InputKey.Control, InputKeyExtensions.FromKey(Key.A), InputKey.MouseWheelUp);
        Assert.That(KeyCombination.Parse(combo.ToString()), Is.EqualTo(combo));
    }
}
