// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Performance;

public partial class DrawVisualiser : DebugWindow
{
    /// <summary>
    /// Fraction of the window's width given to the tree pane.
    /// </summary>
    private const float width_split = 0.4f;

    private const float entry_height = 20;

    private const float toolbar_height = 34;

    /// <summary>
    /// Height of the search row
    /// </summary>
    private const float search_height = 30;

    /// <summary>
    /// How often the property pane re-reads the selected drawable.
    /// </summary>
    private const double property_interval = 100;

    /// <summary>
    /// How often the app's tree is walked to see whether it changed shape.
    /// </summary>
    private const double tree_interval = 500;

    protected override string Title => "Draw Visualiser (Ctrl + F1)";
    protected override Vector2 DefaultSize => new Vector2(940, 620);
    protected override Vector2 MinSize => new Vector2(520, 280);
    protected override Color Accent => Color.Pink;

    /// <summary>
    /// The app root: what inspect mode picks against, whatever the tree is rooted at.
    /// </summary>
    private readonly Drawable absoluteRoot;

    /// <summary>
    /// What the tree pane is rooted at, or null for the empty state it opens in. Moved by the
    /// toolbar buttons and by choosing a drawable.
    /// </summary>
    private Drawable? targetRoot;

    private readonly ScrollableContainer treeScroll;
    private readonly Container treeContent;

    private readonly BasicTextBox treeSearch;
    private readonly BasicTextBox propertySearch;

    private string treeFilter = string.Empty;
    private string propertyFilter = string.Empty;
    private string lastPropertyFilter = string.Empty;

    /// <summary>
    /// Shown instead of the tree while nothing has been chosen, and says how to choose.
    /// </summary>
    private readonly SpriteText emptyHint;
    private readonly ScrollableContainer propertyScroll;
    private readonly FlowContainer propertyFlow;

    /// <summary>
    /// The rows currently in the tree pane. One per visible line, reused as the pane scrolls — not
    /// one per drawable in the app.
    /// </summary>
    private readonly List<VisualiserTreeItem> rowPool = new List<VisualiserTreeItem>();

    private readonly List<(Drawable Drawable, int Depth)> cachedTreeStructure = new List<(Drawable, int)>();
    private readonly List<(Drawable Drawable, int Depth)> currentTreeStructure = new List<(Drawable, int)>();

    private Drawable? selectedDrawable;
    private Drawable? lastSelectedDrawable;

    private PropertyInfo[]? cachedProperties;
    private FieldInfo[]? cachedFields;
    private SpriteText? loadStateText;
    private readonly List<PropertyTracker> propertyTextMap = new List<PropertyTracker>();

    /// <summary>
    /// The highlight boxes and the inspected picker, in screen space, hosted by the layer beside this
    /// window rather than inside it. Built with the window and attached for as long as it is open.
    /// </summary>
    private readonly DrawVisualiserInspectLayer inspectLayer;

    private double nextPropertyRefresh = double.MinValue;
    private double nextTreeRefresh = double.MinValue;

    /// <summary>
    /// Height <see cref="treeContent"/> was last given, so a steady frame does not invalidate it.
    /// </summary>
    private float lastTreeContentHeight = -1;

    public DrawVisualiser(Drawable root)
    {
        absoluteRoot = root;

        inspectLayer = new DrawVisualiserInspectLayer(root)
        {
            Picked = picked =>
            {
                treeSearch?.Text.Value = string.Empty;

                SetTreeRoot(picked);
                SelectDrawable(picked);
                SetInspecting(false);
            }
        };

        Add(buildToolbar());

        var panes = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Padding = new MarginPadding
            {
                Top = toolbar_height
            }
        };

        var treePane = new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(width_split, 1),
            Name = "Tree Pane"
        };

        treePane.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Color = Color.Black,
            Alpha = 0.2f
        });

        treeSearch = createSearchBox("Filter tree by name or type");
        treeSearch.Text.ValueChanged += e =>
        {
            treeFilter = e.NewValue ?? string.Empty;
            nextTreeRefresh = double.MinValue;
        };

        treePane.Add(wrapSearchBox(treeSearch, "Tree Search"));

        var treeBody = new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Padding = new MarginPadding
            {
                Top = search_height
            },
            Name = "Tree Body"
        };

        treeBody.Add(emptyHint = new SpriteText
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(8, 8),
            Font = FontUsage.Default.With(size: 12),
            Color = Color.LightPink,
            Text = "Nothing chosen"
        });

        treeBody.Add(treeScroll = new ScrollableContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Name = "Tree View",
            Child = treeContent = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Width = 1,
                Name = "Tree Content"
            }
        });

        treePane.Add(treeBody);

        var propertyPane = new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            RelativePositionAxes = Axes.X,
            Position = new Vector2(width_split, 0),
            Size = new Vector2(1 - width_split, 1),
            Name = "Property Pane"
        };

        propertyPane.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Color = Color.Black,
            Alpha = 0.2f
        });

        propertySearch = createSearchBox("Filter properties and fields");
        propertySearch.Text.ValueChanged += e =>
        {
            propertyFilter = e.NewValue ?? string.Empty;
            nextPropertyRefresh = double.MinValue;
        };

        propertyPane.Add(wrapSearchBox(propertySearch, "Property Search"));

        var propertyBody = new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Padding = new MarginPadding { Top = search_height },
            Name = "Property Body"
        };

        propertyBody.Add(propertyScroll = new ScrollableContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Name = "Property View",
            Child = propertyFlow = new FlowContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Direction = FlowDirection.Vertical,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Width = 1,
                Spacing = new Vector2(0, 5),
                Name = "Property Flow"
            }
        });

        propertyPane.Add(propertyBody);

        panes.Add(treePane);
        panes.Add(propertyPane);

        Add(panes);
    }

    private Drawable buildToolbar()
    {
        var toolbar = new FlowContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Direction = FlowDirection.Horizontal,
            AutoSizeAxes = Axes.Both,
            Spacing = new Vector2(6, 0),
            Padding = new MarginPadding { Left = 6, Top = 4 }
        };

        toolbar.Add(new BasicButton
        {
            Text = "Choose Drawable",
            TextSize = 12,
            Size = new Vector2(120, 26),
            DefaultColor = Color.DarkMagenta,
            HoverColor = Color.Magenta,
            Action = ToggleInspectMode
        });

        toolbar.Add(new BasicButton
        {
            Text = "Up (Parent)",
            TextSize = 12,
            Size = new Vector2(100, 26),
            Action = () =>
            {
                // From the empty state this is the way to the whole tree, for the times you do want
                // all of it: one press, rather than making every open pay for it.
                if (targetRoot == null)
                    SetTreeRoot(absoluteRoot);
                else if (targetRoot.Parent != null)
                    SetTreeRoot(targetRoot.Parent);
            }
        });

        toolbar.Add(new BasicButton
        {
            Text = "Down (First Child)",
            TextSize = 12,
            Size = new Vector2(130, 26),
            Action = () =>
            {
                if (targetRoot is Container c && c.Children.Count > 0)
                    SetTreeRoot(c.Children[0]);
            }
        });

        return toolbar;
    }

    private static BasicTextBox createSearchBox(string placeholder) => new BasicTextBox
    {
        Anchor = Anchor.TopLeft,
        Origin = Anchor.TopLeft,
        RelativeSizeAxes = Axes.Both,
        Size = new Vector2(1),
        PlaceholderText = placeholder,
        BackgroundColor = Color.FromArgb(255, 34, 26, 32),
        BackgroundFocusedColor = Color.FromArgb(255, 62, 40, 56),
        // Filtering is live, just ignore enter on commit
        ReleaseFocusOnCommit = false
    };

    private static Container wrapSearchBox(BasicTextBox box, string name) => new Container
    {
        Anchor = Anchor.TopLeft,
        Origin = Anchor.TopLeft,
        RelativeSizeAxes = Axes.X,
        Size = new Vector2(1, search_height),
        Padding = new MarginPadding(3, 4),
        Name = name,
        Child = box
    };

    protected internal override void OnOpened()
    {
        base.OnOpened();

        if (Parent is DebugWindowLayer layer)
            layer.AttachOverlay(inspectLayer);
    }

    protected internal override void OnClosed()
    {
        base.OnClosed();

        // Inspect mode hides this window, so a close while inspecting would otherwise leave the
        // window hidden and the picker claiming the whole screen with nothing to return to.
        SetInspecting(false);

        if (Parent is DebugWindowLayer layer)
            layer.DetachOverlay(inspectLayer);
    }

    /// <summary>
    /// What the tree pane is rooted at, or null while nothing has been chosen.
    /// </summary>
    public Drawable? TreeRoot => targetRoot;

    /// <summary>
    /// The tree pane's search box. Matched case-insensitively against
    /// <see cref="Drawable.GetDisplayName"/>, so it covers both a drawable's name and its type.
    /// </summary>
    public Reactive<string> TreeSearchText => treeSearch.Text;

    /// <summary>
    /// The property pane's search box. Matched case-insensitively against member names.
    /// </summary>
    public Reactive<string> PropertySearchText => propertySearch.Text;

    /// <summary>
    /// Roots the tree pane at <paramref name="root"/>, or empties it when given null. Refreshes on
    /// the next frame rather than up to <see cref="tree_interval"/> later, since every caller is
    /// answering a click.
    /// </summary>
    public void SetTreeRoot(Drawable? root)
    {
        targetRoot = root;
        nextTreeRefresh = double.MinValue;
    }

    /// <summary>
    /// Whether inspect mode is picking. While it is, this window is hidden — hidden drawables are
    /// skipped when the positional input queue is built, which is what lets the picker reach a
    /// drawable underneath where the window was sitting.
    /// </summary>
    public bool IsInspecting => inspectLayer.Inspecting;

    public void ToggleInspectMode() => SetInspecting(!inspectLayer.Inspecting);

    public void SetInspecting(bool inspecting)
    {
        if (inspectLayer.Inspecting == inspecting)
            return;

        inspectLayer.Inspecting = inspecting;

        if (inspecting)
            this.FadeOut(150, Easing.OutQuint);
        else
            this.FadeIn(150, Easing.OutQuint);
    }

    /// <summary>
    /// While picking, this window claims no positional input at all.
    /// </summary>
    /// <remarks>
    /// It also fades out, but a fade is a transition and correctness must not wait for one: for the
    /// 150 ms it takes, a window that still claimed its rectangle would keep taking the clicks meant
    /// for whatever is underneath it — a window is a solid object by design
    /// (<see cref="DebugWindow.OnMouseDown"/>), and the default window covers a good part of the
    /// screen. Hidden drawables are dropped from the positional queue, so this is only about the gap
    /// before <see cref="Drawable.Alpha"/> gets there.
    /// </remarks>
    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
        => !inspectLayer.Inspecting && base.ReceivePositionalInputAt(screenSpacePos);

    public override void Update()
    {
        base.Update();

        // No visibility check and no releasing of content: a closed window is detached from the tree,
        // so none of this runs while it is closed.
        inspectLayer.UpdateSelection(selectedDrawable);

        if (Clock.CurrentTime >= nextTreeRefresh)
        {
            nextTreeRefresh = Clock.CurrentTime + tree_interval;
            refreshTree();
        }

        updateTreeViewport();

        if (Clock.CurrentTime >= nextPropertyRefresh)
        {
            nextPropertyRefresh = Clock.CurrentTime + property_interval;
            refreshProperties();
        }
    }

    /// <summary>
    /// Walks the app's tree into a flat (drawable, depth) list and replaces the cached one if it
    /// changed shape.
    /// </summary>
    private void refreshTree()
    {
        currentTreeStructure.Clear();
        buildTreeSnapshot(targetRoot, 0);

        if (currentTreeStructure.Count == 0)
        {
            emptyHint.Alpha = 1;
            emptyHint.Text = targetRoot != null && treeFilter.Length > 0 ? $"Nothing matches \"{treeFilter}\"" : "Nothing chosen";
        }
        else
            emptyHint.Alpha = 0;

        bool changed = currentTreeStructure.Count != cachedTreeStructure.Count;

        if (!changed)
        {
            for (int i = 0; i < currentTreeStructure.Count; i++)
            {
                if (currentTreeStructure[i].Drawable != cachedTreeStructure[i].Drawable ||
                    currentTreeStructure[i].Depth != cachedTreeStructure[i].Depth)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
            return;

        cachedTreeStructure.Clear();
        cachedTreeStructure.AddRange(currentTreeStructure);
    }

    /// <summary>
    /// Appends <paramref name="d"/> and its subtree to <see cref="currentTreeStructure"/>, and
    /// returns whether anything was kept.
    /// </summary>
    private bool buildTreeSnapshot(Drawable? d, int depth)
    {
        if (d == null)
            return false;

        int index = currentTreeStructure.Count;

        currentTreeStructure.Add((d, depth));

        bool keptBelow = false;

        if (d is not IRemoveFromDrawVisualiser && d is Container c)
        {
            for (int i = 0; i < c.Children.Count; i++)
                keptBelow |= buildTreeSnapshot(c.Children[i], depth + 1);
        }

        if (treeFilter.Length == 0 || keptBelow || d.GetDisplayName().Contains(treeFilter, StringComparison.OrdinalIgnoreCase))
            return true;

        // Nothing here or below matched. Non-matching descendants have already dropped themselves,
        // so this is the entry added above and nothing else.
        currentTreeStructure.RemoveRange(index, currentTreeStructure.Count - index);
        return false;
    }

    /// <summary>
    /// Binds the pooled rows to the slice of the tree the pane can actually show.
    /// </summary>
    private void updateTreeViewport()
    {
        int count = cachedTreeStructure.Count;

        float contentHeight = count * entry_height;

        if (!Precision.AlmostEquals(contentHeight, lastTreeContentHeight))
        {
            treeContent.Height = contentHeight;
            lastTreeContentHeight = contentHeight;
        }

        float viewport = treeScroll.ChildSize.Y;
        float scroll = treeScroll.CurrentScroll.Y;

        // One row of slack each way, so a row is bound before it is scrolled into view rather than
        // appearing as it arrives.
        int first = Math.Max(0, (int)(scroll / entry_height) - 1);
        int last = Math.Min(count - 1, (int)((scroll + viewport) / entry_height) + 1);
        int needed = Math.Max(0, last - first + 1);

        while (rowPool.Count < needed)
        {
            var row = new VisualiserTreeItem(() => selectedDrawable, SelectDrawable)
            {
                Height = entry_height
            };

            rowPool.Add(row);
            treeContent.Add(row);
        }

        for (int i = 0; i < rowPool.Count; i++)
        {
            var row = rowPool[i];

            if (i >= needed)
            {
                // Hidden rather than removed: a hidden subtree is skipped by both input queues and
                // costs a container's worth of traversal, and the pane is about to need it again.
                row.Bind(null, 0);
                row.Alpha = 0;
                continue;
            }

            int index = first + i;
            var entry = cachedTreeStructure[index];

            row.Bind(entry.Drawable, entry.Depth);
            row.Y = index * entry_height;
            row.Alpha = 1;
        }
    }

    /// <summary>
    /// Shows <paramref name="d"/> in the property pane, as clicking its row does. Does not move the
    /// tree root.
    /// </summary>
    public void SelectDrawable(Drawable d)
    {
        // A filter narrowed against the last drawable's members says nothing about a different one,
        // and at worst matches none of them, so the pane a new selection opens in is the unfiltered
        // one. Only an actual change resets it, so re-clicking the selected row keeps the filter.
        if (!ReferenceEquals(selectedDrawable, d))
            propertySearch.Text.Value = string.Empty;

        selectedDrawable = d;

        // Straight away rather than on the next tick: this is a click being answered.
        refreshProperties();
        nextPropertyRefresh = Clock.CurrentTime + property_interval;
    }

    private void refreshProperties()
    {
        if (selectedDrawable == null)
        {
            propertyFlow.Clear();
            propertyTextMap.Clear();
            lastSelectedDrawable = null;
            cachedProperties = null;
            cachedFields = null;
            loadStateText = null;
            return;
        }

        var type = selectedDrawable.GetType();

        bool selectionChanged = lastSelectedDrawable != selectedDrawable;

        // The member lists depend on the selection alone, so a keystroke in the search box re-filters
        // rows without walking the inheritance chain again.
        if (selectionChanged)
        {
            var propList = new List<PropertyInfo>();
            var fieldList = new List<FieldInfo>();
            Type? currentType = type;

            // Walk up the inheritance tree to capture all private base members
            while (currentType != null && currentType != typeof(object))
            {
                var props = currentType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

                foreach (var prop in props)
                {
                    if (prop.GetIndexParameters().Length > 0) continue;

                    // If it's a container, don't track "child" or "children" properties.
                    if (selectedDrawable is Container &&
                        (string.Equals(prop.Name, "Child", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(prop.Name, "Children", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Prevent duplicate properties (e.g., if derived class uses 'new' keyword)
                    if (!propList.Exists(p => p.Name == prop.Name))
                        propList.Add(prop);
                }

                var fields = currentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

                foreach (var field in fields)
                {
                    // Filter out compiler-generated backing fields for auto-properties
                    if (field.Name.Contains("k__BackingField")) continue;

                    if (!fieldList.Exists(f => f.Name == field.Name))
                        fieldList.Add(field);
                }

                currentType = currentType.BaseType;
            }

            cachedProperties = propList.ToArray();
            cachedFields = fieldList.ToArray();
        }

        // Rebuild the rows when the selection or the filter changed; otherwise just re-read the
        // values.
        if (selectionChanged || lastPropertyFilter != propertyFilter)
        {
            propertyFlow.Clear();
            propertyTextMap.Clear();
            lastSelectedDrawable = selectedDrawable;
            lastPropertyFilter = propertyFilter;

            // The type is the heading of the pane rather than one of its rows, so it survives every filter
            addPropertyText($"Type: {type.Name}", Color.Yellow);

            loadStateText = matchesPropertyFilter("Load State")
                ? addPropertyText($"Load State: {selectedDrawable.IsLoaded}", Color.White)
                : null;

            foreach (var prop in cachedProperties!)
            {
                if (!matchesPropertyFilter(prop.Name))
                    continue;

                var textElement = addPropertyText($"{prop.Name}: loading...", Color.White);
                propertyTextMap.Add(new PropertyTracker
                {
                    Prop = prop,
                    TextElement = textElement
                });
            }

            foreach (var field in cachedFields!)
            {
                if (!matchesPropertyFilter(field.Name))
                    continue;

                var textElement = addPropertyText($"{field.Name}: loading...", Color.White);
                propertyTextMap.Add(new PropertyTracker
                {
                    Field = field,
                    TextElement = textElement
                });
            }
        }

        if (loadStateText != null)
            loadStateText.Text = $"Load State: {selectedDrawable.IsLoaded}";

        foreach (var tracker in propertyTextMap)
        {
            try
            {
                object? val = tracker.Prop != null
                    ? tracker.Prop.GetValue(selectedDrawable)
                    : tracker.Field?.GetValue(selectedDrawable);

                string valStr = val?.ToString() ?? "null";

                if (valStr == tracker.LastStringValue) continue;

                tracker.LastStringValue = valStr;

                var textColor = Color.White;
                if (val is bool b) textColor = b ? Color.Green : Color.Red;
                else if (val is ValueType) textColor = Color.Cyan;

                bool isPrivate = (tracker.Prop?.GetMethod != null && !tracker.Prop.GetMethod.IsPublic) ||
                                 (tracker.Field != null && tracker.Field.IsPrivate);

                if (isPrivate) textColor = Color.LightGray;

                string name = tracker.Prop != null ? tracker.Prop.Name : tracker.Field!.Name;
                tracker.TextElement.Text = $"{name}: {valStr}";
                tracker.TextElement.Color = textColor;
            }
            catch
            {
                // Silently handle properties that throw exceptions on get
            }
        }
    }

    /// <summary>
    /// Whether a member of this name survives the property search box
    /// </summary>
    private bool matchesPropertyFilter(string name)
        => propertyFilter.Length == 0 || name.Contains(propertyFilter, StringComparison.OrdinalIgnoreCase);

    private SpriteText addPropertyText(string text, Color color)
    {
        var spriteText = new SpriteText
        {
            Text = text,
            Color = color,
            Font = FontUsage.Default.With(size: 14),
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Position = new Vector2(5, 0)
        };

        propertyFlow.Add(new Container
        {
            RelativeSizeAxes = Axes.X,
            Size = new Vector2(1, 15),
            Child = spriteText
        });

        return spriteText;
    }

    private class PropertyTracker
    {
        public PropertyInfo? Prop;
        public FieldInfo? Field;
        public SpriteText TextElement = null!;
        public string? LastStringValue;
    }
}

/// <summary>
/// <see cref="DrawVisualiser"/>'s highlight boxes and its inspect picker, in screen space.
/// </summary>
/// <remarks>
/// Hosted by <see cref="DebugWindowLayer"/> as a sibling of the window rather than a child of it, for
/// two reasons: a highlight drawn inside the window's body would be clipped to the window, and
/// picking has to be able to reach a drawable anywhere on screen. While inspecting, this is
/// deliberately modal over the whole screen — that is what picking means — and it claims nothing at
/// all the rest of the time.
/// </remarks>
public partial class DrawVisualiserInspectLayer : Container
{
    private readonly Drawable pickRoot;
    private readonly Box selectionHighlight;
    private readonly Box inspectHighlight;

    private Drawable? hovered;
    private Drawable? lastFlashed;

    /// <summary>Called with the drawable the user clicked while inspecting.</summary>
    public Action<Drawable>? Picked { get; init; }

    private bool inspecting;

    public bool Inspecting
    {
        get => inspecting;
        set
        {
            if (inspecting == value)
                return;

            inspecting = value;
            hovered = null;
            lastFlashed = null;

            inspectHighlight.Alpha = value ? 0.5f : 0;
        }
    }

    public DrawVisualiserInspectLayer(Drawable pickRoot)
    {
        this.pickRoot = pickRoot;

        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;

        Add(selectionHighlight = new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Color = Color.Red,
            Alpha = 0,
            Blending = BlendingMode.Additive
        });

        Add(inspectHighlight = new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Color = Color.LimeGreen,
            Alpha = 0,
            Blending = BlendingMode.Additive
        });
    }

    /// <summary>
    /// Follows the selected drawable with the red highlight. Called by the window every frame, since
    /// the thing being highlighted, is free to move.
    /// </summary>
    public void UpdateSelection(Drawable? selected)
    {
        if (selected == null || !selected.IsAlive || selected.Parent == null)
        {
            selectionHighlight.Alpha = 0;
            return;
        }

        selectionHighlight.Alpha = 0.4f;

        var rect = selected.DrawRectangle;
        selectionHighlight.Position = new Vector2(rect.X, rect.Y);
        selectionHighlight.Size = new Vector2(rect.Width, rect.Height);
    }

    /// <summary>
    /// Claims the whole screen while picking, and nothing when not — positional dispatch only
    /// descends into a drawable that says the cursor is over it, so this is the whole of the
    /// modality, in one line.
    /// </summary>
    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => inspecting;

    public override void Update()
    {
        base.Update();

        if (!inspecting)
            return;

        // From the input state rather than only from move events: content moves under a stationary
        // cursor, and the highlight should follow that too.
        var manager = GetContainingInputManager();

        if (manager != null)
            updateInspectHighlight(manager.CurrentState.MousePosition);
    }

    public override bool OnMouseMove(MouseEvent e)
    {
        if (!inspecting)
            return false;

        updateInspectHighlight(e.ScreenSpaceMousePosition);
        return true;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (!inspecting)
            return false;

        if (hovered != null)
            Picked?.Invoke(hovered);
        else
            Inspecting = false;

        return true;
    }

    public override bool OnHover(MouseEvent e) => inspecting;

    private void updateInspectHighlight(Vector2 screenSpaceMousePosition)
    {
        hovered = findDrawableUnderMouse(pickRoot, screenSpaceMousePosition);

        if (hovered == null)
            return;

        var rect = hovered.DrawRectangle;
        inspectHighlight.Position = new Vector2(rect.X, rect.Y);
        inspectHighlight.Size = new Vector2(rect.Width, rect.Height);

        if (hovered != lastFlashed)
        {
            inspectHighlight.Color = Color.LimeGreen;
            inspectHighlight.FlashColor(Color.White, 300, Easing.OutQuint);
            lastFlashed = hovered;
        }
    }

    private Drawable? findDrawableUnderMouse(Drawable root, Vector2 mousePos)
    {
        // The debug tools are not part of the app being inspected. The layer is marked, so this also
        // skips every window and this overlay itself in one check.
        if (root is IRemoveFromDrawVisualiser)
            return null;

        // skip dead, hidden, or culled items
        if (!root.IsAlive || !root.IsLoaded || root.IsHidden || root.IsMaskedAway && !root.AlwaysPresent || root.Alpha <= 0)
            return null;

        if (!root.Contains(mousePos))
            return null;

        if (root is Container c)
        {
            // Search children from front to back (reverse depth order) to catch the topmost UI element first
            foreach (var child in c.Children.OrderBy(d => d.Depth).Reverse())
            {
                var found = findDrawableUnderMouse(child, mousePos);

                if (found != null)
                    return found;
            }
        }

        return root;
    }
}

/// <summary>
/// One line of <see cref="DrawVisualiser"/>'s tree pane. Pooled and re-bound as the pane scrolls, so
/// these exist per visible line rather than per drawable in the app.
/// </summary>
public partial class VisualiserTreeItem : Container
{
    private readonly Func<Drawable?> selectedProvider;
    private readonly Action<Drawable> clickAction;

    private readonly Box background;
    private readonly SpriteText label;

    private Drawable? tracked;
    private int trackedDepth = -1;

    /// <summary>
    /// The tree entry this row is currently showing, or null while it is one of the pooled rows the
    /// pane is not using.
    /// </summary>
    public Drawable? Tracked => tracked;

    public VisualiserTreeItem(Func<Drawable?> selectedProvider, Action<Drawable> clickAction)
    {
        this.selectedProvider = selectedProvider;
        this.clickAction = clickAction;

        RelativeSizeAxes = Axes.X;
        Width = 1;

        Add(background = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        });

        Add(label = new SpriteText
        {
            Font = FontUsage.Default.With(size: 14),
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft
        });
    }

    /// <summary>
    /// Points this row at a tree entry, or at nothing. A no-op when already bound to that entry,
    /// which is the common case: scrolling re-binds every visible row every frame, and re-setting the
    /// text would re-shape it.
    /// </summary>
    public void Bind(Drawable? drawable, int depth)
    {
        if (ReferenceEquals(tracked, drawable) && trackedDepth == depth)
            return;

        tracked = drawable;
        trackedDepth = depth;

        label.Text = drawable?.GetDisplayName() ?? string.Empty;
        label.X = depth * 15 + 5;
    }

    public override void Update()
    {
        base.Update();

        if (tracked == null)
        {
            background.Alpha = 0;
            return;
        }

        bool isSelected = ReferenceEquals(selectedProvider(), tracked);

        var bgColor = isSelected ? Color.Blue : Color.Transparent;
        if (tracked.IsHovered) bgColor = Color.Gray;

        background.Color = bgColor;
        background.Alpha = isSelected ? 0.5f : 0.2f;

        bool isUpdating = tracked.IsLoaded &&
                          tracked.IsAlive &&
                          tracked.Size != Vector2.Zero &&
                          (!tracked.IsMaskedAway || tracked.AlwaysPresent);

        bool isDrawing = isUpdating && tracked.DrawAlpha > 0;

        if (isSelected)
        {
            label.Color = Color.Yellow;
            label.Alpha = 1;
        }
        else
        {
            label.Color = Color.White;
            label.Alpha = isUpdating && isDrawing ? 1 : 0.4f;
        }
    }

    public override bool OnDrag(MouseEvent e) => false;

    public override bool OnClick(MouseButtonEvent e)
    {
        if (tracked != null)
            clickAction(tracked);

        return true;
    }
}
