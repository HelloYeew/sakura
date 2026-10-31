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
/// A drawable turned and scaled about any point: the dot marks it, and stays under it however the box moves.
/// </summary>
public partial class TestCustomOrigin : TestScene
{
    private static readonly Color[] petal_colors =
    {
        ColorExtensions.FromHex("#FFB7D5"),
        ColorExtensions.FromHex("#F48FB1"),
        ColorExtensions.FromHex("#E55F9A"),
        ColorExtensions.FromHex("#C9A7F0"),
        ColorExtensions.FromHex("#FFD27F")
    };

    private Box box = null!;
    private Vector2 point = new Vector2(0.5f, 0.85f);

    [SetUp]
    public void SetUp()
    {
        AddStep("Clear", () => Clear());
    }

    [Test]
    public void TestAroundAnyPoint()
    {
        AddStep("Show a box", () =>
        {
            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(box = new Box
            {
                Anchor = Anchor.Centre,
                Size = new Vector2(160, 240),
                Color = ColorExtensions.FromHex("#E55F9A"),
                CustomOrigin = point
            });
            Add(new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(10),
                Color = Color.White
            });
        });
        AddSliderStep("Point across", -1f, 2f, 0.5f, x =>
        {
            point = new Vector2(x, point.Y);
            box.CustomOrigin = point;
        });
        AddSliderStep("Point down", -1f, 2f, 0.85f, y =>
        {
            point = new Vector2(point.X, y);
            box.CustomOrigin = point;
        });
        AddSliderStep("Rotation", 0f, 360f, 0f, value => box.Rotation = value);
        AddSliderStep("Scale", 0.25f, 3f, 1f, value => box.Scale = new Vector2(value));
        AddSliderStep("Shear", -1f, 1f, 0f, value => box.Shear = new Vector2(value, 0));
        AddAssert("The dot's still under its point", () =>
        {
            Vector2 at = Vector2.Transform(point, box.ModelMatrix);
            Vector2 middle = Vector2.Transform(new Vector2(0.5f), box.Parent!.ModelMatrix);
            return Vector2.Distance(at, middle) < 0.01f;
        });
    }

    /// <summary>
    /// Five petals turned about their common base, the way a flower's are.
    /// </summary>
    [Test]
    public void TestAFlower()
    {
        Box[] petals = new Box[petal_colors.Length];

        AddStep("Show a flower", () =>
        {
            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });

            for (int i = 0; i < petals.Length; i++)
            {
                Add(petals[i] = new Box
                {
                    Anchor = Anchor.Centre,
                    Size = new Vector2(60, 150),
                    Color = petal_colors[i],
                    Alpha = 0.8f,
                    CustomOrigin = new Vector2(0.5f, 1)
                });
            }
        });
        AddSliderStep("Open", 0f, 1f, 1f, open =>
        {
            for (int i = 0; i < petals.Length; i++)
                petals[i].Rotation = i * 72 * open;
        });
        AddSliderStep("Size", 0.25f, 2f, 1f, size =>
        {
            foreach (Box petal in petals)
                petal.Scale = new Vector2(size);
        });
    }
}
