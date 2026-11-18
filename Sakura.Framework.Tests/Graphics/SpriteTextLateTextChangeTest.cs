// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// A <see cref="SpriteText"/> whose text changes after it built its vertices for the frame, and is then re-shaped by a
/// size read before the draw node is made.
/// </summary>
/// <remarks>
/// That happens in apps: a thumbnail that finishes loading raises an event during its own update, and a row further
/// up the tree, whose label has already updated, sets a longer text; a flow then reads the label's size, which shapes
/// it at once. The draw node used to batch the newly shaped glyphs over the old vertices, and the draw thread threw
/// <see cref="System.ArgumentOutOfRangeException"/> from <c>SpriteTextDrawNode.Draw</c>.
/// </remarks>
[TestFixture]
public class SpriteTextLateTextChangeTest
{
    private HeadlessTextureManager textureManager = null!;
    private HeadlessRenderer renderer = null!;
    private RendererFontStore store = null!;
    private ManualClock manual = null!;
    private FontRoot root = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        renderer = new HeadlessRenderer(textureManager);
        store = new RendererFontStore(renderer);

        var fonts = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources")
            .GetStorageForDirectory("Fonts");

        store.AddFont(fonts, "Comfortaa-Regular.ttf", alias: "Sprite");

        manual = new ManualClock { CurrentTime = 1000 };
        root = new FontRoot { Size = new Vector2(800, 600), Clock = new FramedClock(manual) };
        root.Load();
        root.CompleteLoad();
        root.Provide(store);
    }

    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    private static FontUsage usage => FontUsage.Default.With(family: "Sprite", size: 40f);

    private SpriteText add(string content, bool shadow = false)
    {
        var sprite = new SpriteText
        {
            Text = content,
            Font = usage,
            Shadow = shadow
        };

        root.Add(sprite);
        frame();
        return sprite;
    }

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    private DrawNode node() => ((ContainerDrawNode)root.GenerateDrawNodeSubtree(0)).Children[^1];

    /// <summary>
    /// Longer text, set and shaped after the vertices were built, draws this frame's vertices instead of throwing.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void LongerTextShapedLateDrawsTheOldVertices(bool shadow)
    {
        SpriteText sprite = add("HAM", shadow);

        sprite.Text = "HAMBURG";
        _ = sprite.Size;

        DrawNode late = node();

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => late.Draw(renderer));
            Assert.That(late.VertexCount, Is.EqualTo((shadow ? 2 : 1) * 3 * 4), "still the three letters it had vertices for");
        });
    }

    /// <summary>
    /// Shorter text the same way: the old batches would have drawn stale glyphs, the snapshot draws what it built.
    /// </summary>
    [Test]
    public void ShorterTextShapedLateDrawsTheOldVertices()
    {
        SpriteText sprite = add("HAMBURG");

        sprite.Text = "HA";
        _ = sprite.Size;

        DrawNode late = node();

        Assert.DoesNotThrow(() => late.Draw(renderer));
    }

    /// <summary>
    /// The change was not lost: the next frame builds vertices for the new text.
    /// </summary>
    [Test]
    public void TheNextFrameShowsTheNewText()
    {
        SpriteText sprite = add("HAM");

        sprite.Text = "HAMBURG";
        _ = sprite.Size;
        frame();

        DrawNode next = node();

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => next.Draw(renderer));
            Assert.That(next.VertexCount, Is.EqualTo(7 * 4));
        });
    }

    private partial class FontRoot : Container
    {
        public void Provide(IFontStore fontStore) => Cache(fontStore);
    }
}
