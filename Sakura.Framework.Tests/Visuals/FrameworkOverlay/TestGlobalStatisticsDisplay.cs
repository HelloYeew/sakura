// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public class TestGlobalStatisticsDisplay : TestScene
{
    private GlobalStatisticsDisplay overlay;

    [SetUp]
    public void SetUp()
    {
        overlay = new GlobalStatisticsDisplay()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Depth = float.MaxValue - 20
        };
        AddStep("Add overlay", () => Add(overlay));
        AddStep("Pop in overlay", () => overlay.ToggleVisibility());
    }

    [Test]
    public void TestDisplay()
    {
        AddAssert("Overlay is visible", () => overlay.Alpha > 0);
    }
}
