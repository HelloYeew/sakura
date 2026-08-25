// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests for <see cref="TextureCreationOptions.ShareKeyFor"/>.
/// </summary>
/// <remarks>
/// A share key that collides across decode sizes is the worst possible bug in the sharing path: the
/// second caller silently gets an image at the wrong resolution, and nothing reports it.
/// </remarks>
[TestFixture]
public class TextureCreationOptionsTest
{
    [Test]
    public void SameSourceAndSizeSharesAKey()
    {
        string a = TextureCreationOptions.ShareKeyFor("cover-1", new Vector2(320, 180), TextureFillMode.Fill);
        string b = TextureCreationOptions.ShareKeyFor("cover-1", new Vector2(320, 180), TextureFillMode.Fill);

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void DifferentDecodeSizesDoNotShare()
    {
        string small = TextureCreationOptions.ShareKeyFor("cover-1", new Vector2(320, 180), TextureFillMode.Fill);
        string large = TextureCreationOptions.ShareKeyFor("cover-1", new Vector2(1920, 1080), TextureFillMode.Fill);

        Assert.That(small, Is.Not.EqualTo(large));
    }

    [Test]
    public void DifferentFillModesDoNotShare()
    {
        // Fill centre-crops before scaling, so the pixels genuinely differ from a Fit of the same source.
        string fill = TextureCreationOptions.ShareKeyFor("cover-1", new Vector2(320, 180), TextureFillMode.Fill);
        string fit = TextureCreationOptions.ShareKeyFor("cover-1", new Vector2(320, 180), TextureFillMode.Fit);

        Assert.That(fill, Is.Not.EqualTo(fit));
    }

    [Test]
    public void DifferentSourcesDoNotShare()
    {
        string one = TextureCreationOptions.ShareKeyFor("cover-1", null, TextureFillMode.Fill);
        string two = TextureCreationOptions.ShareKeyFor("cover-2", null, TextureFillMode.Fill);

        Assert.That(one, Is.Not.EqualTo(two));
    }

    [Test]
    public void AFullResolutionDecodeDoesNotShareWithASizedOne()
    {
        string full = TextureCreationOptions.ShareKeyFor("cover-1", null, TextureFillMode.Fill);
        string sized = TextureCreationOptions.ShareKeyFor("cover-1", new Vector2(320, 180), TextureFillMode.Fill);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(full, Is.Not.EqualTo(sized));
            Assert.That(full, Does.Contain("full"));
        }
    }

    [Test]
    public void DefaultOptionsDecodeAtFullSizeAndDoNotShare()
    {
        var options = TextureCreationOptions.Default;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Decode.TargetSize, Is.Null);
            Assert.That(options.ShareKey, Is.Null);
            Assert.That(options.Name, Is.Null);
        }
    }
}
