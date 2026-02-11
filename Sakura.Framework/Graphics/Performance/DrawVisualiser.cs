// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Reflection;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Performance;

public class DrawVisualiser : Container
{
    private readonly Drawable targetRoot;
    private readonly FlowContainer treeFlow;
    private readonly FlowContainer propertyFlow;
    private readonly Box highlightBox;
    private Drawable selectedDrawable;

    // UI Constants
    private const float WIDTH_SPLIT = 0.4f; // 40% for tree, 60% for props
    private const float ENTRY_HEIGHT = 20;

    public DrawVisualiser(Drawable root)
    {
        targetRoot = root;
        RelativeSizeAxes = Axes.Both;
        // Depth = float.MinValue; // Ensure it draws on top
        Size = new Vector2(1);
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        AlwaysPresent = true;

        // 1. Background
        Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = Color.Blue,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(1),
            Alpha = 0.8f
        });

        // 2. Tree View (Left Side)
        Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(WIDTH_SPLIT, 1f),
            Child = treeFlow = new FlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Width = 1f
            }
        });

        // 3. Property View (Right Side)
        Add(new Container()
        {
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(1f - WIDTH_SPLIT, 1f),
            RelativePositionAxes = Axes.X,
            Position = new Vector2(WIDTH_SPLIT, 0),
            Name = "Property View Container",
            // Width = WIDTH_SPLIT,
            Child = propertyFlow = new FlowContainer()
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Left = 10 },
                Width = 1f
            }
        });

        // 4. Highlight Box (Overlay)
        // We add this to a separate container that doesn't mask, or just on top.
        // It needs to follow the target drawable's screen position.
        Add(highlightBox = new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Color = Color.Red,
            Alpha = 0, // Hidden by default
            // Blending = BlendingParameters.Additive // Optional: if you have blending
        });
    }

    private double timeUntilNextRefresh;

    public override void Update()
    {
        base.Update();

        // Refresh the tree periodically or if dirty (Doing every frame for simplicity here,
        // in production you might want to throttle this or only do it on structure change)
        timeUntilNextRefresh -= Clock.ElapsedFrameTime;
        if (timeUntilNextRefresh <= 0)
        {
            refreshTree();
            refreshProperties();
            timeUntilNextRefresh = 200;
        }

        // Update highlight box position
        if (selectedDrawable != null && selectedDrawable.IsAlive && selectedDrawable.Parent != null)
        {
            highlightBox.Alpha = 0.4f;

            // DrawRectangle is in absolute/screen space.
            // Since Visualiser is usually full screen, we can map directly.
            var rect = selectedDrawable.DrawRectangle;
            highlightBox.Position = new Vector2(rect.X, rect.Y);
            highlightBox.Size = new Vector2(rect.Width, rect.Height);
        }
        else
        {
            highlightBox.Alpha = 0;
        }
    }

    private void refreshTree()
    {
        // NOTE: In a real implementation, you shouldn't clear and recreate children every frame
        // as it generates garbage. You should pool items or sync the list.
        // For this MVP, we clear to ensure accuracy.
        treeFlow.Clear();

        if (targetRoot == null) return;

        scanDrawable(targetRoot, 0);
    }

    private void scanDrawable(Drawable d, int depth)
    {
        // 1. Safety Check: If d is null, stop.
        if (d == null) return;

        // 2. Filter out the Visualiser and all its descendants.
        // We check if the drawable is the visualiser, or if it is inside the visualiser.
        Drawable cursor = d;
        while (cursor != null)
        {
            if (cursor == this) return; // Found visualiser in parent chain -> Skip
            cursor = cursor.Parent;
        }

        // 3. Create UI entry
        var entry = new VisualiserTreeItem(d, depth, d == selectedDrawable);
        entry.Position = new Vector2(0, treeFlow.Children.Count * 20); // Force Y position
        entry.Height = 20;
        // Avoid selecting the App itself to prevent deadlock on selection
        if (selectedDrawable is not App)
            entry.ClickAction = () => selectDrawable(d);
        treeFlow.Add(entry);

        if (d is FpsGraph || d is DrawVisualiser)
        {
            // Skip children of known heavy or self-referential drawables
            return;
        }

        // 4. Recurse
        if (d is Container c)
        {
            // Iterate backwards or forwards? usually forwards for tree view.
            // We use a copy or loop by index to avoid 'Collection Modified' errors
            // if the game modifies children during this frame.
            for (int i = 0; i < c.Children.Count; i++)
            {
                scanDrawable(c.Children[i], depth + 1);
            }
        }
    }

    private void selectDrawable(Drawable d)
    {
        selectedDrawable = d;
        refreshProperties();
    }

    private void refreshProperties()
    {
        propertyFlow.Clear();
        if (selectedDrawable == null) return;

        Type type = selectedDrawable.GetType();

        // Header
        addPropertyText($"Type: {type.Name}", Color.Yellow);
        addPropertyText($"Load State: {selectedDrawable.IsLoaded}", Color.White);

        // Reflection properties
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            try
            {
                // Skip indexers or strict setters
                if (prop.GetIndexParameters().Length > 0) continue;

                object val = prop.GetValue(selectedDrawable);
                string valStr = val?.ToString() ?? "null";

                // Colorize based on type
                Color textColor = Color.White;
                if (val is bool b) textColor = b ? Color.Green : Color.Red;
                if (val is ValueType) textColor = Color.Cyan;

                addPropertyText($"{prop.Name}: {valStr}", textColor);
            }
            catch { }
        }
    }

    private void addPropertyText(string text, Color color)
    {
        propertyFlow.Add(new Container()
        {
            // Color = color,
            Name = "PropertyFlow Text",
            RelativeSizeAxes = Axes.X,
            Size = new Vector2(1, 30),
            Child = new SpriteText
            {
                Text = text,
                Color = color,
                Font = FontUsage.Default.With(size: 16),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Position = new Vector2(5, 0),
                Size = new Vector2(30, 30),
                // RelativeSizeAxes = Axes.X
            }
            // Font = FontUsage.Default.With(size:16) // Adjust font as per your resources
        });
    }
}

// A helper class for the tree items
public class VisualiserTreeItem : Container
{
    public Action ClickAction;
    private Drawable trackedDrawable;

    public VisualiserTreeItem(Drawable d, int depth, bool isSelected)
    {
        trackedDrawable = d;
        RelativeSizeAxes = Axes.X;
        Height = 20;
        Width = 1f;

        var color = isSelected ? Color.Blue : Color.Transparent;
        if (d.IsHovered) color = Color.Gray; // Simple hover feedback from game state

        Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Color = color,
            Alpha = isSelected ? 0.5f : 0.2f,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        });

        Add(new SpriteText
        {
            Text = d.GetDisplayName(),
            Position = new Vector2(depth * 15 + 5, 0),
            Color = isSelected ? Color.Yellow : Color.White,
            Font = FontUsage.Default.With(size: 14),
            Size = new Vector2(1, 20),
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft
        });
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        ClickAction?.Invoke();
        return true;
    }
}
