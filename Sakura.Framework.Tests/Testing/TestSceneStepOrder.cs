// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Testing;

/// <summary>
/// Under NUnit a test scene's steps run straight after its test method, so they run before any [TearDown] code, and
/// steps that a [TearDown] queues still run after that code.
/// </summary>
/// <remarks>
/// Only NUnit's order is checked. The test browser runs every [SetUp] and [TearDown] while it loads a scene, before any
/// step has run.
/// </remarks>
public partial class TestSceneStepOrder : TestScene
{
    private readonly List<string> events = new List<string>();

    /// <summary>
    /// Queues one step, which has to run before the tear-down.
    /// </summary>
    [Test]
    public void TestStepsRunBeforeTearDown() => AddStep("Record the test's step", () => events.Add("test step"));

    /// <summary>
    /// Records itself and queues a step, lets the base run that step, then checks the order.
    /// </summary>
    /// <remarks>
    /// One override rather than a [TearDown] of this class's own: NUnit counts an override as this class's, and runs
    /// two [TearDown] methods of one class in no set order.
    /// </remarks>
    public override void TeardownNUnit()
    {
        events.Add("tear-down");
        AddStep("Record the tear-down's step", () => events.Add("tear-down step"));

        base.TeardownNUnit();

        if (IsVisualRunner)
            return;

        Assert.That(events, Is.EqualTo(new[] { "test step", "tear-down", "tear-down step" }));
    }
}
