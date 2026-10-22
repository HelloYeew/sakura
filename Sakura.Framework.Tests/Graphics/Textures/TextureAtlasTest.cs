// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Tests.Graphics.Textures;

/// <summary>
/// Handing the atlas a pixel buffer, as a glyph does, rather than pixels for it to copy.
/// </summary>
[TestFixture]
public class TextureAtlasTest
{
    private HeadlessTextureManager textureManager = null!;
    private TextureAtlas atlas = null!;

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        atlas = new TextureAtlas(new HeadlessRenderer(textureManager), 64, 64);
    }

    [TearDown]
    public void TearDown()
    {
        atlas.Dispose();
        textureManager.Dispose();
    }

    [Test]
    public void AHandedOverBufferBecomesARegion()
    {
        var pixels = ImageRawData.Rent(8, 4);

        var region = atlas.AddRegion(8, 4, pixels);

        Assert.Multiple(() =>
        {
            Assert.That(region, Is.Not.Null);
            Assert.That(region!.Width, Is.EqualTo(8));
            Assert.That(region.Height, Is.EqualTo(4));
        });
    }

    /// <summary>
    /// A region too big for a page isn't added, and the buffer goes back all the same: the caller handed it over.
    /// </summary>
    [Test]
    public void ABufferTooBigForAPageIsStillLetGo()
    {
        var pixels = ImageRawData.Rent(128, 128);

        var region = atlas.AddRegion(128, 128, pixels);

        Assert.Multiple(() =>
        {
            Assert.That(region, Is.Null);
            Assert.That(pixels.IsValid, Is.False, "released");
        });
    }
}
