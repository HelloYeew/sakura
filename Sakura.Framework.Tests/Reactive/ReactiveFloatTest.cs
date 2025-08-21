// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Globalization;
using NUnit.Framework;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Tests.Reactive;

[TestFixture]
public class ReactiveFloatTest
{
    [TestCase(10.5f)]
    [TestCase(10.555555f)]
    [TestCase(10f)]
    [TestCase(float.MinValue)]
    [TestCase(float.MaxValue)]
    public void TestSetValue(float value)
    {
        var reactive = new ReactiveFloat
        {
            Value = value
        };
        Assert.That(reactive.Value, Is.EqualTo(value));
    }

    [TestCase(10.5f, 0.1f, 10.5f)]
    [TestCase(10.5f, 0.000001f, 10.5f)]
    [TestCase(10f, 0.1f, 10f)]
    public void TestSetValueWithPrecision(float value, float precision, float expected)
    {
        var reactive = new ReactiveFloat
        {
            Value = value,
            Precision = precision
        };
        Assert.That(reactive.Value, Is.EqualTo(expected).Within(0.00001f));
    }

    [TestCase("10.5", 10.5f)]
    [TestCase("-10.5", -10.5f)]
    [TestCase("10.555555", 10.555555f)]
    [TestCase("0", 0.0f)]
    public void TestParseFromString(string value, float actual)
    {
        var reactive = new ReactiveFloat();
        reactive.Parse(value);
        Assert.That(reactive.Value, Is.EqualTo(actual));
    }

    [TestCase("1.4", 1.4f, "en-US")]
    [TestCase("1,4", 1.4f, "de-DE")]
    [TestCase("1.400,01", 1400.01f, "de-DE")]
    [TestCase("1 234,57", 1234.57f, "ru-RU")]
    [TestCase("1,094", 1.094f, "fr-FR")]
    [TestCase("1,400.01", 1400.01f, "zh-CN")]
    public void TestParseFromStringWithLocale(string value, float actual, string culture)
    {
        var reactive = new ReactiveFloat();
        reactive.Parse(value, CultureInfo.GetCultureInfo(culture));
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
