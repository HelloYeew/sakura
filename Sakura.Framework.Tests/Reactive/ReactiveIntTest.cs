// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Tests.Reactive;

[TestFixture]
public class ReactiveIntTest
{
    [TestCase(10)]
    [TestCase(-10)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public void TestSetValue(int value)
    {
        var reactive = new ReactiveInt
        {
            Value = value
        };
        Assert.That(reactive.Value, Is.EqualTo(value));
    }

    [TestCase(10, 10, true)]
    [TestCase(10, -10, false)]
    public void TestDefaultValueCheck(int defaultValue, int value, bool isDefault)
    {
        var reactive = new ReactiveInt(defaultValue);
        reactive.ValueChanged += e => Console.WriteLine($"Value changed from {e.OldValue} to {e.NewValue}");
        Assert.That(reactive.Value, Is.EqualTo(defaultValue));
        Assert.That(reactive.Default, Is.EqualTo(defaultValue));
        Assert.That(reactive.IsDefault, Is.True);

        reactive.Value = value;
        Assert.That(reactive.IsDefault, Is.EqualTo(isDefault));
    }

    [TestCase("10", 10)]
    [TestCase("-10", -10)]
    public void TestParseFromString(string value, int actual)
    {
        var reactive = new ReactiveInt();
        reactive.Parse(value);
        Assert.That(reactive.Value, Is.EqualTo(actual));
    }

    [Test]
    public void TestSetValueWithinRange()
    {
        var reactive = new ReactiveInt
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 50
        };
        Assert.That(reactive.Value, Is.EqualTo(50));
        Assert.Throws<ArgumentOutOfRangeException>(() => reactive.Value = -1, "setting value below min value should throw an exception");
        Assert.Throws<ArgumentOutOfRangeException>(() => reactive.Value = 1000, "setting value above max value should throw an exception");
    }
}
