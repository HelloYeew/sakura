// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

/// <summary>
/// Side-by-side comparison of a plain <see cref="Container"/> and a <see cref="BufferedContainer"/>
/// with identical content. Fading the plain container shows seams where translucent children
/// overlap; the buffered container fades as one flattened image. The scale slider makes the
/// offscreen buffer resolution visible (pixelation at low values).
/// </summary>
public partial class TestBufferedContainer : TestScene
{
    private Container plainContainer = null!;
    private BufferedContainer bufferedContainer = null!;
    private Box plainMover = null!;
    private Box bufferedMover = null!;

    private bool animate = true;
    private double animationTime;

    [SetUp]
    public void SetUp()
    {
        AddStep("Create comparison", () =>
        {
            Clear();
            animationTime = 0;

            Add(new SpriteText
            {
                Text = "Plain Container (fade shows overlap seams)",
                Position = new Vector2(40, 40),
                Color = Color.White
            });

            Add(new SpriteText
            {
                Text = "BufferedContainer (fades as one image)",
                Position = new Vector2(440, 40),
                Color = Color.White
            });

            plainContainer = new Container { Position = new Vector2(40, 80) };
            bufferedContainer = new BufferedContainer { Position = new Vector2(440, 80) };

            plainMover = populateContent(plainContainer);
            bufferedMover = populateContent(bufferedContainer);

            Add(plainContainer);
            Add(bufferedContainer);
        });
    }

    /// <summary>
    /// Overlapping translucent boxes plus text — content where per-child fading visibly
    /// differs from composite fading. Returns the box that gets animated.
    /// </summary>
    private static Box populateContent(Container container)
    {
        container.Size = new Vector2(320, 320);

        container.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = Color.DarkSlateGray
        });

        container.Add(new Box
        {
            Position = new Vector2(30, 30),
            Size = new Vector2(180, 180),
            Color = Color.FromArgb(200, 220, 80, 80)
        });

        container.Add(new Box
        {
            Position = new Vector2(110, 110),
            Size = new Vector2(180, 180),
            Color = Color.FromArgb(200, 80, 120, 220)
        });

        var mover = new Box
        {
            Position = new Vector2(70, 230),
            Size = new Vector2(60, 60),
            Color = Color.FromArgb(200, 120, 220, 120)
        };
        container.Add(mover);

        container.Add(new SpriteText
        {
            Text = "Overlapping content",
            Position = new Vector2(20, 15),
            Color = Color.White
        });

        return mover;
    }

    [Test]
    public void TestComparison()
    {
        AddSliderStep("Alpha (both)", 0f, 1f, 1f, a =>
        {
            if (plainContainer != null) plainContainer.Alpha = a;
            if (bufferedContainer != null) bufferedContainer.Alpha = a;
        });

        AddSliderStep("FrameBufferScale", 0.05f, 2f, 1f, s =>
        {
            if (bufferedContainer != null)
                bufferedContainer.FrameBufferScale = new Vector2(s);
        });

        AddSliderStep("Blur sigma (both axes)", 0f, 20f, 0f, sigma =>
        {
            if (bufferedContainer != null)
                bufferedContainer.BlurSigma = new Vector2(sigma);
        });

        AddStep("Blur horizontal only (motion-blur look)", () =>
        {
            if (bufferedContainer != null)
                bufferedContainer.BlurSigma = new Vector2(12, 0);
        });

        AddSliderStep("Blur rotation (deg)", 0f, 180f, 0f, r =>
        {
            if (bufferedContainer != null)
                bufferedContainer.BlurRotation = r;
        });

        AddStep("Clear blur", () =>
        {
            if (bufferedContainer != null)
                bufferedContainer.BlurSigma = Vector2.Zero;
        });

        AddSliderStep("Grayscale strength", 0f, 1f, 0f, g =>
        {
            if (bufferedContainer != null)
                bufferedContainer.GrayscaleStrength = g;
        });

        AddStep("Glow (blur + additive + original)", () =>
        {
            if (bufferedContainer == null) return;

            bufferedContainer.BlurSigma = new Vector2(8);
            bufferedContainer.EffectBlending = BlendingMode.Additive;
            bufferedContainer.EffectColor = Color.Gold;
            bufferedContainer.EffectPlacement = EffectPlacement.Behind;
            bufferedContainer.DrawOriginal = true;
        });

        AddStep("Veil (effect in front)", () =>
        {
            if (bufferedContainer == null) return;

            bufferedContainer.EffectPlacement = EffectPlacement.InFront;
        });

        AddStep("Reset effects", () =>
        {
            if (bufferedContainer == null) return;

            bufferedContainer.BlurSigma = Vector2.Zero;
            bufferedContainer.BlurRotation = 0;
            bufferedContainer.GrayscaleStrength = 0;
            bufferedContainer.EffectBlending = null;
            bufferedContainer.EffectColor = Color.White;
            bufferedContainer.EffectPlacement = EffectPlacement.Behind;
            bufferedContainer.DrawOriginal = false;
        });

        AddStep("Background color dark red", () =>
        {
            if (bufferedContainer != null)
                bufferedContainer.BackgroundColor = Color.DarkRed;
        });

        AddStep("Background transparent", () =>
        {
            if (bufferedContainer != null)
                bufferedContainer.BackgroundColor = default;
        });

        AddStep("Toggle animation", () => animate = !animate);
        AddStep("Toggle cache (static content only redraws on change)", () =>
        {
            if (bufferedContainer != null)
                bufferedContainer.CacheDrawnFrameBuffer = !bufferedContainer.CacheDrawnFrameBuffer;
        });

        AddStep("Tint composite goldenrod", () =>
        {
            if (bufferedContainer != null)
                bufferedContainer.Color = Color.Goldenrod;
        });
        AddStep("Reset tint", () =>
        {
            if (bufferedContainer != null)
                bufferedContainer.Color = Color.White;
        });
    }

    public override void Update()
    {
        base.Update();

        if (plainMover == null || !animate)
            return;

        animationTime += Clock.ElapsedFrameTime;

        // Bounce a box horizontally inside both panels so redraw behavior is visible.
        float x = 30 + 200 * (0.5f + 0.5f * (float)Math.Sin(animationTime / 600.0));
        plainMover.Position = new Vector2(x, plainMover.Position.Y);
        bufferedMover.Position = new Vector2(x, bufferedMover.Position.Y);
    }
}
