// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// <see cref="Drawable.DrawSize"/> is worked out when read: right straight after a change (no frame in
/// between), through relative chains, and while hidden. <see cref="Drawable.AppliedDrawSize"/> stays the
/// size the matrix was built for until the next update, and space conversions keep to it.
/// </summary>
[TestFixture]
public class DrawSizeFreshnessTest
{
    private ManualClock manual = null!;
    private Container root = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };
    }

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    private void load(Drawable drawable)
    {
        root.Add(drawable);
        root.Load();
        root.CompleteLoad();
        frame();
    }

    [Test]
    public void TestResizeShowsBeforeTheNextFrame()
    {
        var box = new Box { Size = new Vector2(50, 20) };
        load(box);

        box.Size = new Vector2(120, 40);

        Assert.Multiple(() =>
        {
            Assert.That(box.DrawSize, Is.EqualTo(new Vector2(120, 40)), "Fresh at once.");
            Assert.That(box.AppliedDrawSize, Is.EqualTo(new Vector2(50, 20)), "The matrix is still the old one's.");
        });

        frame();

        Assert.That(box.AppliedDrawSize, Is.EqualTo(new Vector2(120, 40)), "Applied by the next update.");
    }

    [Test]
    public void TestRelativeChildFollowsItsParentAtOnce()
    {
        Box child;
        var parent = new Container
        {
            Size = new Vector2(200, 100),
            Child = child = new Box { RelativeSizeAxes = Axes.Both, Size = new Vector2(0.5f) }
        };
        load(parent);

        Assert.That(child.DrawSize, Is.EqualTo(new Vector2(100, 50)));

        parent.Size = new Vector2(400, 300);

        Assert.That(child.DrawSize, Is.EqualTo(new Vector2(200, 150)), "Resolved against the parent as it is now.");
    }

    [Test]
    public void TestRelativeChainResolvesThroughEveryLevel([Values(1, 3, 6)] int depth)
    {
        // Each level fills its parent less 10px of padding a side.
        Drawable innermost = new Box { RelativeSizeAxes = Axes.Both };
        Drawable current = innermost;

        for (int i = 0; i < depth; i++)
        {
            current = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(10),
                Child = current
            };
        }

        var outer = new Container { Size = new Vector2(500, 400), Child = current };
        load(outer);

        outer.Size = new Vector2(300, 260);

        float expectedWidth = 300 - 20 * depth;
        float expectedHeight = 260 - 20 * depth;

        Assert.That(innermost.DrawSize, Is.EqualTo(new Vector2(expectedWidth, expectedHeight)));
    }

    [Test]
    public void TestMarginsComeOffRelativeAxes()
    {
        Box child;
        var parent = new Container
        {
            Size = new Vector2(200, 100),
            Child = child = new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 30,
                Margin = new MarginPadding { Left = 10, Right = 5, Top = 7 }
            }
        };
        load(parent);

        Assert.That(child.DrawSize, Is.EqualTo(new Vector2(185, 30)), "Margins narrow a relative axis only.");
    }

    [Test]
    public void TestHiddenDrawableStaysCurrent()
    {
        Box child;
        var parent = new Container
        {
            Size = new Vector2(200, 100),
            Alpha = 0,
            Child = child = new Box { Size = new Vector2(10) }
        };
        load(parent);

        child.Size = new Vector2(64, 32);
        frame();

        Assert.That(child.DrawSize, Is.EqualTo(new Vector2(64, 32)), "A hidden drawable doesn't update, but its size still reads true.");
    }

    [Test]
    public void TestRelativeChildOfAnEmptyParentHasNoArea()
    {
        Box child;
        var parent = new Container
        {
            Size = Vector2.Zero,
            Child = child = new Box { RelativeSizeAxes = Axes.Both }
        };
        load(parent);

        Assert.Multiple(() =>
        {
            Assert.That(child.DrawSize, Is.EqualTo(Vector2.Zero));
            Assert.That(child.AppliedDrawSize, Is.EqualTo(Vector2.Zero), "Not a pixel leaked from the divide-by-zero guard.");
        });
    }

    [Test]
    public void TestChildSizeIsFresh()
    {
        var container = new Container
        {
            Size = new Vector2(200, 100),
            Padding = new MarginPadding { Left = 10, Right = 20, Top = 5, Bottom = 15 }
        };
        load(container);

        container.Size = new Vector2(300, 200);

        Assert.That(container.ChildSize, Is.EqualTo(new Vector2(270, 180)));
    }

    [Test]
    public void TestSpaceConversionsKeepToTheLaidOutSize()
    {
        var box = new Box { Position = new Vector2(100, 50), Size = new Vector2(100) };
        load(box);

        box.Size = new Vector2(200);

        // The matrix still draws the old size until the next update; mapping through it with the new
        // size would land halfway.
        Assert.That(box.ToScreenSpace(new Vector2(100, 100)), Is.EqualTo(new Vector2(200, 150)));
        Assert.That(box.ToLocalSpace(new Vector2(200, 150)), Is.EqualTo(new Vector2(100, 100)));

        frame();

        Assert.That(box.ToScreenSpace(new Vector2(100, 100)), Is.EqualTo(new Vector2(200, 150)), "Same mapping once laid out at the new size.");
    }

    [TestFixture]
    public class WithText
    {
        private HeadlessTextureManager textureManager = null!;
        private RendererFontStore store = null!;
        private ManualClock manual = null!;
        private FontRoot root = null!;

        private static FontUsage usage => FontUsage.Default.With(family: "Sprite", size: 16f);

        [OneTimeSetUp]
        public void InitializeLogger() => Logger.Initialize();

        [OneTimeTearDown]
        public void ShutdownLogger() => Logger.Shutdown();

        [SetUp]
        public void SetUp()
        {
            textureManager = new HeadlessTextureManager();
            store = new RendererFontStore(new HeadlessRenderer(textureManager));
            store.AddFont(new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources").GetStorageForDirectory("Fonts"),
                "Comfortaa-Regular.ttf", alias: "Sprite");

            manual = new ManualClock { CurrentTime = 1000 };
            root = new FontRoot(store)
            {
                Size = new Vector2(800, 600),
                Clock = new FramedClock(manual)
            };
        }

        [TearDown]
        public void TearDown()
        {
            store.Dispose();
            textureManager.Dispose();
        }

        private float widthOf(string text) => store.Shape(usage, text, 1f).BoundingBox.X;

        private void frame()
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }

        /// <summary>
        /// The case the game used to wait 75 ms for: new text, then the width of what holds it, read at once.
        /// </summary>
        [Test]
        public void TestLabelWidthIsCurrentTheMomentTextChanges()
        {
            SpriteText text;
            var label = new Container
            {
                AutoSizeAxes = Axes.X,
                Height = 40,
                Padding = new MarginPadding { Left = 12, Right = 12 },
                Child = text = new SpriteText { Font = usage, Text = "Main Menu" }
            };

            root.Add(label);
            root.Load();
            root.CompleteLoad();
            frame();

            text.Text = "A much longer screen name";

            Assert.That(widthOf("A much longer screen name"), Is.GreaterThan(widthOf("Main Menu")), "The font must have loaded for this to mean anything.");

            Assert.Multiple(() =>
            {
                Assert.That(text.DrawSize.X, Is.EqualTo(widthOf("A much longer screen name")).Within(0.01f));
                Assert.That(label.DrawSize.X, Is.EqualTo(widthOf("A much longer screen name") + 24).Within(0.01f));
            });
        }

        [Test]
        public void TestHiddenTextMeasuresWhenRead()
        {
            SpriteText text;
            var holder = new Container
            {
                Alpha = 0,
                AutoSizeAxes = Axes.Both,
                Child = text = new SpriteText { Font = usage, Text = "1" }
            };

            root.Add(holder);
            root.Load();
            root.CompleteLoad();
            frame();

            text.Text = "12";
            frame();

            Assert.That(holder.DrawSize.X, Is.EqualTo(widthOf("12")).Within(0.01f), "The badge case: counted while hidden, sized right when shown.");
        }

        private partial class FontRoot : Container
        {
            [Cached(typeof(IFontStore))]
            private readonly IFontStore fonts;

            public FontRoot(IFontStore fonts)
            {
                this.fonts = fonts;
            }
        }
    }
}
