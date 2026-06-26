// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.Containers;

public partial class TestRelativeChildSpace : TestScene
{
    private Container container = null!;
    private Box relativeChild = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Create container with relative child", () =>
        {
            container = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(400),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = Color.DarkSlateGray
                    },
                    relativeChild = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        RelativePositionAxes = Axes.Both,
                        Size = new Vector2(0.5f),
                        Position = new Vector2(0.25f),
                        Color = Color.OrangeRed
                    }
                }
            };
        });

        AddStep("Add container", () => Add(container));
    }

    [Test]
    public void TestDefaultRelativeSpace()
    {
        AddUntilStep("Child resolves to 200x200", () => Precision.AlmostEquals(relativeChild.DrawSize, new Vector2(200), 0.5f));

        AddAssert("RelativeChildSize defaults to One", () => container.RelativeChildSize == Vector2.One);
        AddAssert("RelativeChildOffset defaults to Zero", () => container.RelativeChildOffset == Vector2.Zero);
    }

    [Test]
    public void TestRelativeChildSize()
    {
        AddUntilStep("Child starts at 200x200", () => Precision.AlmostEquals(relativeChild.DrawSize, new Vector2(200), 0.5f));

        AddStep("Set RelativeChildSize to (2,2)", () => container.RelativeChildSize = new Vector2(2));
        AddUntilStep("Child shrinks to 100x100", () => Precision.AlmostEquals(relativeChild.DrawSize, new Vector2(100), 0.5f));

        AddStep("Set RelativeChildSize to (0.5,0.5)", () => container.RelativeChildSize = new Vector2(0.5f));
        AddUntilStep("Child grows to 400x400", () => Precision.AlmostEquals(relativeChild.DrawSize, new Vector2(400), 0.5f));

        AddStep("Reset RelativeChildSize", () => container.RelativeChildSize = Vector2.One);
        AddUntilStep("Child back to 200x200", () => Precision.AlmostEquals(relativeChild.DrawSize, new Vector2(200), 0.5f));
    }

    [Test]
    public void TestRelativeChildOffset()
    {
        RectangleF beforeOffset = default;
        RectangleF parentRect = default;
        AddStep("Capture child & parent screen rects", () =>
        {
            beforeOffset = relativeChild.DrawRectangle;
            parentRect = container.DrawRectangle;
        });
        
        AddStep("Set RelativeChildOffset to (0.25,0.25)", () => container.RelativeChildOffset = new Vector2(0.25f));
        AddUntilStep("Child size unchanged at 200x200", () => Precision.AlmostEquals(relativeChild.DrawSize, new Vector2(200), 0.5f));
        AddUntilStep("Child moved up-left by 0.25 of parent extent", () =>
            Precision.AlmostEquals(relativeChild.DrawRectangle.X, beforeOffset.X - 0.25f * parentRect.Width, 1f) &&
            Precision.AlmostEquals(relativeChild.DrawRectangle.Y, beforeOffset.Y - 0.25f * parentRect.Height, 1f));

        AddStep("Reset RelativeChildOffset", () => container.RelativeChildOffset = Vector2.Zero);
        AddUntilStep("Child returns to original position", () =>
            Precision.AlmostEquals(relativeChild.DrawRectangle.X, beforeOffset.X, 1f) &&
            Precision.AlmostEquals(relativeChild.DrawRectangle.Y, beforeOffset.Y, 1f));
    }

    [Test]
    public void TestNonZeroValidation()
    {
        AddAssert("Zero RelativeChildSize throws", () =>
        {
            try
            {
                container.RelativeChildSize = new Vector2(0, 1);
                return false;
            }
            catch (System.ArgumentException)
            {
                return true;
            }
        });
    }

    [TearDown]
    public void TearDown()
    {
        AddStep("Clear all children", Clear);
    }
}
