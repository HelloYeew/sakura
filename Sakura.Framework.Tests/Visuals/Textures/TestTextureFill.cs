// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Textures;

public class TestTextureFill : TestScene
{
    private Sprite imageSprite;

    [Resolved]
    private ITextureManager textureManager { get; set; }

    [Test]
    public void TestSmallSpriteFillModeActualSize()
    {
        addSprite("small.png");
        AddStep("Resize sprite to actual size", () =>
        {
            imageSprite.RelativeSizeAxes = Axes.None;
            imageSprite.Size = new Vector2(100);
        });
        AddStep("Set FillMode to Stretch", () => imageSprite.FillMode = TextureFillMode.Stretch);
        AddStep("Set FillMode to Fit", () => imageSprite.FillMode = TextureFillMode.Fit);
        AddStep("Set FillMode to Fill", () => imageSprite.FillMode = TextureFillMode.Fill);
        AddStep("Set FillMode to Tile", () => imageSprite.FillMode = TextureFillMode.Tile);
    }

    [Test]
    public void TestSmallSpriteFillModeExpanded()
    {
        addSprite("small.png");
        AddStep("Set FillMode to Stretch", () => imageSprite.FillMode = TextureFillMode.Stretch);
        AddStep("Set FillMode to Fit", () => imageSprite.FillMode = TextureFillMode.Fit);
        AddStep("Set FillMode to Fill", () => imageSprite.FillMode = TextureFillMode.Fill);
        AddStep("Set FillMode to Tile", () => imageSprite.FillMode = TextureFillMode.Tile);
    }

    [Test]
    public void TestLargeSpriteFillMode()
    {
        addSprite("large.jpeg");
        AddStep("Set FillMode to Stretch", () => imageSprite.FillMode = TextureFillMode.Stretch);
        AddStep("Set FillMode to Fit", () => imageSprite.FillMode = TextureFillMode.Fit);
        AddStep("Set FillMode to Fill", () => imageSprite.FillMode = TextureFillMode.Fill);
        AddStep("Set FillMode to Tile", () => imageSprite.FillMode = TextureFillMode.Tile);
    }

    private void addSprite(string textureName)
    {
        AddStep("Add sprite", () =>
        {
            imageSprite = new Sprite()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Texture = textureManager.Get(textureName),
                Size = new Vector2(1f),
                RelativeSizeAxes = Axes.Both,
                FillMode = TextureFillMode.Stretch
            };
            Add(imageSprite);
        });
    }
}
