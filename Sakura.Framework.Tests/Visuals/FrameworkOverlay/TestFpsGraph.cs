// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Configurations;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public partial class TestFpsGraph : TestScene
{
    private FpsGraph fpsGraph;

    [Resolved]
    private FrameworkConfigManager frameworkConfigManager { get; set; } = null!;

    [SetUp]
    public void SetUp()
    {
        fpsGraph = new FpsGraph()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Scale = new Vector2(2)
        };
        AddStep("Add graph", () =>
        {
            Add(fpsGraph);
            if (IsVisualRunner)
            {
                var config = frameworkConfigManager.Get<PerformanceOverlayState>(FrameworkSetting.ShowFpsGraph);
                var dropdown = new BasicDropdown<PerformanceOverlayState>()
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Width = 200,
                    Items = Enum.GetValues<PerformanceOverlayState>(),
                    Margin = new MarginPadding(10)
                };
                dropdown.Current.BindTo(config);
                config.BindTo(dropdown.Current);
                Add(dropdown);
            }
        });
        AddStep("Pop in overlay", () => fpsGraph.FadeIn(100, Easing.OutQuint));
    }

    [Test]
    public void TestOverlay()
    {
        AddAssert("Graph is visible", () => fpsGraph.IsAlive);
    }
}
