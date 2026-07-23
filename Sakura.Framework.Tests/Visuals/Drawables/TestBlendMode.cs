// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

/// <summary>
/// Visual test for <see cref="Drawable.Blending"/> across all <see cref="BlendingMode"/>s, for both
/// the white-pixel path (a solid <see cref="Box"/>) and the textured path (a <see cref="Sprite"/>).
/// Each foreground overlaps a colorful background so the blend math is visible (Additive brightens,
/// Multiply/Screen tint, Opaque overwrites, etc.).
/// </summary>
public partial class TestBlendMode : TestScene
{
    [Resolved]
    private ITextureManager textureManager { get; set; } = null!;

    private static readonly BlendingMode[] all_modes =
    {
        BlendingMode.Alpha,
        BlendingMode.Additive,
        BlendingMode.Opaque,
        BlendingMode.Multiply,
        BlendingMode.Screen,
        BlendingMode.Premultiplied,
    };

    [SetUp]
    public void SetUp() => AddStep("Clear", Clear);

    /// <summary>
    /// A colorful background (red/green/blue bands + a white stripe) so any blend mode shows a
    /// distinct, readable result where the foreground overlaps it.
    /// </summary>
    private void addBackground()
    {
        AddStep("Add background", () =>
        {
            var bg = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(420, 280),
            };

            bg.Add(new Box { RelativeSizeAxes = Axes.Both, Color = Color.Black });
            bg.Add(new Box { RelativeSizeAxes = Axes.Both, Width = 1f / 3f, Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Color = Color.Red });
            bg.Add(new Box { RelativeSizeAxes = Axes.Both, Width = 1f / 3f, Anchor = Anchor.Centre, Origin = Anchor.Centre, Color = Color.Lime });
            bg.Add(new Box { RelativeSizeAxes = Axes.Both, Width = 1f / 3f, Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight, Color = Color.Blue });
            bg.Add(new Box { RelativeSizeAxes = Axes.X, Height = 40, Anchor = Anchor.Centre, Origin = Anchor.Centre, Color = Color.White });

            Add(bg);
        });
    }

    /// <summary>
    /// White-pixel (solid color) foreground for every blend mode, as a labelled column.
    /// </summary>
    [Test]
    public void TestWhitePixelAllModes()
    {
        addBackground();
        AddStep("Add boxes (one per mode)", () => Add(buildModeRow(textured: false)));
    }

    /// <summary>
    /// Textured foreground (small.png) for every blend mode.
    /// </summary>
    [Test]
    public void TestTexturedAllModes()
    {
        addBackground();
        AddStep("Add sprites (one per mode)", () => Add(buildModeRow(textured: true)));
    }

    /// <summary>
    /// Switch a single overlapping box's blend mode live, one mode per <see cref="BlendingMode"/>.
    /// </summary>
    [TestCaseSource(nameof(all_modes))]
    public void TestWhitePixelSingle(BlendingMode mode)
    {
        addBackground();

        Box box = null!;
        AddStep("Add overlapping box", () => Add(box = new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(220, 180),
            Color = Color.White,
            Alpha = 0.8f
        }));

        AddStep($"Blending = {mode}", () => box.Blending = mode);
    }

    /// <summary>
    /// Switch a single overlapping sprite's blend mode live, one mode per <see cref="BlendingMode"/>.
    /// </summary>
    [TestCaseSource(nameof(all_modes))]
    public void TestTexturedSingle(BlendingMode mode)
    {
        addBackground();

        Sprite sprite = null!;
        AddStep("Add overlapping sprite", () => Add(sprite = new Sprite
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(220, 180),
            Texture = textureManager.Get("small.png"),
            FillMode = TextureFillMode.Fill
        }));

        AddStep($"Blending = {mode}", () => sprite.Blending = mode);
    }

    /// <summary>
    /// Interactive: a slider-free pair of buttons to cycle blend modes on both drawable types.
    /// </summary>
    [Test]
    public void TestInteractive()
    {
        addBackground();

        Box box = null!;
        Sprite sprite = null!;

        AddStep("Add overlapping box + sprite", () =>
        {
            Add(box = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                X = -70,
                Size = new Vector2(160, 200),
                Color = Color.White,
                Alpha = 0.85f
            });

            Add(sprite = new Sprite
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                X = 70,
                Size = new Vector2(160, 200),
                Texture = textureManager.Get("small.png"),
                FillMode = TextureFillMode.Fill
            });
        });

        foreach (var mode in all_modes)
        {
            var m = mode;
            AddStep($"Set both → {m}", () =>
            {
                box.Blending = m;
                sprite.Blending = m;
            });
        }
    }

    /// <summary>
    /// Builds a horizontal row of <paramref name="textured"/> (Sprite) or solid (Box) drawables — one
    /// per blend mode, each labelled — overlapping the background area so the blend result is visible.
    /// </summary>
    private Container buildModeRow(bool textured)
    {
        var flow = new FlowContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Direction = FlowDirection.Horizontal,
            AutoSizeAxes = Axes.Both,
            Spacing = new Vector2(8, 0)
        };

        foreach (var mode in all_modes)
        {
            var cell = new Container
            {
                AutoSizeAxes = Axes.None,
                Size = new Vector2(60, 240),
            };

            Drawable fg = textured
                ? new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Texture = textureManager.Get("small.png"),
                    FillMode = TextureFillMode.Fill,
                    Blending = mode
                }
                : new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.White,
                    Alpha = 0.85f,
                    Blending = mode
                };

            cell.Add(fg);
            cell.Add(new SpriteText
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Text = mode.ToString(),
                Color = Color.White
            });

            flow.Add(cell);
        }

        return flow;
    }
}
