// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.Textures;

public partial class TestTextureAtlas : TestScene
{
    [Resolved]
    private ITextureManager textureManager { get; set; } = null!;

    [Test]
    public void TestSmallTextureIsAtlased()
    {
        Texture small = null!;

        AddStep("Load small.png", () =>
        {
            small = textureManager.Get("small.png");
            Add(new Sprite
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Texture = small,
                Size = new Vector2(234)
            });
        });

        // these assertions only apply when the active manager actually packs into an atlas
        // (i.e. the real GL/Metal managers). The headless manager has no atlas, so skip there.
        AddAssert("texture is in atlas page", () => textureManager.Atlas == null || textureManager.Atlas.OwnsNativeTexture(small.BackendTexture));
        AddAssert("uv rect is a sub-region", () => textureManager.Atlas == null || small.UvRect.Width < 1f || small.UvRect.Height < 1f);
    }

    [Test]
    public void TestLargeTextureFallsBackToStandalone()
    {
        Texture large = null!;

        AddStep("Load large.jpeg", () =>
        {
            large = textureManager.Get("large.jpeg");
            Add(new Sprite
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Texture = large,
                Size = new Vector2(0.8f),
                RelativeSizeAxes = Axes.Both,
                FillMode = TextureFillMode.Fit
            });
        });

        AddAssert("texture is standalone (not atlased)", () => textureManager.Atlas == null || !textureManager.Atlas.OwnsNativeTexture(large.BackendTexture));
        AddAssert("uv rect is full", () => large.UvRect.Width >= 1f && large.UvRect.Height >= 1f);
    }

    [Test]
    public void TestCachingReusesAtlasRegion()
    {
        Texture first = null!;
        Texture second = null!;

        AddStep("Load small.png twice", () =>
        {
            first = textureManager.Get("small.png");
            second = textureManager.Get("small.png");
        });

        AddAssert("same cached texture returned", () => ReferenceEquals(first, second));
        AddAssert("same UV region", () =>
            Precision.AlmostEquals(first.UvRect.X, second.UvRect.X) &&
            Precision.AlmostEquals(first.UvRect.Y, second.UvRect.Y) &&
            Precision.AlmostEquals(first.UvRect.Width, second.UvRect.Width) &&
            Precision.AlmostEquals(first.UvRect.Height, second.UvRect.Height)
        );
    }

    [Test]
    public void TestManyAtlasedSprites()
    {
        AddStep("Add grid of atlased sprites", () =>
        {
            var flow = new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Direction = FlowDirection.Horizontal,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(4)
            };

            for (int i = 0; i < 24; i++)
            {
                flow.Add(new Sprite
                {
                    Texture = textureManager.Get("small.png"),
                    Size = new Vector2(64),
                    FillMode = TextureFillMode.Fit
                });
            }

            Add(flow);
        });
    }

    [Test]
    public void TestAtlasedVsStandalone()
    {
        AddStep("Add comparison", () =>
        {
            var flow = new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Direction = FlowDirection.Horizontal,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(20)
            };

            flow.Add(labelledSprite("small.png (atlased)", "small.png"));
            flow.Add(labelledSprite("large.jpeg (standalone)", "large.jpeg"));

            Add(flow);
        });
    }

    private Container labelledSprite(string label, string textureName)
    {
        var container = new Container
        {
            AutoSizeAxes = Axes.None,
            Size = new Vector2(260, 280)
        };

        container.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = Color.Black,
            Alpha = 0.4f
        });
        container.Add(new Sprite
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Texture = textureManager.Get(textureName),
            Size = new Vector2(1),
            RelativeSizeAxes = Axes.Both,
            FillMode = TextureFillMode.Fit
        });
        container.Add(new SpriteText
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            Text = label,
            Color = Color.White
        });

        return container;
    }
}
