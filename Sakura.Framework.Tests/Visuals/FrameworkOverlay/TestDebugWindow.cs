// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public partial class TestDebugWindow : ManualInputManagerTestScene
{
    private DebugWindowLayer layer = null!;
    private BasicButton appButton = null!;
    private int appClicks;

    [SetUp]
    public void SetUp()
    {
        appClicks = 0;

        AddStep("Set up layer and an app button behind it", () =>
        {
            TestContent.Clear();

            TestContent.Add(appButton = new BasicButton
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(60, 420),
                Size = new Vector2(220, 60),
                Text = "App button",
                Action = () => appClicks++
            });

            TestContent.Add(new SpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(60, 490),
                Text = "this button still works with windows open",
                Font = FontUsage.Default.With(size: 14),
                Color = Color.LightGray
            });

            TestContent.Add(layer = new DebugWindowLayer { Depth = float.MaxValue });
        });
    }

    [Test]
    public void TestOpenAndClose()
    {
        AddStep("Open", () => layer.Toggle(() => new PinkWindow()));
        AddAssert("Is open", () => layer.IsOpen<PinkWindow>());
        AddAssert("Has a parent", () => currentWindow<PinkWindow>().Parent == layer);

        AddStep("Toggle again", () => layer.Toggle(() => new PinkWindow()));
        AddAssert("Is closed", () => !layer.IsOpen<PinkWindow>());
        AddAssert("Detached, not hidden", () => layer.Children.Count == 0);
    }

    [Test]
    public void TestDragMoves()
    {
        AddStep("Open at a known place", () =>
        {
            layer.Open(() => new PinkWindow()).SetBounds(new Vector2(80, 60), new Vector2(520, 320));
        });

        Vector2 before = Vector2.Zero;

        AddStep("Drag the title bar", () =>
        {
            var window = currentWindow<PinkWindow>();
            before = window.Position;

            var rect = window.DrawRectangle;
            var grabPoint = new Vector2(rect.X + 80, rect.Y + DebugWindow.TITLE_HEIGHT / 2);

            InputManager.Drag(grabPoint, grabPoint + new Vector2(140, 90));
        });

        AddAssert("Moved by the drag delta", () =>
        {
            var moved = currentWindow<PinkWindow>().Position - before;
            return Precision.AlmostEquals(moved.X, 140, 1) && Precision.AlmostEquals(moved.Y, 90, 1);
        });
    }

    [Test]
    public void TestDragResizes()
    {
        AddStep("Open at a known size", () =>
        {
            layer.Open(() => new PinkWindow()).SetBounds(new Vector2(80, 60), new Vector2(420, 260));
        });

        Vector2 before = Vector2.Zero;

        AddStep("Drag the corner grip", () =>
        {
            var window = currentWindow<PinkWindow>();
            before = window.CurrentSize;

            var rect = window.DrawRectangle;
            var grip = new Vector2(rect.X + rect.Width - 8, rect.Y + rect.Height - 8);

            InputManager.Drag(grip, grip + new Vector2(120, 80));
        });

        AddAssert("Grew by the drag delta", () =>
        {
            var grown = currentWindow<PinkWindow>().CurrentSize - before;
            return Precision.AlmostEquals(grown.X, 120, 1) && Precision.AlmostEquals(grown.Y, 80, 1);
        });

        AddStep("Drag the grip far past the minimum", () =>
        {
            var window = currentWindow<PinkWindow>();
            var rect = window.DrawRectangle;
            var grip = new Vector2(rect.X + rect.Width - 8, rect.Y + rect.Height - 8);

            InputManager.Drag(grip, grip - new Vector2(2000, 2000));
        });

        AddAssert("Stopped at the minimum", () =>
        {
            var size = currentWindow<PinkWindow>().CurrentSize;
            return Precision.AlmostEquals(size.X, PinkWindow.MIN.X, 1) && Precision.AlmostEquals(size.Y, PinkWindow.MIN.Y, 1);
        });
    }

    [Test]
    public void TestCloseButton()
    {
        AddStep("Open", () => layer.Open(() => new PinkWindow()).SetBounds(new Vector2(80, 60), new Vector2(520, 320)));

        AddStep("Click the close button", () =>
        {
            var rect = currentWindow<PinkWindow>().DrawRectangle;

            // Top-right of the title bar, where the chrome puts it.
            InputManager.MoveMouseTo(new Vector2(rect.X + rect.Width - 13, rect.Y + DebugWindow.TITLE_HEIGHT / 2));
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("Closed", () => !layer.IsOpen<PinkWindow>());
    }

    /// <summary>
    /// The reason this rework exists. The old overlays returned true from every positional handler for
    /// the whole screen while visible, so nothing behind them could be clicked.
    /// </summary>
    [Test]
    public void TestAppBehindStaysClickable()
    {
        AddStep("Open two windows away from the button", () =>
        {
            layer.Open(() => new PinkWindow()).SetBounds(new Vector2(60, 40), new Vector2(400, 240));
            layer.Open(() => new CyanWindow()).SetBounds(new Vector2(500, 40), new Vector2(360, 220));
        });

        AddStep("Click the app button", () =>
        {
            InputManager.MoveMouseTo(appButton);
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("The app got the click", () => appClicks == 1);

        AddStep("Move a window over the button", () =>
        {
            currentWindow<PinkWindow>().SetBounds(appButton.Position - new Vector2(20), appButton.Size + new Vector2(40));
        });

        AddStep("Click the same place again", () =>
        {
            InputManager.MoveMouseTo(appButton);
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("The window absorbed it", () => appClicks == 1);
    }

    [Test]
    public void TestClickRaises()
    {
        AddStep("Open two side by side", () =>
        {
            layer.Open(() => new PinkWindow()).SetBounds(new Vector2(60, 40), new Vector2(400, 240));
            layer.Open(() => new CyanWindow()).SetBounds(new Vector2(500, 40), new Vector2(360, 220));
        });

        AddAssert("The second is in front", () => currentWindow<CyanWindow>().Depth > currentWindow<PinkWindow>().Depth);

        AddStep("Click the first one's title bar", () =>
        {
            var rect = currentWindow<PinkWindow>().DrawRectangle;

            InputManager.MoveMouseTo(new Vector2(rect.X + 80, rect.Y + DebugWindow.TITLE_HEIGHT / 2));
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("The first is now in front", () => currentWindow<PinkWindow>().Depth > currentWindow<CyanWindow>().Depth);
    }

    [Test]
    public void TestOverlappingWindows()
    {
        AddStep("Open both overlapping", () =>
        {
            layer.Open(() => new PinkWindow()).SetBounds(new Vector2(100, 60), new Vector2(420, 300));
            layer.Open(() => new CyanWindow()).SetBounds(new Vector2(240, 140), new Vector2(420, 300));
        });

        AddAssert("Both open at once", () => layer.IsOpen<PinkWindow>() && layer.IsOpen<CyanWindow>());

        AddStep("Close the front one", () => layer.Close<CyanWindow>());

        AddAssert("The other is untouched", () => layer.IsOpen<PinkWindow>() && !layer.IsOpen<CyanWindow>());
    }

    private T currentWindow<T>() where T : DebugWindow
    {
        foreach (var window in layer.OpenWindows)
        {
            if (window is T typed)
                return typed;
        }

        throw new AssertionException($"No open {typeof(T).Name}.");
    }

    private partial class PinkWindow : DebugWindow
    {
        public static readonly Vector2 MIN = new Vector2(260, 140);

        protected override string Title => "Pink Window (stub)";
        protected override Vector2 DefaultSize => new Vector2(520, 320);
        protected override Vector2 MinSize => MIN;
        protected override Color Accent => Color.Pink;

        public PinkWindow()
        {
            Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = Color.Pink,
                Alpha = 0.08f
            });

            Add(new SpriteText
            {
                Position = new Vector2(10),
                Text = "drag the title bar to move, drag the bottom-right corner to resize",
                Color = Color.White
            });
        }
    }

    private partial class CyanWindow : DebugWindow
    {
        protected override string Title => "Cyan Window (stub)";
        protected override Vector2 DefaultSize => new Vector2(360, 220);
        protected override Color Accent => Color.Cyan;

        public CyanWindow()
        {
            Add(new SpriteText
            {
                Position = new Vector2(10),
                Text = "a second window that open at the same time",
                Color = Color.White
            });
        }
    }
}
