// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Input;

public class KeyBindingStoreTest
{
    private enum TestAction
    {
        Select,
        Back,
    }

    private TemporaryStorage storage = null!;
    private string dir = null!;

    private static KeyBinding[] defaults() => new[]
    {
        new KeyBinding(InputKeyExtensions.FromKey(Key.Enter), TestAction.Select),
        new KeyBinding(new KeyCombination(InputKey.Control, InputKeyExtensions.FromKey(Key.Z)), TestAction.Back),
    };

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "sakura-kb-" + Guid.NewGuid().ToString("N"));
        storage = new TemporaryStorage(dir);
    }

    [TearDown]
    public void TearDown()
    {
        storage.Dispose();
    }

    [Test]
    public void TestDefaultsUsedWhenNoOverrides()
    {
        var store = new KeyBindingStore<TestAction>(storage, defaults());

        Assert.That(store.IsOverridden(TestAction.Select), Is.False);
        Assert.That(store.GetCombinations(TestAction.Select).Single(), Is.EqualTo((KeyCombination)InputKeyExtensions.FromKey(Key.Enter)));
    }

    [Test]
    public void TestSetOverridePersistsAndReloads()
    {
        var store = new KeyBindingStore<TestAction>(storage, defaults());
        store.SetCombinations(TestAction.Select, new[] { KeyCombination.Parse("Space") });

        // New store instance over the same storage should load the override.
        var reloaded = new KeyBindingStore<TestAction>(storage, defaults());
        Assert.That(reloaded.IsOverridden(TestAction.Select), Is.True);
        Assert.That(reloaded.GetCombinations(TestAction.Select).Single(), Is.EqualTo(KeyCombination.Parse("Space")));
    }

    [Test]
    public void TestResetToDefaultRemovesOverride()
    {
        var store = new KeyBindingStore<TestAction>(storage, defaults());
        store.SetCombinations(TestAction.Back, new[] { KeyCombination.Parse("Escape") });
        Assert.That(store.IsOverridden(TestAction.Back), Is.True);

        store.ResetToDefault(TestAction.Back);
        Assert.That(store.IsOverridden(TestAction.Back), Is.False);
        Assert.That(store.GetCombinations(TestAction.Back).Single(), Is.EqualTo(KeyCombination.Parse("Control+Z")));
    }

    [Test]
    public void TestChangedEventFires()
    {
        var store = new KeyBindingStore<TestAction>(storage, defaults());
        int fired = 0;
        store.Changed += () => fired++;

        store.SetCombinations(TestAction.Select, new[] { KeyCombination.Parse("Space") });
        store.ResetAllToDefault();

        Assert.That(fired, Is.EqualTo(2));
    }

    [Test]
    public void TestGetBindingsMergesOverridesOverDefaults()
    {
        var store = new KeyBindingStore<TestAction>(storage, defaults());
        store.SetCombinations(TestAction.Select, new[] { KeyCombination.Parse("Space") });

        var bindings = store.GetBindings(typeof(object)).ToList();

        var selectCombo = bindings.Single(b => b.GetAction<TestAction>().Equals(TestAction.Select)).KeyCombination;
        var backCombo = bindings.Single(b => b.GetAction<TestAction>().Equals(TestAction.Back)).KeyCombination;

        Assert.That(selectCombo, Is.EqualTo(KeyCombination.Parse("Space")));     // overridden
        Assert.That(backCombo, Is.EqualTo(KeyCombination.Parse("Control+Z")));   // default
    }

    [Test]
    public void TestDefaultKeyCombinationIsSafe()
    {
        // Regression: FirstOrDefault() on an empty sequence yields default(KeyCombination); reading
        // .Keys must not throw on the uninitialised ImmutableArray.
        KeyCombination empty = default;
        Assert.That(empty.Keys.Length, Is.EqualTo(0));
        Assert.That(empty.ToString(), Is.EqualTo(string.Empty));
    }
}
