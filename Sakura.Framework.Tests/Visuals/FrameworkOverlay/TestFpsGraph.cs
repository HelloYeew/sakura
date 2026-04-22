// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public class TestFpsGraph : TestScene
{
    private FpsGraph fpsGraph;

    [SetUp]
    public void SetUp()
    {
        fpsGraph = new FpsGraph(Clock)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
        };
        AddStep("Add graph", () => Add(fpsGraph));
        AddStep("Pop in overlay", () => fpsGraph.FadeIn(100, Easing.OutQuint));
    }

    [Test]
    public void TestOverlay()
    {
        AddAssert("Graph is visible", () => fpsGraph.IsAlive);
    }
}
