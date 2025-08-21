// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Tests.Reactive;

[TestFixture]
public class ReactiveBoolTest
{
    [TestCase(true)]
    [TestCase(false)]
    public void TestSetValue(bool value)
    {
        var reactive = new ReactiveBool
        {
            Value = value
        };
        Assert.That(reactive.Value, Is.EqualTo(value));
    }

    [TestCase("true", true)]
    [TestCase("True", true)]
    [TestCase("false", false)]
    [TestCase("False", false)]
    [TestCase("1", true)]
    [TestCase("0", false)]
    public void TestParseFromString(string value, bool actual)
    {
        var reactive = new ReactiveBool();
        reactive.Parse(value);
        Assert.That(reactive.Value, Is.EqualTo(actual));
    }

    [Test]
    public void TestToggle()
    {
        var reactive = new ReactiveBool();
        Assert.That(reactive.Value, Is.False);

        reactive.Toggle();
        Assert.That(reactive.Value, Is.True);

        reactive.Toggle();
        Assert.That(reactive.Value, Is.False);
    }
}
