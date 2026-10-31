// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

/// <summary>
/// A graph's marks, all in one draw: dots, rings, solid and dashed lines, an area, boxes and triangles.
/// </summary>
public partial class TestPlotCanvas : TestScene
{
    private static readonly Color bloom = ColorExtensions.FromHex("#E55F9A");
    private static readonly Color lilac = ColorExtensions.FromHex("#A07FD6");
    private static readonly Color amber = ColorExtensions.FromHex("#E8A33D");
    private static readonly Color red = ColorExtensions.FromHex("#D9434F");
    private static readonly Color ink = ColorExtensions.FromHex("#3B324A");

    private PlotCanvas canvas = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Show a canvas", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#B4B4C4") });
            Add(canvas = new PlotCanvas
            {
                RelativeSizeAxes = Axes.None,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(600, 300)
            });
        });
    }

    [Test]
    public void TestEveryMark()
    {
        AddStep("Draw one of each", drawEveryMark);
        AddAssert("Six marks and twenty dots", () => canvas.Count == 26);
        AddSliderStep("Zoom", 0.5f, 4f, 1f, zoom => canvas.Scale = new Vector2(zoom));
    }

    /// <summary>
    /// Thousands of dots in one draw, as a timing graph of a whole song has.
    /// </summary>
    [Test]
    public void TestManyDots()
    {
        AddStep("Plot 2000 dots", () =>
        {
            var random = new Random(1);
            canvas.Clear();

            for (int i = 0; i < 2000; i++)
            {
                float spread = (float)(random.NextDouble() + random.NextDouble() - 1) * 120;
                canvas.Dot(new Vector2(i * 0.3f, 150 + spread), 2, Math.Abs(spread) < 40 ? bloom : Math.Abs(spread) < 80 ? lilac : amber);
            }
        });
        AddAssert("2000 marks", () => canvas.Count == 2000);
        AddSliderStep("Zoom", 0.5f, 4f, 1f, zoom => canvas.Scale = new Vector2(zoom));
    }

    /// <summary>
    /// A line's dashes keep their pattern along it, round its corners.
    /// </summary>
    [Test]
    public void TestDashes()
    {
        AddStep("Dash a bent line", () =>
        {
            canvas.Clear();
            canvas.Line(Enumerable.Range(0, 61).Select(i => new Vector2(i * 10, 150 + 100 * MathF.Sin(i / 4f))).ToList(), 2, ink, 8, 6);
            canvas.Line(new Vector2(0, 280), new Vector2(600, 280), 3, red, 20, 4);
        });
        AddAssert("Two marks", () => canvas.Count == 2);
    }

    /// <summary>
    /// A fade scales the marks' vertices rather than building them again, however many there are.
    /// </summary>
    [Test]
    public void TestFade()
    {
        AddStep("Draw one of each", drawEveryMark);
        AddSliderStep("Alpha", 0f, 1f, 1f, alpha => canvas.Alpha = alpha);
        AddStep("Fade out and in", () => canvas.FadeOut(500).Then().FadeIn(500));
    }

    [Test]
    public void TestClearing()
    {
        AddStep("Draw one of each", drawEveryMark);
        AddStep("Clear it", () => canvas.Clear());
        AddAssert("Nothing there", () => canvas.Count == 0);
        AddUntilStep("No vertices", () => canvas.VertexCount == 0);
    }

    private void drawEveryMark()
    {
        canvas.Clear();
        canvas.Box(new RectangleF(0, 0, 600, 300), Color.White.WithAlpha(0.3f));
        canvas.Area(Enumerable.Range(0, 61).Select(i => new Vector2(i * 10, 260 - 40 * MathF.Abs(MathF.Sin(i / 6f)))).ToList(), 300, amber.WithAlpha(0.5f));
        canvas.Line(Enumerable.Range(0, 61).Select(i => new Vector2(i * 10, 150 + 60 * MathF.Sin(i / 5f))).ToList(), 2.6f, ink);
        canvas.Line(new Vector2(0, 50), new Vector2(600, 50), 2, red, 6, 5);

        for (int i = 0; i < 20; i++)
            canvas.Dot(new Vector2(20 + i * 29, 100), 1 + i * 0.4f, bloom);

        canvas.Ring(new Vector2(300, 220), 12, 2.5f, lilac);
        canvas.Triangle(new Vector2(500, 230), new Vector2(520, 230), new Vector2(510, 212), red);
    }
}
