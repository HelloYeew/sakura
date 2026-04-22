// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sakura.Framework.Development;
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
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Performance;

public class DrawVisualiser : FocusedOverlayContainer, IRemoveFromDrawVisualiser
{
    private Drawable targetRoot;
    private readonly Drawable absoluteRoot;
    private readonly Box backgroundBox;
    private readonly FlowContainer treeFlow;
    private readonly ScrollableContainer parentTreeFlow;
    private readonly ScrollableContainer parentPropertyFlow;
    private readonly FlowContainer propertyFlow;
    private readonly Container leftContainer;
    private readonly Container rightContainer;
    private readonly Box highlightBox;
    private Drawable? selectedDrawable;
    private readonly SpriteText currentTimeText;
    private readonly SpriteText runningTimeText;

    private bool isInspecting;
    private Drawable? hoveredDrawable;
    private readonly Box inspectHighlightBox;
    private readonly FlowContainer headerButtonsFlow;
    private Drawable lastHoveredDrawable;

    private Drawable lastSelectedDrawable;
    private PropertyInfo[] cachedProperties;
    private FieldInfo[] cachedFields;
    private SpriteText loadStateText;
    private readonly List<PropertyTracker> propertyTextMap = new();
    private readonly List<(Drawable drawable, int depth)> cachedTreeStructure = new();
    private readonly List<(Drawable drawable, int depth)> currentTreeStructure = new();

    private const float width_split = 0.4f;
    private const float entry_height = 20;

    public DrawVisualiser(Drawable root)
    {
        absoluteRoot = root;
        targetRoot = root;
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;

        Add(highlightBox = new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Color = Color.Red,
            Alpha = 0,
            Blending = BlendingMode.Additive
        });

        // Background
        Add(backgroundBox = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = Color.Black,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(1),
            Alpha = 0.75f
        });

        Add(inspectHighlightBox = new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Color = Color.LimeGreen,
            Alpha = 0,
            Blending = BlendingMode.Additive,
            Depth = float.MinValue
        });

        // Header
        Add(new SpriteText
        {
            Text = "Draw Visualiser (Ctrl + F1)",
            Font = FontUsage.Default.With(size: 30, weight: "Bold"),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 5),
            Color = Color.Pink,
            RelativeSizeAxes = Axes.X,
            Height = 50
        });

        Add(currentTimeText = new SpriteText
        {
            Text = "",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 50),
            Color = Color.LightPink,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(runningTimeText = new SpriteText
        {
            Text = "",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 70),
            Color = Color.LightPink,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(new SpriteText()
        {
            Text =
                $"Sakura Framework v{DebugUtils.GetFrameworkVersion()}",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-10, 50),
            Color = Color.LightPink,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(new SpriteText()
        {
            Text = $"Running {Logger.AppIdentifier} v{Logger.VersionIdentifier} {(DebugUtils.IsDebugBuild ? "(Debug Build)" : "")}",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-10, 70),
            Color = Color.LightPink,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(headerButtonsFlow = new FlowContainer
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-10, 10),
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(10, 0)
        });

        headerButtonsFlow.Add(new BasicButton
        {
            Text = "Up (Parent)",
            Action = () =>
            {
                if (targetRoot.Parent != null)
                {
                    targetRoot = targetRoot.Parent;
                    timeUntilNextTreeRefresh = 0; // Force immediate refresh
                }
            }
        });

        headerButtonsFlow.Add(new BasicButton
        {
            Text = "Down (First Child)",
            Action = () =>
            {
                if (targetRoot is Container c && c.Children.Count > 0)
                {
                    targetRoot = c.Children[0];
                    timeUntilNextTreeRefresh = 0; // Force immediate refresh
                }
            }
        });

        headerButtonsFlow.Add(new BasicButton
        {
            Text = "Inspect",
            DefaultColor = Color.DarkMagenta,
            HoverColor = Color.Magenta,
            Action = toggleInspectMode
        });

        // Tree view (left)
        Add(leftContainer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Size = new Vector2(width_split, 0.75f),
            RelativePositionAxes = Axes.X,
            Name = "Left Container"
        });

        leftContainer.Add(new Box()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Color = Color.Black,
            Alpha = 0.2f,
            Size = new Vector2(1)
        });

        leftContainer.Add(parentTreeFlow = new ScrollableContainer()
        {
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(1),
            RelativePositionAxes = Axes.Both,
            Name = "Tree View Container"
        });

        treeFlow = new FlowContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Spacing = new Vector2(0, 2),
            Width = 1f,
            Name = "Tree Flow"
        };

        parentTreeFlow.Add(treeFlow);

        // Property (right)
        Add(rightContainer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Size = new Vector2(1f - width_split, 0.75f),
            RelativePositionAxes = Axes.X,
            Position = new Vector2(width_split, 0),
            Name = "Right Container"
        });

        rightContainer.Add(new Box()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Color = Color.Black,
            Alpha = 0.2f,
            Size = new Vector2(1)
        });

        rightContainer.Add(parentPropertyFlow = new ScrollableContainer()
        {
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(1),
            RelativePositionAxes = Axes.Both,
            Name = "Property View Container"
        });

        propertyFlow = new FlowContainer()
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Spacing = new Vector2(0, 5),
            Width = 1f,
            Name = "Property Flow"
        };

        parentPropertyFlow.Add(propertyFlow);
    }

    private double timeUntilNextTreeRefresh;
    private double timeUntilNextPropertyRefresh;

    public override void Update()
    {
        base.Update();

        if (State == Visibility.Hidden) return;

        timeUntilNextPropertyRefresh -= Clock.ElapsedFrameTime;
        if (timeUntilNextPropertyRefresh <= 0)
        {
            refreshProperties();

            currentTimeText.Text = $"{DateTime.Now:dd MMMM yyyy HH:mm:ss tt}";
            runningTimeText.Text = $"Has been running for {TimeSpan.FromSeconds(targetRoot.Clock.CurrentTime / 1000):hh\\:mm\\:ss}";

            timeUntilNextPropertyRefresh = 20;
        }

        timeUntilNextTreeRefresh -= Clock.ElapsedFrameTime;
        if (timeUntilNextTreeRefresh <= 0)
        {
            refreshTree();
            timeUntilNextTreeRefresh = 500;
        }

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

        currentTimeText.Text = $"{DateTime.Now:dd MMMM yyyy HH:mm:ss tt}";
        runningTimeText.Text = $"Has been running for {TimeSpan.FromSeconds(targetRoot.Clock.CurrentTime / 1000):hh\\:mm\\:ss}";
    }

    private void toggleInspectMode()
    {
        isInspecting = !isInspecting;

        if (isInspecting)
        {
            highlightBox.Alpha = 0;
            leftContainer.FadeOut(200, Easing.OutQuint);
            rightContainer.FadeOut(200, Easing.OutQuint);
            backgroundBox.FadeTo(0.1f, 200, Easing.OutQuint);
            headerButtonsFlow.FadeOut(200, Easing.OutQuint);
            inspectHighlightBox.Alpha = 0.5f;
            hoveredDrawable = null;
        }
        else
        {
            leftContainer.FadeIn(200, Easing.OutQuint);
            rightContainer.FadeIn(200, Easing.OutQuint);
            backgroundBox.FadeTo(0.75f, 200, Easing.OutQuint);
            headerButtonsFlow.FadeIn(200, Easing.OutQuint);
            inspectHighlightBox.Alpha = 0;
        }
    }

    public override bool OnMouseMove(MouseEvent e)
    {
        if (isInspecting)
        {
            hoveredDrawable = findDrawableUnderMouse(absoluteRoot, e.ScreenSpaceMousePosition);

            if (hoveredDrawable != null)
            {
                var rect = hoveredDrawable.DrawRectangle;
                inspectHighlightBox.Position = new Vector2(rect.X, rect.Y);
                inspectHighlightBox.Size = new Vector2(rect.Width, rect.Height);
                if (hoveredDrawable != lastHoveredDrawable)
                {
                    inspectHighlightBox.Color = Color.LimeGreen;
                    inspectHighlightBox.FlashColour(Color.White, 300, Easing.OutQuint);
                }
                lastHoveredDrawable = hoveredDrawable;
            }
            return true;
        }

        return base.OnMouseMove(e);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (isInspecting)
        {
            if (hoveredDrawable != null)
            {
                targetRoot = hoveredDrawable;
                timeUntilNextTreeRefresh = 0;
            }

            toggleInspectMode();
            return true;
        }

        return base.OnMouseDown(e);
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (isInspecting && e.Key == Key.Escape)
        {
            toggleInspectMode();
            return true;
        }

        return base.OnKeyDown(e);
    }

    private Drawable? findDrawableUnderMouse(Drawable root, Vector2 mousePos)
    {
        // skip the visualiser itself to prevent infinite loops / selecting the debugger
        if (root == this || root is IRemoveFromDrawVisualiser)
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

    private void refreshTree()
    {
        if (targetRoot == null) return;

        // Gather the current state of the game's tree
        currentTreeStructure.Clear();
        buildTreeSnapshot(targetRoot, 0);

        // Compare with cached state
        bool treeChanged = false;
        if (currentTreeStructure.Count != cachedTreeStructure.Count)
        {
            treeChanged = true;
        }
        else
        {
            for (int i = 0; i < currentTreeStructure.Count; i++)
            {
                if (currentTreeStructure[i].drawable != cachedTreeStructure[i].drawable ||
                    currentTreeStructure[i].depth != cachedTreeStructure[i].depth)
                {
                    treeChanged = true;
                    break;
                }
            }
        }

        // Only rebuild the heavy UI if the hierarchy actually changed
        if (treeChanged)
        {
            cachedTreeStructure.Clear();
            cachedTreeStructure.AddRange(currentTreeStructure);

            treeFlow.Clear();

            for (int i = 0; i < cachedTreeStructure.Count; i++)
            {
                var item = cachedTreeStructure[i];

                // Pass a function so the item can check if it's currently selected
                var entry = new VisualiserTreeItem(item.drawable, item.depth,() => item.drawable == selectedDrawable)
                {
                    Position = new Vector2(0, i * 20),
                    Height = 20,
                    ClickAction = () => selectDrawable(item.drawable)
                };

                treeFlow.Add(entry);
            }
        }
    }

    private void buildTreeSnapshot(Drawable d, int depth)
    {
        if (d == null) return;

        Drawable cursor = d;
        while (cursor != null)
        {
            if (cursor == this) return;
            cursor = cursor.Parent;
        }

        currentTreeStructure.Add((d, depth));

        if (d is IRemoveFromDrawVisualiser) return;

        if (d is Container c)
        {
            for (int i = 0; i < c.Children.Count; i++)
            {
                buildTreeSnapshot(c.Children[i], depth + 1);
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
        // If nothing is selected, clear everything and reset our cache
        if (selectedDrawable == null)
        {
            propertyFlow.Clear();
            propertyTextMap.Clear();
            lastSelectedDrawable = null;
            cachedProperties = null;
            cachedFields = null;
            return;
        }

        Type type = selectedDrawable.GetType();

        // Rebuild UI layout only if we've selected a different drawable than last time.
        if (lastSelectedDrawable != selectedDrawable)
        {
            propertyFlow.Clear();
            propertyTextMap.Clear();
            lastSelectedDrawable = selectedDrawable;

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

            // Header (Static, doesn't need to be tracked)
            addPropertyText($"Type: {type.Name}", Color.Yellow);

            // Load State (Tracked manually)
            loadStateText = addPropertyText($"Load State: {selectedDrawable.IsLoaded}", Color.White);

            // Properties
            foreach (var prop in cachedProperties)
            {
                var textElement = addPropertyText($"{prop.Name}: loading...", Color.White);
                propertyTextMap.Add(new PropertyTracker
                {
                    Prop = prop,
                    TextElement = textElement,
                    LastStringValue = null
                });
            }

            foreach (var field in cachedFields)
            {
                var textElement = addPropertyText($"{field.Name}: loading...", Color.White);
                propertyTextMap.Add(new PropertyTracker
                {
                    Field = field,
                    TextElement = textElement
                });
            }
        }

        // Update state in existing UI elements
        if (loadStateText != null)
        {
            loadStateText.Text = $"Load State: {selectedDrawable.IsLoaded}";
        }

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

                Color textColor = Color.White;
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

    private SpriteText addPropertyText(string text, Color color)
    {
        var spriteText = new SpriteText
        {
            Text = text,
            Color = color,
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Position = new Vector2(5, 0),
            Size = new Vector2(30, 30)
        };

        propertyFlow.Add(new Container()
        {
            RelativeSizeAxes = Axes.X,
            Size = new Vector2(1, 15),
            Child = spriteText
        });

        return spriteText;
    }

    protected override void PopIn() => this.FadeIn(200, Easing.OutQuint);

    protected override void PopOut() => this.FadeOut(200, Easing.OutQuint);

    private class PropertyTracker
    {
        public PropertyInfo Prop;
        public FieldInfo? Field;
        public SpriteText TextElement;
        public string? LastStringValue;
    }
}

public class VisualiserTreeItem : Container
{
    public Action ClickAction;
    private readonly Drawable trackedDrawable;
    private readonly Func<bool> checkSelected;

    private readonly Box background;
    private readonly SpriteText label;

    public VisualiserTreeItem(Drawable d, int depth, Func<bool> isSelected)
    {
        trackedDrawable = d;
        checkSelected = isSelected;

        RelativeSizeAxes = Axes.X;
        Height = 20;
        Width = 1f;

        Add(background = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        });

        Add(label = new SpriteText
        {
            Text = d.GetDisplayName(),
            Position = new Vector2(depth * 15 + 5, 0),
            Font = FontUsage.Default.With(size: 14),
            Size = new Vector2(1, 20),
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft
        });
    }

    public override void Update()
    {
        base.Update();

        bool isSelected = checkSelected();

        var bgColor = isSelected ? Color.Blue : Color.Transparent;
        if (trackedDrawable.IsHovered) bgColor = Color.Gray;

        background.Color = bgColor;
        background.Alpha = isSelected ? 0.5f : 0.2f;

        bool isUpdating = trackedDrawable.IsLoaded &&
                          trackedDrawable.IsAlive &&
                          trackedDrawable.Size != Vector2.Zero &&
                          (!trackedDrawable.IsMaskedAway || trackedDrawable.AlwaysPresent);

        bool isDrawing = isUpdating && trackedDrawable.DrawAlpha > 0;

        if (isSelected)
        {
            label.Color = Color.Yellow;
            label.Alpha = 1.0f;
        }
        else
        {
            label.Color = Color.White;
            label.Alpha = isUpdating && isDrawing ? 1.0f : 0.4f;
        }
    }

    public override bool OnDrag(MouseEvent e)
    {
        return false;
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        ClickAction?.Invoke();
        return true;
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            return true;
        }
        return base.OnKeyDown(e);
    }
}
