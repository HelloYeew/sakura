// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Text;

/// <summary>
/// Tests the shaping counters, which exist so that the two changes built on top of them can be
/// falsified at all.
/// </summary>
[TestFixture]
public class TextShapingCounterTest
{
    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;

    private static long textShapes => GlobalStatistics.Get<long>("Fonts", "Text Shapes").Value;
    private static long shapedRuns => GlobalStatistics.Get<long>("Fonts", "Shaped Runs").Value;

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        store = new RendererFontStore(new HeadlessRenderer(textureManager));

        var fonts = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources")
            .GetStorageForDirectory("Fonts");

        store.AddFont(fonts, "Comfortaa-Regular.ttf", alias: "Counted");
    }

    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    private Font font() => store.Get("Counted");

    [Test]
    public void ShapingTextMovesTheCounter()
    {
        long before = textShapes;

        font().ProcessText("hello", 16f);

        Assert.That(textShapes - before, Is.EqualTo(1));
    }

    [Test]
    public void EachLayoutCounts()
    {
        long before = textShapes;

        for (int i = 0; i < 5; i++)
            font().ProcessText("hello", 16f);

        Assert.That(textShapes - before, Is.EqualTo(5), "nothing caches shaping yet, so five layouts are five shapes");
    }

    /// <summary>
    /// Empty text takes the <see cref="ShapedText.Empty"/> short circuit before any shaping work, so it
    /// must not register otherwise an idle screen full of empty labels would read as constant churn.
    /// </summary>
    [Test]
    public void EmptyTextDoesNotCount()
    {
        long before = textShapes;

        font().ProcessText("", 16f);
        font().ProcessText(null!, 16f);

        Assert.That(textShapes, Is.EqualTo(before));
    }

    /// <summary>
    /// Runs are counted separately from layouts because mixed-script text shapes once per stretch a
    /// different font covers, so run count is what actually tracks HarfBuzz work.
    /// </summary>
    [Test]
    public void RunsAreCountedSeparatelyFromLayouts()
    {
        long beforeShapes = textShapes;
        long beforeRuns = shapedRuns;

        font().ProcessText("hello", 16f);

        Assert.Multiple(() =>
        {
            Assert.That(textShapes - beforeShapes, Is.EqualTo(1));
            Assert.That(shapedRuns - beforeRuns, Is.GreaterThanOrEqualTo(1), "single-script text is at least one run");
        });
    }
}
