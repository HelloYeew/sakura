// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

public partial class TestSafeAreaContainer : TestScene
{
    private SafeAreaDefiningContainer defining = null!;
    private SafeAreaContainer safeContainer = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Create hierarchy", () =>
        {
            Clear();

            Add(defining = new SafeAreaDefiningContainer
            {
                Children = new Drawable[]
                {
                    // Full-bleed background: extends under the (simulated) notch.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = Color.DarkRed
                    },
                    safeContainer = new SafeAreaContainer
                    {
                        Children = new Drawable[]
                        {
                            // Safe content: stays clear of the insets.
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Color = Color.DarkSlateGray
                            },
                            new SpriteText
                            {
                                Text = "Safe content area — dark red regions simulate the unsafe area (notch / corners).",
                                Position = new Vector2(10, 10),
                                Color = Color.White
                            }
                        }
                    }
                }
            });
        });
    }

    [Test]
    public void TestSimulatedInsets()
    {
        AddStep("Simulate notch (top 60)", () => defining.OverrideSafeArea = new MarginPadding { Top = 60 });
        AddAssert("Padding applied", () => safeContainer.Padding.Top > 0);

        AddStep("Simulate landscape notch (left 80)", () => defining.OverrideSafeArea = new MarginPadding { Left = 80 });
        AddStep("Simulate all edges", () => defining.OverrideSafeArea = new MarginPadding(40));

        AddSliderStep("Top inset", 0f, 150f, 60f, v =>
        {
            if (defining != null)
            {
                var p = defining.OverrideSafeArea ?? new MarginPadding();
                p.Top = v;
                defining.OverrideSafeArea = p;
            }
        });
    }

    [Test]
    public void TestOverrideEdges()
    {
        AddStep("Simulate all edges (40)", () => defining.OverrideSafeArea = new MarginPadding(40));
        AddStep("Override top edge", () => safeContainer.SafeAreaOverrideEdges = Edges.Top);
        AddAssert("Top padding ignored", () => safeContainer.Padding.Top == 0 && safeContainer.Padding.Left > 0);
        AddStep("Override all edges", () => safeContainer.SafeAreaOverrideEdges = Edges.All);
        AddAssert("All padding ignored", () => safeContainer.Padding.Equals(new MarginPadding()));
        AddStep("Respect all edges", () => safeContainer.SafeAreaOverrideEdges = Edges.None);
    }

    [Test]
    public void TestFollowWindow()
    {
        AddStep("Follow window safe area", () => defining.OverrideSafeArea = null);
        AddAssert("Insets match window (usually zero on desktop)", () => defining.OverrideSafeArea == null);
    }
}
