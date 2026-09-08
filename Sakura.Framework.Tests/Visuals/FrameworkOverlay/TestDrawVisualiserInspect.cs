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

        // The pick re-roots the tree at what was clicked. A Box has no children, so a tree of exactly
        // one row is what proves the click landed on the box itself rather than on an ancestor.
        AddUntilStep("Tree re-rooted at the box", () => visibleRows() == 1);

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

        AddAssert("The window took that click", () => visibleRows() != 1);

        AddStep("Start inspecting", () => window().ToggleInspectMode());

        AddStep("Click the same place again", () =>
        {
            InputManager.MoveMouseTo(target);
            InputManager.Click(MouseButton.Left);
        });

        AddUntilStep("The box was picked through it", () => visibleRows() == 1);
    }

    private DrawVisualiser window() => layer.OpenWindows.OfType<DrawVisualiser>().Single();

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
