// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

public partial class TestShearedMaskingDrawSizePreservingFillContainer : TestScene
{
    private DrawSizePreservingFillContainer mainContainer = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Clear screen", Clear);
        AddStep("Add draw preserved container", () =>
        {
            mainContainer = new DrawSizePreservingFillContainer()
            {
                RelativeSizeAxes = Axes.Both,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.LightGreen
                }
            };
            Add(mainContainer);
        });
    }

    [Test]
    public void TestShearedRoundedMasking()
    {
        AddStep("Add sheared rounded container", () =>
        {
            mainContainer.Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(700, 300),
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
        addMainContainerSliderStep();
    }

    [Test]
    public void TestShearedBorder()
    {
        AddStep("Add sheared container with border", () =>
        {
            mainContainer.Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(700, 300),
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
        addMainContainerSliderStep();
    }

    [Test]
    public void TestShearedCircle()
    {
        AddStep("Add sheared circle", () =>
        {
            mainContainer.Add(new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(400, 400),
                Shear = new Vector2(0.4f, 0),
                Color = Color.MediumPurple
            });
        });
        addMainContainerSliderStep();
    }

    [Test]
    public void TestChildClippingInsideShear()
    {
        AddStep("Add sheared mask with overflowing child", () =>
        {
            mainContainer.Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(700, 300),
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
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(1.5f, 0.5f),
                        Color = Color.LightGreen
                    }
                }
            });
        });
        addMainContainerSliderStep();
    }

    private void addMainContainerSliderStep()
    {
        AddSliderStep("Width", 0, 1920, 800, width => mainContainer.TargetDrawSize = mainContainer.TargetDrawSize with
        {
            X = width
        });
        AddSliderStep("Height", 0, 1080, 600, height => mainContainer.TargetDrawSize = mainContainer.TargetDrawSize with
        {
            Y = height
        });
    }
}
