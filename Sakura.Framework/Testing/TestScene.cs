// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Testing;

[TestFixture]
public abstract class TestScene : Container
{
    public IReadOnlyList<TestStep> Steps => steps;
    private readonly List<TestStep> steps = new();

    public TestScene()
    {
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
    }

    public void AddStep(string description, Action stepAction)
    {
        steps.Add(new TestStep
        {
            Description = description,
            Action = stepAction,
            IsAssert = false
        });
    }

    public void AddAssert(string description, Func<bool> assert)
    {
        steps.Add(new TestStep
        {
            Description = description,
            Action = () => Assert.That(assert(), description),
            IsAssert = true
        });
    }

    [Test]
    public virtual void RunTestsHeadless()
    {
        // TODO: This need to set AppHost to run headless, and then run step
        foreach (var step in steps)
        {
            step.Action();
        }
    }

    // TODO: Wait until step, add wait step every x milliseconds until step is completed, or add a timeout to prevent infinite waiting.
}
