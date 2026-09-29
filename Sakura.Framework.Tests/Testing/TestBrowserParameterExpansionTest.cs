// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Testing;

/// <summary>
/// Test for the browser's own expansion of a parameterized [Test].
/// </summary>
[TestFixture]
public class TestBrowserParameterExpansionTest
{
    private static object[][] expand(string methodName, out string skipReason)
    {
        var method = typeof(Subject).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!;
        return TestBrowserApp.BuildParameterCombinations(typeof(Subject), method, out skipReason);
    }

    [Test]
    public void TestSingleParameterExpandsToOneCombinationPerValue()
    {
        object[][] combinations = expand(nameof(Subject.OneParameter), out _);

        Assert.That(combinations, Has.Length.EqualTo(3));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(combinations[0], Has.Length.EqualTo(1));
            Assert.That(combinations.Select(c => c[0]), Is.EquivalentTo(new object[] { 1, 2, 3 }));
        }
    }

    [Test]
    public void TestTwoParametersProduceTheCartesianProduct()
    {
        object[][] combinations = expand(nameof(Subject.TwoParameters), out _);

        Assert.That(combinations, Has.Length.EqualTo(6), "Three values times two values.");
        Assert.That(combinations, Has.All.Length.EqualTo(2));
    }

    [Test]
    public void TestValuesAreConvertedToTheParameterType()
    {
        object[][] combinations = expand(nameof(Subject.DoubleParameter), out _);

        Assert.That(combinations, Has.Length.EqualTo(2));
        Assert.That(combinations[0][0], Is.TypeOf<double>());
    }

    [Test]
    public void TestRangeIsExpandedToo()
    {
        object[][] combinations = expand(nameof(Subject.RangeParameter), out _);
        Assert.That(combinations, Has.Length.EqualTo(3));
    }

    [Test]
    public void TestParameterWithoutADataSourceIsReportedRatherThanThrowing()
    {
        object[][] combinations = expand(nameof(Subject.NoDataSource), out string skipReason);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(combinations, Is.Null, "There is nothing sensible to invoke it with.");
            Assert.That(skipReason, Does.Contain("orphan"), "The reason names the offending parameter.");
        }
    }

    private class Subject
    {
#pragma warning disable CA1822 // Methods can be mark as static
        public void OneParameter([Values(1, 2, 3)] int value) { }

        public void TwoParameters([Values(1, 2, 3)] int first, [Values("a", "b")] string second) { }

        public void DoubleParameter([Values(0.25, 0.5)] double value) { }

        public void RangeParameter([Range(1, 3)] int value) { }

        public void NoDataSource(int orphan) { }
    }
#pragma warning restore CA1822
}
