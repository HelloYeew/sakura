// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public partial class TestDrawVisualiser : TestScene
{
    private DebugWindowLayer layer = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add the window layer", () =>
        {
            Clear();
            Add(layer = new DebugWindowLayer { Depth = float.MaxValue - 20 });
        });
    }

    /// <summary>
    /// The window opens with an empty tree, not with the app's whole tree. Nobody reads a whole
    /// app's tree, so the tool asks you to choose a drawable instead — and one press of Up from the
    /// empty state reaches the root for the times you do want all of it.
    /// </summary>
    [Test]
    public void TestOpensEmptyAndWalksUpToTheRoot()
    {
        AddStep("Add a box", () => Add(new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Color = Color.Red,
            Size = new Vector2(100),
            Depth = float.MaxValue - 30
        }));

        open();

        AddAssert("Window is open", () => layer.IsOpen<DrawVisualiser>());
        AddAssert("Nothing is chosen", () => window().TreeRoot == null);
        AddUntilStep("No rows are showing", () => visibleRows() == 0);

        AddStep("Choose a drawable, as picking would", () => window().SetTreeRoot(this));

        AddUntilStep("Rows were built", () => visibleRows() > 0);

        AddStep("Empty it again", () => window().SetTreeRoot(null));

        AddUntilStep("Back to nothing", () => visibleRows() == 0);
    }

    /// <summary>
    /// The tree is virtualised: rows exist for the visible range, not one per drawable in the app
    /// (§9 item 3). A thousand boxes used to mean a thousand-odd <see cref="VisualiserTreeItem"/>s.
    /// </summary>
    [Test]
    public void TestTreeIsVirtualised()
    {
        AddStep("Add a lot of boxes", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                Add(new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Color = ColorExtensions.GetRandomColor(true),
                    Size = new Vector2(100),
                    Position = new Vector2(Random.Shared.Next(-400, 400), Random.Shared.Next(-400, 400)),
                    Depth = float.MaxValue - 30
                });
            }
        });

        open();

        AddStep("Root the tree at the scene", () => window().SetTreeRoot(this));
        AddUntilStep("Tree rows were built", () => visibleRows() > 0);

        // The window is 620px tall, so a 20px row cannot fit more than a few dozen of them however
        // many drawables the app has.
        AddAssert("Rows are bounded by the pane, not the app", () => countRows() < 100);
    }

    /// <summary>
    /// The highlight boxes and the picker are a sibling of the window on the layer, not a child of
    /// it — a highlight inside the window's body would be clipped to the window, and picking has to
    /// reach anything on screen (§4.3). So they are attached and detached with the window.
    /// </summary>
    [Test]
    public void TestInspectLayerFollowsTheWindow()
    {
        AddAssert("No overlay before opening", () => countInspectLayers() == 0);

        open();

        AddAssert("Overlay attached to the layer", () => countInspectLayers() == 1);
        AddAssert("Overlay is not inside the window", () => collect<DrawVisualiserInspectLayer>(window()).Count == 0);

        AddStep("Start inspecting", () => window().ToggleInspectMode());
        AddAssert("Inspecting", () => window().IsInspecting);

        AddStep("Close", () => layer.Toggle(() => new DrawVisualiser(this)));

        AddAssert("Nothing left in the layer", () => layer.Children.Count == 0);
        AddAssert("Overlay detached", () => countInspectLayers() == 0);
    }

    /// <summary>
    /// Closing while inspecting has to leave inspect mode too: the window hides itself to let the
    /// picker reach what is underneath it, so a close that skipped this would leave a hidden window
    /// to reopen and a picker claiming the whole screen.
    /// </summary>
    [Test]
    public void TestCloseWhileInspectingStopsInspecting()
    {
        open();

        AddStep("Start inspecting", () => window().ToggleInspectMode());
        AddAssert("Window hides itself while picking", () => window().Alpha < 1);

        DrawVisualiser closed = null;

        AddStep("Close", () =>
        {
            closed = window();
            layer.Toggle(() => new DrawVisualiser(this));
        });

        AddAssert("Not inspecting any more", () => !closed.IsInspecting);

        AddStep("Reopen", () => layer.Toggle(() => new DrawVisualiser(this)));

        AddUntilStep("Window is visible again", () => window().Alpha >= 1);
    }

    private void open() => AddStep("Open", () => layer.Toggle(() => new DrawVisualiser(this)));

    private DrawVisualiser window() => layer.OpenWindows.OfType<DrawVisualiser>().Single();

    private int countRows() => collect<VisualiserTreeItem>(window()).Count;

    /// <summary>
    /// Rows the pane is actually showing. The pool is not shrunk when the tree gets smaller —
    /// surplus rows are hidden and reused — so the bound ones are the visible ones.
    /// </summary>
    private int visibleRows() => collect<VisualiserTreeItem>(window()).Count(r => r.Alpha > 0);

    private int countInspectLayers() => layer.Children.OfType<DrawVisualiserInspectLayer>().Count();

    private static System.Collections.Generic.List<T> collect<T>(Drawable drawable) where T : Drawable
    {
        var found = new System.Collections.Generic.List<T>();

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

        walk(drawable);
        return found;
    }
}
