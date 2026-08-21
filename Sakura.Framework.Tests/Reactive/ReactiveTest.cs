// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Reactive;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Tests.Reactive;

[TestFixture]
public class ReactiveTest
{
    [OneTimeSetUp]
    public void InitializeLogger()
    {
        Logger.Initialize();
    }

    [OneTimeTearDown]
    public void ShutdownLogger()
    {
        Logger.Shutdown();
    }

    [Test]
    public void TestInitialValue()
    {
        var reactive = new Reactive<int>(10);
        Assert.That(reactive.Value, Is.EqualTo(10));
        Assert.That(reactive.Default, Is.EqualTo(10));
    }

    [Test]
    public void TestValueChangedEvent()
    {
        const string start_value = "anon";
        var reactive = new Reactive<string>(start_value);
        string? eventOldValue = null;
        string? eventNewValue = null;
        int firedCount = 0;

        reactive.ValueChanged += e =>
        {
            firedCount++;
            eventOldValue = e.OldValue;
            eventNewValue = e.NewValue;
        };

        reactive.Value = "anon tokyo";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firedCount, Is.EqualTo(1));
            Assert.That(eventOldValue, Is.EqualTo(start_value));
            Assert.That(eventNewValue, Is.EqualTo("anon tokyo"));
            Assert.That(reactive.Value, Is.EqualTo("anon tokyo"));
        }

        reactive.Value = "anon tokyo";
        Assert.That(firedCount, Is.EqualTo(1), "ValueChanged should not fire when the value does not change");

        reactive.Value = "anon tokyo 2";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firedCount, Is.EqualTo(2));
            Assert.That(eventOldValue, Is.EqualTo("anon tokyo"));
            Assert.That(eventNewValue, Is.EqualTo("anon tokyo 2"));
            Assert.That(reactive.Value, Is.EqualTo("anon tokyo 2"));
        }
    }

    [Test]
    public void TestSetValueByParse()
    {
        var reactive = new Reactive<int>(10);
        Assert.That(reactive.Value, Is.EqualTo(10));

        reactive.Parse("20");
        Assert.That(reactive.Value, Is.EqualTo(20));

        Assert.Throws(typeof(FormatException), () => reactive.Parse("not a number"), "use parse with the value that's not convertible to holding type should throw an exception");
        Assert.That(reactive.Value, Is.EqualTo(20), "value still not change");
    }

    [Test]
    public void TestBindingValueSingle()
    {
        var source = new Reactive<int>(5);
        var target = new Reactive<int>(0);

        target.BindTo(source);

        source.Value = 10;
        Assert.That(target.Value, Is.EqualTo(10), "target value changed after bind");
        Assert.That(source.Value, Is.EqualTo(10), "source value changed normally");

        target.Value = 20;
        Assert.That(source.Value, Is.EqualTo(10), "source value not changed by target");
        Assert.That(target.Value, Is.EqualTo(20), "target value changed after target set");
    }

    [Test]
    public void TestBindingValueMultiple()
    {
        var source = new Reactive<int>(5);
        var target1 = new Reactive<int>(0);
        var target2 = new Reactive<int>(0);

        target1.BindTo(source);
        target2.BindTo(source);

        source.Value = 10;
        Assert.That(target1.Value, Is.EqualTo(10), "target1 value changed after bind");
        Assert.That(target2.Value, Is.EqualTo(10), "target2 value changed after bind");
        Assert.That(source.Value, Is.EqualTo(10), "source value changed normally");
    }

    [Test]
    public void TestBindingMultipleSources()
    {
        var source1 = new Reactive<int>(5);
        var source2 = new Reactive<int>(10);
        var target = new Reactive<int>(0);

        target.BindTo(source1);
        target.BindTo(source2);

        source1.Value = 15;
        Assert.That(target.Value, Is.EqualTo(15), "target value changed after first source bind");

        source2.Value = 20;
        Assert.That(target.Value, Is.EqualTo(20), "target value changed after second source bind");
    }

    [Test]
    public void TestUnbindSpecificSource()
    {
        var source = new Reactive<int>(5);
        var target = new Reactive<int>(0);

        target.BindTo(source);
        source.Value = 10;
        Assert.That(target.Value, Is.EqualTo(10), "target value changed after bind");

        target.UnbindFrom(source);
        source.Value = 20;
        Assert.That(target.Value, Is.EqualTo(10), "target value did not change after unbind");
    }

    [Test]
    public void TestUnbindAllSources()
    {
        var source1 = new Reactive<int>(5);
        var source2 = new Reactive<int>(10);
        var target = new Reactive<int>(0);

        target.BindTo(source1);
        target.BindTo(source2);

        source1.Value = 15;
        source2.Value = 20;
        Assert.That(target.Value, Is.EqualTo(20), "target value changed after both binds");

        target.UnbindAll();
        source1.Value = 25;
        source2.Value = 30;
        Assert.That(target.Value, Is.EqualTo(20), "target value did not change after unbind all");
    }

    #region Re-entrant notification

    /// <summary>
    /// A handler that coerces the value and writes it back must not leave the subscribers behind it
    /// being told about the value it just replaced.
    /// </summary>
    /// <remarks>
    /// This is the shape that used to hang the game: a slider rounding its own value on change
    /// (<see cref="Sakura.Framework.Graphics.UserInterface.SliderBar{T}.DecimalPlaces"/>) plus a
    /// two-way binding to a config setting. The rounding handler ran first and replaced 0.379… with
    /// 0.38; the binding handler, still holding the superseded event, pushed 0.379… back in; the
    /// rounding handler rounded it again, and so on until the stack ran out.
    /// </remarks>
    [Test]
    public void TestHandlerThatWritesBackDoesNotLeaveLaterHandlersStale()
    {
        var reactive = new Reactive<double>(0);
        var seen = new List<double>();

        // Subscribed first, as SliderBar's rounding is.
        reactive.ValueChanged += e =>
        {
            double rounded = Math.Round(e.NewValue, 2);

            if (rounded != e.NewValue)
                reactive.Value = rounded;
        };

        reactive.ValueChanged += e => seen.Add(e.NewValue);

        reactive.Value = 0.3792797029018402;

        Assert.That(reactive.Value, Is.EqualTo(0.38));
        Assert.That(seen, Is.EqualTo(new[] { 0.38 }), "the later handler was told about a superseded value");
    }

    [Test]
    public void TestTwoWayBindingWithACoercingHandlerTerminates()
    {
        var slider = new ReactiveNumber<double>(0) { MinValue = 0, MaxValue = 1 };
        var config = new Reactive<double>(0);

        int configChanges = 0;

        // Rounding on change, subscribed before the binding exactly as SliderBar does it.
        slider.ValueChanged += e =>
        {
            double rounded = Math.Round(e.NewValue, 2);

            if (rounded != e.NewValue)
                slider.Value = rounded;
        };

        config.ValueChanged += _ => configChanges++;

        slider.BindTo(config);
        config.BindTo(slider);

        // Before the fix this recursed until the stack overflowed, the two sides alternating between
        // the raw value and its rounding.
        slider.Value = 0.3792797029018402;

        Assert.That(slider.Value, Is.EqualTo(0.38));
        Assert.That(config.Value, Is.EqualTo(0.38), "the two sides disagreed about the value");
        Assert.That(configChanges, Is.EqualTo(1), "the bound side was notified more than once for one change");
    }

    [Test]
    public void TestSingleHandlerThatWritesBackStillSettles()
    {
        var reactive = new Reactive<int>(0);

        reactive.ValueChanged += e =>
        {
            if (e.NewValue > 10)
                reactive.Value = 10;
        };

        reactive.Value = 50;

        Assert.That(reactive.Value, Is.EqualTo(10));
    }

    [Test]
    public void TestUnsubscribingRestoresTheSingleHandlerPath()
    {
        var reactive = new Reactive<int>(0);
        var seen = new List<int>();

        Action<ValueChangedEvent<int>> first = _ => { };
        Action<ValueChangedEvent<int>> second = e => seen.Add(e.NewValue);

        reactive.ValueChanged += first;
        reactive.ValueChanged += second;
        reactive.ValueChanged -= first;

        // Removing a handler that was never added must not make the count disagree with reality.
        reactive.ValueChanged -= first;

        reactive.Value = 7;

        Assert.That(seen, Is.EqualTo(new[] { 7 }));
    }

    #endregion
}
