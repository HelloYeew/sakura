// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// End-to-end pixels through the real <see cref="BufferedContainer"/> draw path, on the CPU reference
/// rasterizer and on Vulkan (see <see cref="PixelCapture"/>).
/// </summary>
[TestFixture(PixelCapture.HEADLESS)]
[TestFixture(PixelCapture.VULKAN)]
public class BufferedContainerCompositeTest
{
    private const int width = 80;
    private const int height = 40;

    // Opaque, and primary so that sRGB-to-linear conversion is exact (0 and 255 map to 0 and 1).
    private static readonly Color lower = Color.Red;
    private static readonly Color upper = Color.Lime;

    private static readonly Vector4 background = new Vector4(0, 0, 1, 1);

    // Only the lower box covers this column, both cover the other.
    private const int lower_only_x = 10;
    private const int overlap_x = 40;
    private const int sample_y = 20;

    private readonly string rendererKind;

    private ManualClock manual = null!;
    private CachingRoot root = null!;
    private HeadlessAppHost host = null!;
    private PixelCapture capture = null!;

    /// <summary>
    /// Exact up to float error on the reference rasterizer; widened by the GPU's 8-bit rounding otherwise.
    /// </summary>
    private float tolerance => 1e-4f + capture.Quantization;

    public BufferedContainerCompositeTest(string rendererKind)
    {
        this.rendererKind = rendererKind;
    }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Logger.Initialize();
        capture = PixelCapture.Create(rendererKind, width, height);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        capture?.Dispose();
        Logger.Shutdown();
    }

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new CachingRoot
        {
            Size = new Vector2(width, height),
            Clock = new FramedClock(manual)
        };

        root.Load();
        root.CompleteLoad();

        host = new HeadlessAppHost(nameof(BufferedContainerCompositeTest));
        root.CacheHost(host);
    }

    [TearDown]
    public void TearDown() => host.Dispose();

    private partial class CachingRoot : Container
    {
        public void CacheHost(AppHost value) => Cache(value);
    }

    /// <summary>
    /// Renders the tree and returns the screen surface, with the background pre-filled so compositing has
    /// something to blend against.
    /// </summary>
    private PixelSurface render()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();

        capture.Begin(background);
        capture.Renderer.SetRoot(root.GenerateDrawNodeSubtree(0));
        capture.Renderer.Draw(new FramedClock(manual));

        return capture.End();
    }

    private static void addOverlappingBoxes(Container target)
    {
        target.Add(new Box { Position = new Vector2(0, 0), Size = new Vector2(60, height), Color = lower });
        target.Add(new Box { Position = new Vector2(20, 0), Size = new Vector2(60, height), Color = upper });
    }

    /// <summary>
    /// The behavior the class has always documented: a faded buffered container fades one flattened image,
    /// so a child hidden behind an opaque sibling contributes nothing.
    /// </summary>
    [Test]
    public void AFadedBufferedContainerHidesOverlapEntirely()
    {
        var buffered = new BufferedContainer { Size = new Vector2(width, height), Alpha = 0.5f };
        addOverlappingBoxes(buffered);
        root.Add(buffered);

        var pixel = render()[overlap_x, sample_y];

        Assert.Multiple(() =>
        {
            Assert.That(pixel.X, Is.EqualTo(0f).Within(tolerance), "the lower box is behind an opaque sibling and must not show through");
            Assert.That(pixel.Y, Is.EqualTo(0.5f).Within(tolerance), "half the upper box");
            Assert.That(pixel.Z, Is.EqualTo(0.5f).Within(tolerance), "half the background");
        });
    }

    /// <summary>
    /// The contrast that gives the test above its meaning: a plain container fades each child, so the
    /// hidden box bleeds through the one in front of it.
    /// </summary>
    [Test]
    public void AFadedPlainContainerLetsTheOverlapBleedThrough()
    {
        var plain = new Container { Size = new Vector2(width, height), Alpha = 0.5f };
        addOverlappingBoxes(plain);
        root.Add(plain);

        var pixel = render()[overlap_x, sample_y];

        Assert.That(pixel.X, Is.EqualTo(0.25f).Within(tolerance), "this bleed-through is exactly what buffering exists to remove");
    }

    /// <summary>
    /// Where nothing overlaps there is nothing to flatten, so both containers must agree — which is what
    /// stops the test above from passing for the wrong reason, such as a composite that has simply lost a
    /// color channel.
    /// </summary>
    [Test]
    public void BufferedAndPlainAgreeWhereNothingOverlaps()
    {
        var buffered = new BufferedContainer { Size = new Vector2(width, height), Alpha = 0.5f };
        addOverlappingBoxes(buffered);
        root.Add(buffered);

        var bufferedPixel = render()[lower_only_x, sample_y];

        SetUp();

        var plain = new Container { Size = new Vector2(width, height), Alpha = 0.5f };
        addOverlappingBoxes(plain);
        root.Add(plain);

        var plainPixel = render()[lower_only_x, sample_y];

        Assert.Multiple(() =>
        {
            Assert.That(bufferedPixel.X, Is.EqualTo(plainPixel.X).Within(tolerance), "red");
            Assert.That(bufferedPixel.Y, Is.EqualTo(plainPixel.Y).Within(tolerance), "green");
            Assert.That(bufferedPixel.Z, Is.EqualTo(plainPixel.Z).Within(tolerance), "blue");
        });
    }

    /// <summary>
    /// An unfaded buffered container must be pixel-identical to a plain one. This is the equivalence 's
    /// passthrough would rely on, and the one the old Alpha composite broke: the buffer's partially
    /// covered regions arrived multiplied by their alpha a second time.
    /// </summary>
    [Test]
    public void AnUnfadedBufferedContainerMatchesAPlainContainer()
    {
        var buffered = new BufferedContainer { Size = new Vector2(width, height) };
        addOverlappingBoxes(buffered);
        root.Add(buffered);

        var bufferedSurface = render();
        var bufferedOverlap = bufferedSurface[overlap_x, sample_y];
        var bufferedLowerOnly = bufferedSurface[lower_only_x, sample_y];

        SetUp();

        var plain = new Container { Size = new Vector2(width, height) };
        addOverlappingBoxes(plain);
        root.Add(plain);

        var plainSurface = render();
        var plainOverlap = plainSurface[overlap_x, sample_y];
        var plainLowerOnly = plainSurface[lower_only_x, sample_y];

        Assert.Multiple(() =>
        {
            Assert.That(bufferedOverlap.X, Is.EqualTo(plainOverlap.X).Within(tolerance), "overlap: red");
            Assert.That(bufferedOverlap.Y, Is.EqualTo(plainOverlap.Y).Within(tolerance), "overlap: green");
            Assert.That(bufferedOverlap.Z, Is.EqualTo(plainOverlap.Z).Within(tolerance), "overlap: blue");

            Assert.That(bufferedLowerOnly.X, Is.EqualTo(plainLowerOnly.X).Within(tolerance), "lower box only: red");
            Assert.That(bufferedLowerOnly.Y, Is.EqualTo(plainLowerOnly.Y).Within(tolerance), "lower box only: green");
            Assert.That(bufferedLowerOnly.Z, Is.EqualTo(plainLowerOnly.Z).Within(tolerance), "lower box only: blue");
        });
    }

    /// <summary>
    /// The container's <see cref="Drawable.Color"/> tints the composite. Color does not cascade to
    /// children in this framework, so it can only be applied on the composite quad — which is why a
    /// passthrough that skipped the buffer would have to force buffering whenever the tint is not neutral.
    /// </summary>
    [Test]
    public void TheContainersColorTintsTheComposite()
    {
        var buffered = new BufferedContainer { Size = new Vector2(width, height), Color = Color.Red };
        buffered.Add(new Box { Position = new Vector2(0, 0), Size = new Vector2(width, height), Color = Color.White });
        root.Add(buffered);

        var pixel = render()[overlap_x, sample_y];

        Assert.Multiple(() =>
        {
            Assert.That(pixel.X, Is.EqualTo(1f).Within(tolerance), "red survives the tint");
            Assert.That(pixel.Y, Is.EqualTo(0f).Within(tolerance), "green is tinted out");
            Assert.That(pixel.Z, Is.EqualTo(0f).Within(tolerance), "blue is tinted out");
        });
    }
}
