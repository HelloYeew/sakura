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

/// <summary>
/// A container setting its children's alpha after its <c>base.Update()</c>, as one that drives its children does: by then
/// they've had their update for the frame. Taken to 0 they have to go, not stay at the last alpha they drew at.
/// </summary>
public partial class TestAlphaSetByParent : TestScene
{
    private Driver driver = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Show a driver", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(driver = new Driver
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            });
        });
    }

    [Test]
    public void TestDrivenAlpha() => AddSliderStep("Alpha it sets", 0f, 1f, 0.6f, alpha => driver.ChildAlpha = alpha);

    [Test]
    public void TestDrivenToNothing()
    {
        AddStep("Show them", () => driver.ChildAlpha = 0.6f);
        AddUntilStep("They're drawn", () => driver.Disc.DrawAlpha > 0 && driver.Square.DrawAlpha > 0 && driver.Group.DrawAlpha > 0);
        AddStep("Take them to nothing", () => driver.ChildAlpha = 0);
        AddUntilStep("They've gone", () => driver.Disc.DrawAlpha == 0 && driver.Square.DrawAlpha == 0 && driver.Group.DrawAlpha == 0);
        AddStep("Show them again", () => driver.ChildAlpha = 0.6f);
        AddUntilStep("They're back", () => driver.Disc.DrawAlpha > 0 && driver.Square.DrawAlpha > 0 && driver.Group.DrawAlpha > 0);
    }

    private partial class Driver : Container
    {
        public readonly Circle Disc;
        public readonly Box Square;
        public readonly Container Group;

        public float ChildAlpha { get; set; } = 0.6f;

        public Driver()
        {
            Size = new Vector2(600, 200);

            Children = new Drawable[]
            {
                Disc = new Circle
                {
                    Size = new Vector2(160),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Color = Color.White
                },
                Square = new Box
                {
                    Size = new Vector2(160),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Color = ColorExtensions.FromHex("#FFB7D5")
                },
                Group = new Container
                {
                    Size = new Vector2(160),
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#C9A7F0") }
                }
            };
        }

        public override void Update()
        {
            base.Update();

            Disc.Alpha = Square.Alpha = Group.Alpha = ChildAlpha;
        }
    }
}
