// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals;

/// <summary>
/// A parameterised <c>[Test]</c> inside a test scene. The browser loads scenes by reflection and used
/// to invoke every plain <c>[Test]</c> with no arguments, so a method taking <c>[Values]</c> threw
/// TargetParameterCountException and brought the whole application down rather than failing one test.
/// </summary>
public partial class TestBrowserParameterisedTests : ManualInputManagerTestScene
{
    [SetUp]
    public void SetUp()
    {
        AddStep("Clear", () =>
        {
            TestContent.Clear();
            TestContent.Add(new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(80),
                Color = Color.SteelBlue,
            });
        });
    }

    [Test]
    public void TestSingleValuesParameter([Values(1, 2, 3)] int value)
    {
        AddAssert("Received a value from the set", () => value is 1 or 2 or 3);
    }

    [Test]
    public void TestDoubleValuesParameter([Values(0.25, 0.5)] double value)
    {
        AddAssert("Received a value from the set", () => value == 0.25 || value == 0.5);
    }

    [Test]
    public void TestTwoValuesParametersCombine([Values(1, 2)] int first, [Values("a", "b")] string second)
    {
        // Four combinations in total; the browser must run each one rather than pick a single pairing.
        AddAssert("Both arguments arrived", () => first is 1 or 2 && second is "a" or "b");
    }

    [Test]
    public void TestRangeParameter([Range(1, 3)] int value)
    {
        AddAssert("Within the range", () => value >= 1 && value <= 3);
    }

    [Test]
    public void TestPlainTestStillRuns()
    {
        AddAssert("No arguments needed", () => true);
    }

    [TestCase(10)]
    [TestCase(20)]
    public void TestCaseStillRuns(int value)
    {
        AddAssert("Received the case argument", () => value == 10 || value == 20);
    }
}
