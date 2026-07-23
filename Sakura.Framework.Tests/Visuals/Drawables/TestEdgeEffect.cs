// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

public partial class TestEdgeEffect : TestScene
{
    private Container target = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Create container", () =>
        {
            target = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(200),
                CornerRadius = 20,
                Masking = true,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.SlateGray,
                }
            };
        });

        AddStep("Add container", () => Add(target));
    }

    [Test]
    public void TestShadow()
    {
        AddStep("Apply drop shadow", () => target.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Shadow,
            Color = Color.FromArgb(160, Color.Black),
            Radius = 25,
            Offset = new Vector2(0, 8),
        });

        AddAssert("Edge effect is a shadow", () => target.EdgeEffect.Type == EdgeEffectType.Shadow);
    }

    [Test]
    public void TestGlow()
    {
        AddStep("Apply glow", () => target.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Color = Color.FromArgb(200, Color.Cyan),
            Radius = 30,
        });

        AddAssert("Edge effect is a glow", () => target.EdgeEffect.Type == EdgeEffectType.Glow);
    }

    [Test]
    public void TestAnimatedGlowRadius()
    {
        AddStep("Set up glow", () => target.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Color = Color.FromArgb(220, Color.Magenta),
            Radius = 0,
        });

        AddStep("Grow glow radius", () => target.TweenEdgeEffectRadiusTo(40, 600, Easing.OutQuint));
        AddWaitStep("Watch it grow", 600);
        AddStep("Shrink glow radius", () => target.TweenEdgeEffectRadiusTo(5, 600, Easing.InQuint));
        AddWaitStep("Watch it shrink", 600);
        AddAssert("Radius shrank", () => target.EdgeEffect.Radius <= 6);
    }

    [Test]
    public void TestHollowGlow()
    {
        AddStep("Apply hollow glow (outline)", () => target.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Color = Color.FromArgb(220, Color.Lime),
            Radius = 18,
            Hollow = true,
        });

        AddAssert("Edge effect is hollow", () => target.EdgeEffect.Hollow);
    }

    [Test]
    public void TestFadeEdgeEffect()
    {
        AddStep("Apply opaque shadow", () => target.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Shadow,
            Color = Color.FromArgb(255, Color.Black),
            Radius = 25,
        });

        AddStep("Fade edge effect out", () => target.FadeEdgeEffectTo(0f, 800));
        AddWaitStep("Wait for fade", 800);
        AddAssert("Edge effect alpha near zero", () => target.EdgeEffect.Color.A <= 1);
    }

    private bool glow = true;
    private float radius = 20;
    private float roundness;
    private float offsetX;
    private float offsetY;
    private float cornerRadius = 20;
    private float alpha = 0.8f;

    private void applyEdgeEffect() => target.EdgeEffect = new EdgeEffectParameters
    {
        Type = glow ? EdgeEffectType.Glow : EdgeEffectType.Shadow,
        Color = Color.FromArgb((int)Math.Clamp(alpha * 255f, 0f, 255f), glow ? Color.Lime : Color.Black),
        Radius = radius,
        Roundness = roundness,
        Offset = new Vector2(offsetX, offsetY),
    };

    [Test]
    public void TestInteractive()
    {
        AddStep("Apply initial glow", applyEdgeEffect);

        AddStep("Toggle glow/shadow", () =>
        {
            glow = !glow;
            applyEdgeEffect();
        });

        AddSliderStep("Radius", 0f, 80f, radius, v =>
        {
            radius = v;
            applyEdgeEffect();
        });

        AddSliderStep("Roundness", 0f, 80f, roundness, v =>
        {
            roundness = v;
            applyEdgeEffect();
        });

        AddSliderStep("Offset X", -60f, 60f, offsetX, v =>
        {
            offsetX = v;
            applyEdgeEffect();
        });

        AddSliderStep("Offset Y", -60f, 60f, offsetY, v =>
        {
            offsetY = v;
            applyEdgeEffect();
        });

        AddSliderStep("Alpha", 0f, 1f, alpha, v =>
        {
            alpha = v;
            applyEdgeEffect();
        });

        AddSliderStep("Container corner radius", 0f, 100f, cornerRadius, v =>
        {
            cornerRadius = v;
            target.CornerRadius = v;
        });
    }

    [TearDown]
    public void TearDown()
    {
        AddStep("Clear all children", Clear);
    }
}
