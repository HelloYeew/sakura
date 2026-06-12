// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Logging;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Tests.Reactive;

[TestFixture]
public class ReactiveNumberTest
{
    [OneTimeSetUp]
    public void InitializeLogger()
    {
        Logger.Initialize();
    }

    [Test]
    public void TestInitialValue()
    {
        var reactive = new ReactiveNumber<int>(10);
        Assert.That(reactive.Value, Is.EqualTo(10));
        Assert.That(reactive.Default, Is.EqualTo(10));
        Assert.That(reactive.IsDefault, Is.True);
    }

    [Test]
    public void TestMinMaxValue()
    {
        var reactive = new ReactiveNumber<int>(10);
        int eventCalledMin = 0;
        int eventCalledMax = 0;
        reactive.ValueChanged += e =>
        {
            Logger.Verbose($"MinValue: {reactive.MinValue}, MaxValue: {reactive.MaxValue}, Precision: {reactive.Precision}");
        };
        reactive.MinValueChanged += e =>
        {
            Logger.Verbose($"MinValueChanged: {e.OldValue} -> {e.NewValue}");
            Logger.Verbose($"MinValue: {reactive.MinValue}, MaxValue: {reactive.MaxValue}, Precision: {reactive.Precision} (invoke MinValueChanged)");
            eventCalledMin++;
        };
        reactive.MaxValueChanged += e =>
        {
            Logger.Verbose($"MaxValueChanged: {e.OldValue} -> {e.NewValue}");
            Logger.Verbose($"MinValue: {reactive.MinValue}, MaxValue: {reactive.MaxValue}, Precision: {reactive.Precision} (invoke MaxValueChanged)");
            eventCalledMax++;
        };
        Assert.That(reactive.MinValue, Is.EqualTo(int.MinValue));
        Assert.That(reactive.MaxValue, Is.EqualTo(int.MaxValue));
        Assert.That(reactive.Precision, Is.EqualTo(1));
        reactive.MinValue = 5;
        Assert.That(reactive.MinValue, Is.EqualTo(5));
        Assert.That(eventCalledMin, Is.EqualTo(1), "MinValueChanged should be called when the MinValue is set");
        reactive.MaxValue = 15;
        Assert.That(reactive.MaxValue, Is.EqualTo(15));
        Assert.That(eventCalledMax, Is.EqualTo(1), "MaxValueChanged should be called when the MaxValue is set");
        Assert.Throws<ArgumentOutOfRangeException>(() => reactive.MinValue = 20, "setting MinValue greater than MaxValue should throw an exception");
        Assert.Throws<ArgumentOutOfRangeException>(() => reactive.MaxValue = 3, "setting MaxValue less than MinValue should throw an exception");
        Assert.That(reactive.MinValue, Is.EqualTo(5), "MinValue should not change after failed set");
        Assert.That(reactive.MaxValue, Is.EqualTo(15), "MaxValue should not change after failed set");
    }

    [Test]
    public void TestValueChangedEventValue()
    {
        var reactive = new ReactiveNumber<int>(10);
        int eventCalled = 0;
        int oldValue = 0;
        int newValue = 0;
        reactive.ValueChanged += e =>
        {
            oldValue = e.OldValue;
            newValue = e.NewValue;
            eventCalled++;
        };
        reactive.Value = 20;
        Assert.That(reactive.Value, Is.EqualTo(20), "value changed when set to a new value");
        Assert.That(eventCalled, Is.EqualTo(1), "ValueChanged should be called when the value is changed");
        Assert.That(oldValue, Is.EqualTo(10), "oldValue should be the previous value before change");
        Assert.That(newValue, Is.EqualTo(20), "newValue should be the new value after change");
    }

    [Test]
    public void TestSetValueWithinRange()
    {
        var reactive = new ReactiveNumber<int>(10);
        reactive.MinValue = 5;
        reactive.MaxValue = 15;
        int eventCalled = 0;
        reactive.ValueChanged += e => eventCalled++;
        reactive.Value = 12;
        Assert.That(reactive.Value, Is.EqualTo(12), "value changed when set within range");
        Assert.That(eventCalled, Is.EqualTo(1), "ValueChanged should be called when the value is set within range");

        // Out-of-range values clamp to the nearest bound
        reactive.Value = 4;
        Assert.That(reactive.Value, Is.EqualTo(5), "setting value below MinValue should clamp to min");
        reactive.Value = 100;
        Assert.That(reactive.Value, Is.EqualTo(15), "setting value above MaxValue should clamp to max");
    }

    [Test]
    public void TestPrecisionIsAppliedToValue()
    {
        // Regression: the Precision setter previously fired its event without ever
        // assigning the field, so precision never took effect.
        var reactive = new ReactiveNumber<float>(0)
        {
            MinValue = 0,
            MaxValue = 100
        };

        reactive.Precision = 0.5f;
        Assert.That(reactive.Precision, Is.EqualTo(0.5f), "Precision must actually change when set");

        reactive.Value = 10.34f;
        Assert.That(reactive.Value, Is.EqualTo(10.5f).Within(0.0001f), "Value must snap to the precision step");

        reactive.Value = 10.1f;
        Assert.That(reactive.Value, Is.EqualTo(10f).Within(0.0001f));
    }

    [Test]
    public void TestShrinkingRangeReclampsCurrentValue()
    {
        var upper = new ReactiveNumber<int>(50)
        {
            MinValue = 0,
            MaxValue = 100
        };

        upper.Value = 90;
        upper.MaxValue = 60;
        Assert.That(upper.Value, Is.EqualTo(60), "shrinking the max bound must clamp the current value");

        var lower = new ReactiveNumber<int>(10)
        {
            MinValue = 0,
            MaxValue = 100
        };

        lower.Value = 10;
        lower.MinValue = 30;
        Assert.That(lower.Value, Is.EqualTo(30), "raising the min bound must clamp the current value");
    }

    [Test]
    public void TestSetValueByParse()
    {
        var reactive = new ReactiveNumber<int>(10);
        int eventCalled = 0;
        reactive.ValueChanged += e => eventCalled++;
        reactive.Parse("20");
        Assert.That(reactive.Value, Is.EqualTo(20));
        Assert.That(eventCalled, Is.EqualTo(1), "ValueChanged should be called when the value is set by Parse");
        Assert.Throws(typeof(FormatException), () => reactive.Parse("string"), "use parse with the value that's not convertible to holding type should throw an exception");
        Assert.That(reactive.Value, Is.EqualTo(20), "value still not change after failed parse");
        Assert.That(eventCalled, Is.EqualTo(1), "ValueChanged should not be called after failed parse");
    }
}
