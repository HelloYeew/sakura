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

public class TestPanZoomContainer : ManualInputManagerTestScene
{
    private PanZoomContainer panZoom = null!;
    private SpriteText statsText = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Initialize Workspace", () =>
        {
            TestContent.Clear();

            panZoom = new PanZoomContainer
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Depth = 1
            };

            // center marker
            panZoom.Add(new Box
            {
                Size = new Vector2(20, 20),
                Origin = Anchor.Centre,
                Position = Vector2.Zero,
                Color = Color.Red
            });
            
            for (int x = -1000; x <= 1000; x += 250)
            {
                for (int y = -1000; y <= 1000; y += 250)
                {
                    if (x == 0 && y == 0) continue; // Skip center

                    panZoom.Add(new DummyNode
                    {
                        Position = new Vector2(x, y)
                    });
                }
            }

            statsText = new SpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(10, 10),
                Color = Color.Yellow,
                Text = "Zoom: 1.0x | Pan: <0, 0>",
                Depth = 0
            };

            TestContent.Add(panZoom);
            TestContent.Add(statsText);
        });
    }

    public override void Update()
    {
        base.Update();
        if (panZoom != null && statsText != null)
        {
            statsText.Text = $"Zoom: {panZoom.Zoom:F2}x | Pan: <{panZoom.PanPosition.X:F0}, {panZoom.PanPosition.Y:F0}>";
        }
    }

    [Test]
    public void TestProgrammaticManipulation()
    {
        AddStep("Zoom In (Scale = 2.0)", () => panZoom.Zoom = 2.0f);
        AddStep("Zoom Out (Scale = 0.5)", () => panZoom.Zoom = 0.5f);
        AddStep("Pan Right", () => panZoom.PanPosition = new Vector2(300, 0));
        AddStep("Pan Down", () => panZoom.PanPosition = new Vector2(300, 300));
        AddStep("Reset View", () =>
        {
            panZoom.Zoom = 1.0f;
            panZoom.PanPosition = Vector2.Zero;
        });
    }

    private class DummyNode : Container
    {
        public DummyNode()
        {
            Size = new Vector2(100, 60);
            Origin = Anchor.Centre;

            Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = Color.DarkSlateBlue
            });

            Add(new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 20,
                Color = Color.MediumSlateBlue,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft
            });
        }
    }
}
