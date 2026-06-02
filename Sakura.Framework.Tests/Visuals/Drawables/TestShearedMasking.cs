// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

public class TestShearedMasking : TestScene
{
    [SetUp]
    public void SetUp()
    {
        AddStep("Clear screen", Clear);
    }

    [Test]
    public void TestShearedRoundedMasking()
    {
        AddStep("Add sheared rounded container", () =>
        {
            Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(300, 100),
                Shear = new Vector2(0.5f, 0),
                Masking = true,
                CornerRadius = 30,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = ColorExtensions.FromHex("#A67070")
                    }
                }
            });
        });
    }

    [Test]
    public void TestShearedBorder()
    {
        AddStep("Add sheared container with border", () =>
        {
            Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(300, 100),
                Shear = new Vector2(-0.5f, 0),
                Masking = true,
                CornerRadius = 30,
                BorderThickness = 8,
                BorderColor = Color.White,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = ColorExtensions.FromHex("#6F8BA6")
                    }
                }
            });
        });
    }

    [Test]
    public void TestShearedCircle()
    {
        AddStep("Add sheared circle", () =>
        {
            Add(new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(200, 200),
                Shear = new Vector2(0.4f, 0),
                Color = Color.MediumPurple
            });
        });
    }

    [Test]
    public void TestChildClippingInsideShear()
    {
        AddStep("Add sheared mask with overflowing child", () =>
        {
            Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(300, 100),
                Shear = new Vector2(0.5f, 0),
                Masking = true,
                CornerRadius = 25,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = Color.DarkGray
                    },
                    new Box
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(400, 50),
                        Color = Color.LightGreen
                    }
                }
            });
        });
    }
}
