// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Text;

/// <summary>
/// Text drawn by more than one font is shaped as one run per font, and each glyph still says where in the whole text its
/// cluster starts: a caret maps a string index to a glyph through it.
/// </summary>
[TestFixture]
public class TextRunClusterTest
{
    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;

    /// <summary>
    /// Opens the logger, which loading the default fonts writes to.
    /// </summary>
    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    /// <summary>
    /// Closes the logger.
    /// </summary>
    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    /// <summary>
    /// Makes a store with the framework's default fonts and their fallbacks.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        store = new RendererFontStore(new HeadlessRenderer(textureManager));

        store.LoadDefaultFont(new EmbeddedResourceStorage(typeof(RendererFontStore).Assembly, "Sakura.Framework.Resources")
            .GetStorageForDirectory("Fonts"));
    }

    /// <summary>
    /// Releases the store and its textures.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    /// <summary>
    /// Every glyph starts at its own index in the text, in runs after the first as in the first.
    /// </summary>
    /// <param name="text">Latin around a character NotoSans lacks, so the text is three runs.</param>
    /// <param name="expected">Where each glyph's cluster starts.</param>
    [TestCase("ab好cd", new[] { 0, 1, 2, 3, 4 })]
    [TestCase("a😀b", new[] { 0, 1, 3 })]
    public void EachGlyphStartsAtItsIndexInTheWholeText(string text, int[] expected)
    {
        var shaped = store.Shape(FontUsage.Default.With(size: 16f), text, 1f);

        Assert.That(shaped.Glyphs.Select(glyph => glyph.StartIndex), Is.EqualTo(expected));
    }
}
