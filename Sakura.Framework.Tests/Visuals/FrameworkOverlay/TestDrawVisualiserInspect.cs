// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public partial class TestDrawVisualiserInspect : ManualInputManagerTestScene
{
    private DebugWindowLayer layer = null!;
    private Box target = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Set up a box to pick, and the layer", () =>
        {
            TestContent.Clear();

            TestContent.Add(target = new Box
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(120, 420),
                Size = new Vector2(160, 120),
                Color = Color.Red
            });

            TestContent.Add(layer = new DebugWindowLayer { Depth = float.MaxValue });
        });

        AddStep("Open the visualiser", () => layer.Open(() => new DrawVisualiser(TestContent)));
    }

    [Test]
    public void TestPicksTheDrawableUnderTheCursor()
    {
        AddStep("Start inspecting", () => window().ToggleInspectMode());

        AddStep("Point at the box and click", () =>
        {
            InputManager.MoveMouseTo(target);
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("Picking ended", () => !window().IsInspecting);
        AddAssert("The box is selected", () => window().SelectedDrawable == target);

        // Rooted at the parent, so the box shows among its siblings.
        AddAssert("Tree rooted at the box's parent", () => window().TreeRoot == target.Parent);
        AddUntilStep("Tree shows the parent and its children", () => visibleRows() > 1);

        AddUntilStep("Window came back", () => window().Alpha >= 1);
    }

    /// <summary>
    /// The window hides itself while picking
    /// </summary>
    [Test]
    public void TestPicksThroughWhereTheWindowWas()
    {
        AddStep("Put the window over the box", () => window().SetBounds(new Vector2(60, 360), new Vector2(560, 300)));

        AddStep("Click the box with the window in the way", () =>
        {
            InputManager.MoveMouseTo(target);
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("The window took that click", () => window().SelectedDrawable == null);

        AddStep("Start inspecting", () => window().ToggleInspectMode());

        AddStep("Click the same place again", () =>
        {
            InputManager.MoveMouseTo(target);
            InputManager.Click(MouseButton.Left);
        });

        AddUntilStep("The box was picked through it", () => window().SelectedDrawable == target);
    }

    /// <summary>
    /// A full-screen container with nothing in it, in front of everything — an input catcher, as
    /// Yuuki's Alt+scroll volume catcher is. It draws nothing, so the pick goes through it.
    /// </summary>
    [Test]
    public void TestPicksThroughAnEmptyContainerInFront()
    {
        AddStep("Add an empty full-screen container in front", () => TestContent.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Name = "Catcher"
        }));

        AddStep("Start inspecting", () => window().ToggleInspectMode());
        AddStep("Point at the box", () => InputManager.MoveMouseTo(target));
        AddAssert("The box is hovered", () => inspectLayer().Hovered == target);

        AddStep("Click", () => InputManager.Click(MouseButton.Left));
        AddAssert("The box is selected", () => window().SelectedDrawable == target);
    }

    [Test]
    public void TestPicksThroughATransparentDrawable()
    {
        AddStep("Cover the box with a fully transparent one", () => TestContent.Add(new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = target.Position,
            Size = target.Size,
            Color = Color.FromArgb(0, 255, 255, 255)
        }));

        AddStep("Start inspecting", () => window().ToggleInspectMode());
        AddStep("Point at the box", () => InputManager.MoveMouseTo(target));
        AddAssert("The visible box is hovered", () => inspectLayer().Hovered == target);
        AddAssert("It is the only thing there", () => inspectLayer().Candidates.Count == 1);
    }

    [Test]
    public void TestPicksAChildOutsideItsParent()
    {
        Box overflowing = null!;

        AddStep("Add a box sticking out of a zero-size parent", () => TestContent.Add(new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(400, 100),
            Child = overflowing = new Box
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Size = new Vector2(100),
                Color = Color.Blue
            }
        }));

        AddStep("Start inspecting", () => window().ToggleInspectMode());
        AddStep("Point at the box", () => InputManager.MoveMouseTo(overflowing));
        AddAssert("The box is hovered", () => inspectLayer().Hovered == overflowing);
    }

    [Test]
    public void TestPicksABorderedContainer()
    {
        Container bordered = null!;

        AddStep("Add an empty container with a border", () => TestContent.Add(bordered = new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(400, 100),
            Size = new Vector2(100),
            Masking = true,
            BorderThickness = 4,
            BorderColor = Color.White
        }));

        AddStep("Start inspecting", () => window().ToggleInspectMode());
        AddStep("Point at it", () => InputManager.MoveMouseTo(bordered));
        AddAssert("The container is hovered", () => inspectLayer().Hovered == bordered);
    }

    [Test]
    public void TestFallsBackToAContainerWhereNothingDraws()
    {
        AddStep("Start inspecting", () => window().ToggleInspectMode());
        AddStep("Point at an empty spot", () => InputManager.MoveMouseTo(new Vector2(700, 100)));
        AddAssert("The root is hovered", () => inspectLayer().Hovered == TestContent);
    }

    [Test]
    public void TestScrollCyclesThroughWhatIsUnderTheCursor()
    {
        Box front = null!;

        AddStep("Add a smaller box in front of the box", () => TestContent.Add(front = new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = target.Position + new Vector2(40, 30),
            Size = new Vector2(80, 60),
            Color = Color.Green
        }));

        AddStep("Start inspecting", () => window().ToggleInspectMode());
        AddStep("Point at both", () => InputManager.MoveMouseTo(front));
        AddAssert("The front one is hovered", () => inspectLayer().Hovered == front);
        AddAssert("The label names it and counts both", () =>
            inspectLayer().HoverLabelText.StartsWith(front.GetDisplayName()) && inspectLayer().HoverLabelText.Contains("1/2"));

        AddStep("Scroll down", () => InputManager.ScrollBy(new Vector2(0, -1)));
        AddAssert("The one behind is hovered", () => inspectLayer().Hovered == target);
        AddWaitStep("Let frames pass with the cursor still", 5);
        AddAssert("Still the one behind", () => inspectLayer().Hovered == target);

        AddStep("Scroll down past the back", () => InputManager.ScrollBy(new Vector2(0, -1)));
        AddAssert("Wrapped to the front", () => inspectLayer().Hovered == front);

        AddStep("Scroll up", () => InputManager.ScrollBy(new Vector2(0, 1)));
        AddAssert("Wrapped to the back", () => inspectLayer().Hovered == target);

        AddStep("Click", () => InputManager.Click(MouseButton.Left));
        AddAssert("The one scrolled to is selected", () => window().SelectedDrawable == target);
    }

    [Test]
    public void TestMovingTheCursorStartsFromTheFrontAgain()
    {
        Box front = null!;

        AddStep("Add a smaller box in front of the box", () => TestContent.Add(front = new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = target.Position + new Vector2(40, 30),
            Size = new Vector2(80, 60),
            Color = Color.Green
        }));

        AddStep("Start inspecting", () => window().ToggleInspectMode());
        AddStep("Point at both", () => InputManager.MoveMouseTo(front));
        AddStep("Scroll down", () => InputManager.ScrollBy(new Vector2(0, -1)));
        AddAssert("The one behind is hovered", () => inspectLayer().Hovered == target);

        AddStep("Nudge the cursor", () => InputManager.MoveMouseTo(front.DrawRectangle.Location + new Vector2(10, 10)));
        AddAssert("The front one is hovered again", () => inspectLayer().Hovered == front);
    }

    private DrawVisualiser window() => layer.OpenWindows.OfType<DrawVisualiser>().Single();

    private DrawVisualiserInspectLayer inspectLayer() => collect<DrawVisualiserInspectLayer>(layer).Single();

    /// <summary>
    /// Rows the tree pane is actually showing. The pool is not shrunk when the tree gets smaller —
    /// surplus rows are hidden and reused so the bound ones are the visible ones.
    /// </summary>
    private int visibleRows() => collect<VisualiserTreeItem>(window()).Count(r => r.Alpha > 0);

    private static List<T> collect<T>(Drawable drawable) where T : Drawable
    {
        var found = new List<T>();

        walk(drawable);
        return found;

        void walk(Drawable d)
        {
            if (d is T match)
                found.Add(match);

            if (d is Container container)
            {
                foreach (var child in container.Children)
                    walk(child);
            }
        }
    }
}
