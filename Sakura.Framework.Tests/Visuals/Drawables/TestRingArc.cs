// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

/// <summary>
/// Rings side by side, stepped edges against smooth ones: zoom in to see the difference.
/// </summary>
public partial class TestRingArc : TestScene
{
    private Container zoom = null!;
    private RingArc plain = null!;
    private RingArc smooth = null!;
    private RingArc thinPlain = null!;
    private RingArc thinSmooth = null!;

    private RingArc[] rings => new[] { plain, smooth, thinPlain, thinSmooth };

    [SetUp]
    public void SetUp()
    {
        AddStep("Show rings", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(zoom = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Child = new FlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Horizontal,
                    Spacing = new Vector2(60, 0),
                    Children = new Drawable[]
                    {
                        column("Plain", plain = arc(false, 0.86f), thinPlain = arc(false, 0.97f)),
                        column("Smooth", smooth = arc(true, 0.86f), thinSmooth = arc(true, 0.97f))
                    }
                }
            });
        });
    }

    [Test]
    public void TestEdges()
    {
        AddSliderStep("Zoom", 0.5f, 6f, 1f, scale => zoom.Scale = new Vector2(scale));
        AddSliderStep("Start", 0f, 1f, 0.1f, start =>
        {
            foreach (RingArc ring in rings)
                ring.Start = start;
        });
        AddSliderStep("End", 0f, 1f, 0.82f, end =>
        {
            foreach (RingArc ring in rings)
                ring.Progress = end;
        });
        AddSliderStep("Thickness (inner radius)", 0f, 0.995f, 0.86f, inner =>
        {
            plain.InnerRadius = inner;
            smooth.InnerRadius = inner;
        });
        AddAssert("Smooth edges take three times the vertices", () => smooth.VertexCount == plain.VertexCount * 3);
    }

    /// <summary>
    /// The thick smooth ring, told to start where it ends and then past it, is gone (nothing to draw) until it's given an
    /// arc again.
    /// </summary>
    [Test]
    public void TestAnArcWithNothingToDraw()
    {
        AddStep("Start where it ends", () =>
        {
            smooth.Start = 0.5f;
            smooth.Progress = 0.5f;
        });
        AddUntilStep("Nothing to draw", () => smooth.VertexCount == 0);
        AddStep("Start past where it ends", () => smooth.Start = 0.7f);
        AddAssert("Still nothing", () => smooth.VertexCount == 0);
        AddStep("An arc again", () =>
        {
            smooth.Start = 0.1f;
            smooth.Progress = 0.82f;
        });
        AddUntilStep("Back", () => smooth.VertexCount > 0);
    }

    /// <summary>
    /// A fade scales the ring's vertices rather than building them again.
    /// </summary>
    [Test]
    public void TestFade()
    {
        AddSliderStep("Alpha", 0f, 1f, 1f, alpha =>
        {
            foreach (RingArc ring in rings)
                ring.Alpha = alpha;
        });
        AddStep("Fade out and in", () =>
        {
            foreach (RingArc ring in rings)
                ring.FadeOut(500).Then().FadeIn(500);
        });
        AddWaitStep("Let it run", 1100);
        AddUntilStep("Back to full", () => plain.DrawAlpha == 1);
    }

    private static RingArc arc(bool smoothEdges, float innerRadius) => new RingArc
    {
        Anchor = Anchor.TopCentre,
        Origin = Anchor.TopCentre,
        Size = new Vector2(300),
        Start = 0.1f,
        Progress = 0.82f,
        InnerRadius = innerRadius,
        SmoothEdges = smoothEdges,
        Color = ColorExtensions.FromHex("#FFB7D5")
    };

    private static Drawable column(string title, RingArc thick, RingArc thin) => new FlowContainer
    {
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 20),
        Children = new Drawable[]
        {
            new SpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Text = title,
                Color = Color.White
            },
            thick,
            thin
        }
    };
}
