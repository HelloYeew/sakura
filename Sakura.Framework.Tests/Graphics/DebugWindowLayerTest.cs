// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Performance-related behavior test for <see cref="DebugWindowLayer"/> and its class
/// </summary>
[TestFixture]
public class DebugWindowLayerTest
{
    private const float root_width = 1280;
    private const float root_height = 720;

    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore fontStore = null!;
    private ManualClock clock = null!;
    private TestRoot root = null!;
    private DebugWindowLayer layer = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        fontStore = new RendererFontStore(new HeadlessRenderer(textureManager));

        var fonts = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources")
            .GetStorageForDirectory("Fonts");

        fontStore.AddFont(fonts, "Comfortaa-Regular.ttf");

        clock = new ManualClock();

        root = new TestRoot(fontStore, new HeadlessWindow())
        {
            Size = new Vector2(root_width, root_height),
            Clock = new FramedClock(clock)
        };

        root.Load();
        root.CompleteLoad();

        root.Add(layer = new DebugWindowLayer());

        runFrame();
    }

    [TearDown]
    public void TearDown()
    {
        fontStore.Dispose();
        textureManager.Dispose();
    }

    private void runFrame(double advanceMs = 16)
    {
        clock.CurrentTime += advanceMs;
        root.UpdateSubTree();
    }

    /// <summary>
    /// Advances past any window's close animation. Closing plays the window out and the layer detaches
    /// it when that finishes, so a close is only observable a few frames later.
    /// </summary>
    private void runClose()
    {
        runFrame();
        runFrame(1000);
    }

    [Test]
    public void NothingIsBuiltUntilFirstOpen()
    {
        int built = 0;

        Assert.That(layer.Children, Is.Empty);

        layer.Toggle(() => new StubWindow(++built));
        Assert.That(built, Is.EqualTo(1));

        layer.Toggle(() => new StubWindow(++built));
        layer.Toggle(() => new StubWindow(++built));

        Assert.That(built, Is.EqualTo(1), "the factory should only run for the first open");
    }

    [Test]
    public void ClosingDetachesFromTheTree()
    {
        var window = layer.Open(() => new StubWindow(1));
        runFrame();

        Assert.That(window.Parent, Is.EqualTo(layer));
        Assert.That(layer.IsOpen<StubWindow>(), Is.True);

        layer.Close<StubWindow>();
        runFrame();

        // Still a child while it plays out, but closed to everything that asks.
        Assert.Multiple(() =>
        {
            Assert.That(window.IsClosing, Is.True);
            Assert.That(layer.IsOpen<StubWindow>(), Is.False);
            Assert.That(layer.OpenWindows, Is.Empty);
        });

        runClose();

        Assert.Multiple(() =>
        {
            Assert.That(window.Parent, Is.Null);
            Assert.That(window.IsDisposed, Is.False);
            Assert.That(layer.Children, Is.Empty);
            Assert.That(layer.IsOpen<StubWindow>(), Is.False);
        });
    }

    [Test]
    public void ClosingIsNotHiding()
    {
        var window = layer.Open(() => new StubWindow(1));
        runFrame();

        layer.Close<StubWindow>();
        runClose();

        // A window that hid itself would still be a child with Alpha 0 once its animation ended.
        // Nothing here should be: the animation is a send-off, not the resting state.
        foreach (var child in layer.Children)
            Assert.That(child, Is.Not.InstanceOf<DebugWindow>());

        Assert.That(window.Alpha, Is.EqualTo(1).Within(0.001f));
    }

    [Test]
    public void OpeningPlaysTheWindowIn()
    {
        var window = layer.Open(() => new StubWindow(1));

        // Before a single frame has run: the window is attached but has not been drawn yet, so it
        // starts from invisible rather than popping in at full alpha for one frame.
        Assert.Multiple(() =>
        {
            Assert.That(window.Alpha, Is.EqualTo(0).Within(0.001f));
            Assert.That(window.Scale.X, Is.LessThan(1));
        });

        runFrame();
        Assert.That(window.Alpha, Is.GreaterThan(0), "The open animation should be under way.");

        runFrame(1000);
        Assert.Multiple(() =>
        {
            Assert.That(window.Alpha, Is.EqualTo(1).Within(0.001f));
            Assert.That(window.Scale.X, Is.EqualTo(1).Within(0.001f));
        });
    }

    /// <summary>
    /// Re-opening a window still playing out catches it rather than starting over. The
    /// instance never left the tree, so snapping it back to invisible first would be a flicker.
    /// </summary>
    [Test]
    public void ReopeningMidCloseCancelsTheClose()
    {
        var window = layer.Open(() => new StubWindow(1));
        runFrame(1000);

        layer.Close<StubWindow>();
        runFrame(70);

        float partway = window.Alpha;

        Assert.Multiple(() =>
        {
            Assert.That(window.IsClosing, Is.True);
            Assert.That(partway, Is.LessThan(1).And.GreaterThan(0), "Should be caught mid-fade.");
        });

        var reopened = layer.Open(() => new StubWindow(2));

        Assert.Multiple(() =>
        {
            Assert.That(reopened, Is.SameAs(window), "the instance should be reused, not rebuilt");
            Assert.That(window.IsClosing, Is.False);
            Assert.That(window.Alpha, Is.EqualTo(partway).Within(0.001f), "should carry on from where the close got to, not restart from zero");
        });

        runFrame(1000);

        Assert.Multiple(() =>
        {
            Assert.That(window.Parent, Is.EqualTo(layer), "the cancelled close must not detach it later");
            Assert.That(window.Alpha, Is.EqualTo(1).Within(0.001f));
            Assert.That(layer.IsOpen<StubWindow>(), Is.True);
        });
    }

    /// <summary>
    /// A window on its way out is not a target. Its animation is a send-off, and for the length of it
    /// the app underneath must already be clickable.
    /// </summary>
    [Test]
    public void AClosingWindowClaimsNoInput()
    {
        var window = layer.Open(() => new StubWindow(1));
        window.SetBounds(new Vector2(100, 100), new Vector2(400, 300));
        runFrame(1000);

        var inside = window.ToScreenSpace(new Vector2(0.5f, 0.5f));

        Assert.Multiple(() =>
        {
            Assert.That(window.ReceivePositionalInputAt(inside), Is.True);
            Assert.That(layer.NotifyMouseDown(inside), Is.True);
        });

        layer.Close<StubWindow>();
        runFrame();

        Assert.Multiple(() =>
        {
            Assert.That(window.ReceivePositionalInputAt(inside), Is.False);
            Assert.That(layer.NotifyMouseDown(inside), Is.False, "a closing window must not be raised back to the front either");
        });
    }

    [Test]
    public void ReopeningKeepsContentAndBounds()
    {
        var window = layer.Open(() => new StubWindow(1));
        window.SetBounds(new Vector2(120, 90), new Vector2(500, 400));
        runFrame();

        window.AddMarker();
        int markers = window.MarkerCount;
        Assert.That(markers, Is.EqualTo(1));

        layer.Close<StubWindow>();
        runClose();

        var reopened = layer.Open(() => new StubWindow(2));
        runFrame();

        Assert.Multiple(() =>
        {
            Assert.That(reopened, Is.SameAs(window), "the instance should be reused, not rebuilt");
            Assert.That(reopened.MarkerCount, Is.EqualTo(markers), "content should survive a close");
            Assert.That(reopened.Position, Is.EqualTo(new Vector2(120, 90)));
            Assert.That(reopened.CurrentSize, Is.EqualTo(new Vector2(500, 400)));
        });
    }

    [Test]
    public void DisposeOnCloseRebuildsInstead()
    {
        int built = 0;

        var first = layer.Open(() => new DisposingStubWindow(++built));
        runFrame();

        layer.Close<DisposingStubWindow>();
        runClose();

        var second = layer.Open(() => new DisposingStubWindow(++built));
        runFrame();

        Assert.Multiple(() =>
        {
            Assert.That(built, Is.EqualTo(2));
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(first.IsDisposed, Is.True);
        });
    }

    [Test]
    public void SeveralWindowsOpenAtOnce()
    {
        layer.Open(() => new StubWindow(1));
        layer.Open(() => new OtherStubWindow());
        runFrame();

        Assert.Multiple(() =>
        {
            Assert.That(layer.Children, Has.Count.EqualTo(2));
            Assert.That(layer.IsOpen<StubWindow>(), Is.True);
            Assert.That(layer.IsOpen<OtherStubWindow>(), Is.True);
        });

        // The old overlays force-hid one another; two tools open together is the point of the rework.
        layer.Close<StubWindow>();
        runFrame();

        Assert.Multiple(() =>
        {
            Assert.That(layer.IsOpen<StubWindow>(), Is.False);
            Assert.That(layer.IsOpen<OtherStubWindow>(), Is.True);
        });
    }

    [Test]
    public void OpeningRaisesAboveTheOthers()
    {
        var first = layer.Open(() => new StubWindow(1));
        var second = layer.Open(() => new OtherStubWindow());
        runFrame();

        Assert.That(second.Depth, Is.GreaterThan(first.Depth));

        layer.Open(() => new StubWindow(1));
        runFrame();

        Assert.That(first.Depth, Is.GreaterThan(second.Depth), "reopening an open window should raise it");
    }

    [Test]
    public void MouseDownRaisesTheWindowUnderIt()
    {
        var first = layer.Open(() => new StubWindow(1));
        first.SetBounds(new Vector2(0, 0), new Vector2(400, 300));

        var second = layer.Open(() => new OtherStubWindow());
        second.SetBounds(new Vector2(600, 0), new Vector2(400, 300));

        runFrame();

        Assert.That(second.Depth, Is.GreaterThan(first.Depth));

        Assert.That(layer.NotifyMouseDown(new Vector2(200, 150)), Is.True);
        Assert.That(first.Depth, Is.GreaterThan(second.Depth));
    }

    [Test]
    public void MouseDownOutsideEveryWindowRaisesNothing()
    {
        var window = layer.Open(() => new StubWindow(1));
        window.SetBounds(new Vector2(0, 0), new Vector2(400, 300));
        runFrame();

        float depth = window.Depth;

        // Where the app is, not where the window is: the click belongs to the app and must not be
        // intercepted in any way.
        Assert.That(layer.NotifyMouseDown(new Vector2(900, 600)), Is.False);
        Assert.That(window.Depth, Is.EqualTo(depth));
    }

    [Test]
    public void WindowsAreClampedInsideTheParent()
    {
        var window = layer.Open(() => new StubWindow(1));

        window.SetBounds(new Vector2(root_width + 500, root_height + 500), new Vector2(400, 300));
        runFrame();

        Assert.Multiple(() =>
        {
            Assert.That(window.Position.X, Is.EqualTo(root_width - 400));
            Assert.That(window.Position.Y, Is.EqualTo(root_height - 300));
        });

        // A window larger than the client area is shrunk to fit rather than left partly unreachable.
        window.SetBounds(Vector2.Zero, new Vector2(root_width * 2, root_height * 2));
        runFrame();

        Assert.Multiple(() =>
        {
            Assert.That(window.CurrentSize.X, Is.EqualTo(root_width));
            Assert.That(window.CurrentSize.Y, Is.EqualTo(root_height));
        });
    }

    [Test]
    public void MinimumSizeIsRespected()
    {
        var window = layer.Open(() => new StubWindow(1));

        window.SetBounds(Vector2.Zero, new Vector2(10, 10));
        runFrame();

        Assert.Multiple(() =>
        {
            Assert.That(window.CurrentSize.X, Is.EqualTo(StubWindow.MIN.X));
            Assert.That(window.CurrentSize.Y, Is.EqualTo(StubWindow.MIN.Y));
        });
    }

    [Test]
    public void ShrinkingTheParentPullsWindowsBackIn()
    {
        var window = layer.Open(() => new StubWindow(1));
        window.SetBounds(new Vector2(800, 400), new Vector2(400, 300));
        runFrame();

        Assert.That(window.Position, Is.EqualTo(new Vector2(800, 400)));

        // A window placed in pixels would otherwise be left outside a shrunken client area.
        root.Size = new Vector2(900, 500);
        runFrame();
        runFrame();

        Assert.Multiple(() =>
        {
            Assert.That(window.Position.X, Is.LessThanOrEqualTo(900 - window.CurrentSize.X));
            Assert.That(window.Position.Y, Is.LessThanOrEqualTo(500 - window.CurrentSize.Y));
        });
    }

    [Test]
    public void CloseAllClosesEveryOpenWindow()
    {
        layer.Open(() => new StubWindow(1));
        layer.Open(() => new OtherStubWindow());
        runFrame();

        layer.CloseAll();
        runClose();

        Assert.That(layer.Children, Is.Empty);
    }

    /// <summary>
    /// A root that can hand its subtree the dependencies a <see cref="SpriteText"/> needs, which is the
    /// only reason a window cannot be loaded under a bare container.
    /// </summary>
    private partial class TestRoot : Container
    {
        private readonly IFontStore fontStore;
        private readonly IWindow window;

        public TestRoot(IFontStore fontStore, IWindow window)
        {
            this.fontStore = fontStore;
            this.window = window;
        }

        public override void Load()
        {
            base.Load();

            Cache(fontStore);
            Cache(window);
        }
    }

    private partial class StubWindow : DebugWindow
    {
        public static readonly Vector2 MIN = new Vector2(200, 120);

        public readonly int Generation;

        private int markers;

        public int MarkerCount => markers;

        protected override string Title => "Stub Window";
        protected override Vector2 DefaultSize => new Vector2(640, 480);
        protected override Vector2 MinSize => MIN;
        protected override Color Accent => Color.Pink;

        public StubWindow(int generation)
        {
            Generation = generation;
        }

        public void AddMarker()
        {
            Add(new Box { Size = new Vector2(10) });
            markers++;
        }
    }

    private partial class OtherStubWindow : DebugWindow
    {
        protected override string Title => "Other Stub Window";
        protected override Vector2 DefaultSize => new Vector2(320, 240);
    }

    private partial class DisposingStubWindow : DebugWindow
    {
        public readonly int Generation;

        protected override string Title => "Disposing Stub Window";
        protected internal override bool DisposeOnClose => true;

        public DisposingStubWindow(int generation)
        {
            Generation = generation;
        }
    }
}
