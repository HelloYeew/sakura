// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public class TestDrawVisualiser : TestScene
{
    private DrawVisualiser visualiser;

    [SetUp]
    public void SetUp()
    {
        visualiser = new DrawVisualiser(this)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Depth = float.MaxValue - 30
        };
        AddStep("Add DrawVisualiser", () => Add(visualiser));
        AddStep("Pop in overlay", () => visualiser.ToggleVisibility());
    }

    [Test]
    public void TestDrawable()
    {
        AddStep("Add a box", () => Add(new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Color = Color.Red,
            Size = new Vector2(100),
            Depth = float.MaxValue - 20
        }));
    }

    [Test]
    public void TestPerformance()
    {
        AddStep("Add a lot of boxes", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                Add(new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Color = ColorExtensions.GetRandomColor(true),
                    Size = new Vector2(100),
                    Position = new Vector2(Random.Shared.Next(-400, 400), Random.Shared.Next(-400, 400)),
                    Depth = float.MaxValue - 20
                });
            }
        });
    }
}
