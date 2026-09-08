// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
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
        AddAssert("Rows are bounded by the pane, not the app", () => countRows() < 100);
    }

    /// <summary>
    /// The highlight boxes and the picker are a sibling of the window on the layer, not a child of
    /// it — a highlight inside the window's body would be clipped to the window, and picking has to
    /// reach anything on screen. So they are attached and detached with the window.
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

        AddUntilStep("Nothing left in the layer once the close animation ends", () => layer.Children.Count == 0);
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

    /// <summary>
    /// The tree pane's search box keeps the drawables that match and the ancestors that make them
    /// locatable, and nothing else. A match shown without its chain says nothing about where it is.
    /// </summary>
    [Test]
    public void TestTreeSearchKeepsMatchesAndTheirAncestors()
    {
        Drawable needle = null!;

        AddStep("Add a haystack with one needle in it", () =>
        {
            for (int i = 0; i < 20; i++)
            {
                Add(new Box
                {
                    Name = $"Haystack {i}",
                    Color = Color.Gray,
                    Size = new Vector2(10),
                    Position = new Vector2(i * 12, 0),
                    Depth = float.MaxValue - 30
                });
            }

            Add(new Container
            {
                Name = "Outer",
                Size = new Vector2(20),
                Position = new Vector2(0, 40),
                Depth = float.MaxValue - 31,
                Child = needle = new Box
                {
                    Name = "Needle",
                    Color = Color.Red,
                    Size = new Vector2(20)
                }
            });
        });

        open();

        AddStep("Root the tree at the scene", () => window().SetTreeRoot(this));
        AddUntilStep("The haystack is showing", () => visibleRows() > 20);

        AddStep("Search for the needle", () => window().TreeSearchText.Value = "needle");

        AddUntilStep("Only the needle and its ancestors", () =>
        {
            var shown = trackedRows();

            return shown.Count > 0
                   && shown.Contains(needle)
                   && shown.All(d => d == needle || isAncestorOf(d, needle));
        });

        AddStep("Clear the search", () => window().TreeSearchText.Value = string.Empty);

        AddUntilStep("The haystack is back", () => visibleRows() > 20);
    }

    /// <summary>
    /// A search that matches nothing empties the tree and says so, rather than reading as the
    /// "nothing chosen" empty state the window opens in.
    /// </summary>
    [Test]
    public void TestTreeSearchWithNoMatchesEmptiesTheTree()
    {
        open();

        AddStep("Root the tree at the scene", () => window().SetTreeRoot(this));
        AddUntilStep("Rows were built", () => visibleRows() > 0);

        AddStep("Search for something absent", () => window().TreeSearchText.Value = "no drawable is called this");

        AddUntilStep("Nothing is showing", () => visibleRows() == 0);

        AddStep("Clear the search", () => window().TreeSearchText.Value = string.Empty);

        AddUntilStep("Rows are back", () => visibleRows() > 0);
    }

    /// <summary>
    /// The property pane's search box narrows the reflected members by name. The type heading is not
    /// one of them — a filtered pane still has to say what it is a pane of.
    /// </summary>
    [Test]
    public void TestPropertySearchFiltersMembersByName()
    {
        Box box = null!;

        AddStep("Add a box", () => Add(box = new Box
        {
            Name = "Target",
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Color = Color.Red,
            Size = new Vector2(100),
            Depth = float.MaxValue - 30
        }));

        open();

        AddStep("Choose it, as picking would", () =>
        {
            window().SetTreeRoot(box);
            window().SelectDrawable(box);
        });

        AddUntilStep("Members were reflected", () => propertyRows().Count > 10);

        AddStep("Search for alpha", () => window().PropertySearchText.Value = "alpha");

        AddUntilStep("Only the heading and alpha-named members", () =>
        {
            var rows = propertyRows();

            return rows.Count > 1
                   && rows[0] == "Type: Box"
                   && rows.Skip(1).All(r => r.Contains("alpha", StringComparison.OrdinalIgnoreCase));
        });

        AddStep("Clear the search", () => window().PropertySearchText.Value = string.Empty);

        AddUntilStep("Every member is back", () => propertyRows().Count > 10);
    }

    /// <summary>
    /// Choosing a different drawable clears the property search. A filter narrowed against the last
    /// drawable's members can match none of the new one's, so keeping it would answer a click with an
    /// empty pane.
    /// </summary>
    [Test]
    public void TestChoosingAnotherDrawableClearsThePropertySearch()
    {
        Box first = null!;
        Container second = null!;

        AddStep("Add two drawables", () =>
        {
            Add(first = new Box
            {
                Name = "First",
                Color = Color.Red,
                Size = new Vector2(50),
                Depth = float.MaxValue - 30
            });

            Add(second = new Container
            {
                Name = "Second",
                Size = new Vector2(50),
                Position = new Vector2(60, 0),
                Depth = float.MaxValue - 31
            });
        });

        open();

        AddStep("Choose the first", () => window().SelectDrawable(first));
        AddStep("Search for alpha", () => window().PropertySearchText.Value = "alpha");

        AddUntilStep("The pane is filtered", () => propertyRows().Count > 1 && propertyRows().Count < 10);

        AddStep("Re-click the same one", () => window().SelectDrawable(first));
        AddAssert("The filter survives re-clicking the selection", () => window().PropertySearchText.Value == "alpha");

        AddStep("Choose the second", () => window().SelectDrawable(second));

        AddAssert("The search box was cleared", () => window().PropertySearchText.Value.Length == 0);
        AddUntilStep("Every member is showing", () => propertyRows().Count > 10);
    }

    /// <summary>
    /// The gutter tag names the one thing most worth acting on, and says nothing at all about a
    /// drawable that is simply drawing — the tree is mostly healthy, so a mark on every row would be
    /// a mark on none of them.
    /// </summary>
    [Test]
    public void TestStateTagIsSilentWhenDrawing()
    {
        Box healthy = null!;
        Box zeroSized = null!;
        Box hidden = null!;

        AddStep("Add drawables in three states", () =>
        {
            Add(healthy = new Box { Name = "Healthy", Color = Color.Red, Size = new Vector2(50), Depth = float.MaxValue - 30 });
            Add(zeroSized = new Box { Name = "ZeroSized", Color = Color.Red, Size = Vector2.Zero, Depth = float.MaxValue - 31 });
            Add(hidden = new Box { Name = "Hidden", Color = Color.Red, Size = new Vector2(50), Alpha = 0, Depth = float.MaxValue - 32 });
        });

        open();

        AddStep("Root the tree at the scene", () => window().SetTreeRoot(this));
        AddUntilStep("Rows were built", () => rowFor(healthy) != null);

        AddAssert("A drawing row is untagged", () => rowFor(healthy)!.State == DrawableState.Drawing);
        AddAssert("A zero-sized row says so", () => rowFor(zeroSized)!.State == DrawableState.ZeroSize);
        AddAssert("A hidden row says so", () => rowFor(hidden)!.State == DrawableState.Hidden);

        AddStep("Give the zero-sized one a size", () => zeroSized.Size = new Vector2(20));
        AddUntilStep("It goes quiet again", () => rowFor(zeroSized)!.State == DrawableState.Drawing);
    }

    /// <summary>
    /// The state a row reports is what the drawable is doing to itself. Invisibility inherited from
    /// an ancestor is not a fact about the child, and is left to the row dimming to say — so a dim
    /// row with no tag means "something above me did this".
    /// </summary>
    [Test]
    public void TestInheritedInvisibilityIsNotTaggedOnTheChild()
    {
        Container parent = null!;
        Box child = null!;

        AddStep("Add a visible child of a hidden parent", () => Add(parent = new Container
        {
            Name = "Parent",
            Size = new Vector2(80),
            Depth = float.MaxValue - 30,
            Child = child = new Box
            {
                Name = "Child",
                Color = Color.Red,
                Size = new Vector2(40)
            }
        }));

        open();

        AddStep("Root the tree at the scene", () => window().SetTreeRoot(this));
        AddUntilStep("Rows were built", () => rowFor(child) != null);

        AddAssert("Both are drawing to start with", () => rowFor(parent)!.State == DrawableState.Drawing && rowFor(child)!.State == DrawableState.Drawing);

        AddStep("Hide the parent", () => parent.Alpha = 0);

        AddUntilStep("The parent is tagged hidden", () => rowFor(parent)!.State == DrawableState.Hidden);

        AddAssert("The child is not tagged, since it did nothing", () => rowFor(child)!.State == DrawableState.Drawing);
        AddAssert("The child's own alpha is untouched", () => child.Alpha > 0 && child.DrawAlpha <= 0);
    }

    /// <summary>
    /// Selection and hover stopped sharing a channel. Hovering a selected row used to overwrite the
    /// selection color outright, and the row stopped looking selected.
    /// </summary>
    [Test]
    public void TestSelectionSurvivesHover()
    {
        var selectedIdle = VisualiserTreeItem.RowFill(selected: true, rowHovered: false, cursorOn: false);
        var selectedHovered = VisualiserTreeItem.RowFill(selected: true, rowHovered: true, cursorOn: false);
        var hoveredOnly = VisualiserTreeItem.RowFill(selected: false, rowHovered: true, cursorOn: false);
        var cursorOnly = VisualiserTreeItem.RowFill(selected: false, rowHovered: true, cursorOn: true);
        var idle = VisualiserTreeItem.RowFill(selected: false, rowHovered: false, cursorOn: false);

        AddAssert("Hovering a selected row brightens it rather than recolouring it", () =>
            selectedHovered.Color == selectedIdle.Color && selectedHovered.Alpha > selectedIdle.Alpha);

        AddAssert("A hovered row is not mistakable for a selected one", () => hoveredOnly.Color != selectedIdle.Color);

        AddAssert("A tree hover outranks the app cursor, which is not something you did on purpose", () =>
            cursorOnly.Color == hoveredOnly.Color);

        AddAssert("An idle row is not filled at all", () => idle.Alpha == 0);
    }

    /// <summary>
    /// Selecting a row raises the bar that is selection's own channel, so no other state can take it
    /// away.
    /// </summary>
    [Test]
    public void TestSelectingARowMarksIt()
    {
        Box box = null!;

        AddStep("Add a box", () => Add(box = new Box { Name = "Target", Color = Color.Red, Size = new Vector2(50), Depth = float.MaxValue - 30 }));

        open();

        AddStep("Root and select it", () =>
        {
            window().SetTreeRoot(box);
            window().SelectDrawable(box);
        });

        AddUntilStep("Its row reports selected", () => rowFor(box)?.IsSelected == true);

        AddStep("Select something else", () => window().SelectDrawable(this));

        AddUntilStep("It stops reporting selected", () => rowFor(box)?.IsSelected == false);
    }

    /// <summary>
    /// The label dims its "(Type#id)" half so a name reads first — but most drawables are never given
    /// a name, and for those the type is the whole identity rather than a qualifier on one. Dimming it
    /// there would dim the entire tree.
    /// </summary>
    [Test]
    public void TestTypeIsOnlyDimmedBehindAName()
    {
        Box named = null!;
        Box unnamed = null!;

        AddStep("Add one named and one not", () =>
        {
            Add(named = new Box { Name = "Named", Color = Color.Red, Size = new Vector2(30), Depth = float.MaxValue - 30 });
            Add(unnamed = new Box { Color = Color.Red, Size = new Vector2(30), Position = new Vector2(40, 0), Depth = float.MaxValue - 31 });
        });

        open();

        AddStep("Root the tree at the scene", () => window().SetTreeRoot(this));
        AddUntilStep("Rows were built", () => rowFor(named) != null && rowFor(unnamed) != null);

        AddAssert("The named row dims its type", () => qualifier(rowFor(named)!).Color != Color.White);
        AddAssert("The unnamed row does not", () => qualifier(rowFor(unnamed)!).Color == Color.White);
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

    /// <summary>
    /// The "(Type#id)" half of a row's label — the second of the two texts in its label flow.
    /// </summary>
    private static SpriteText qualifier(VisualiserTreeItem row) =>
        collect<FlowContainer>(row).Single().Children.OfType<SpriteText>().ElementAt(1);

    private VisualiserTreeItem? rowFor(Drawable drawable) =>
        collect<VisualiserTreeItem>(window()).FirstOrDefault(r => r.Alpha > 0 && ReferenceEquals(r.Tracked, drawable));


    /// <summary>
    /// The drawables the tree pane is actually showing, in the order it shows them.
    /// </summary>
    private List<Drawable> trackedRows() =>
        collect<VisualiserTreeItem>(window())
            .Where(r => r.Alpha > 0 && r.Tracked != null)
            .Select(r => r.Tracked!)
            .ToList();

    /// <summary>
    /// The lines the property pane is showing, in order. One <see cref="SpriteText"/> per row.
    /// </summary>
    private List<string> propertyRows() =>
        collect<FlowContainer>(window())
            .Single(f => f.Name == "Property Flow")
            .Children.OfType<Container>()
            .SelectMany(c => c.Children.OfType<SpriteText>())
            .Select(t => t.Text)
            .ToList();

    private static bool isAncestorOf(Drawable candidate, Drawable of)
    {
        for (var parent = of.Parent; parent != null; parent = parent.Parent)
        {
            if (parent == candidate)
                return true;
        }

        return false;
    }

    private static List<T> collect<T>(Drawable drawable) where T : Drawable
    {
        var found = new List<T>();

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
