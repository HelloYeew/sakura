// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Reflection;
using Sakura.Framework.Development;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Performance;

public class DrawVisualiser : Container
{
    private readonly Drawable targetRoot;
    private readonly Box backgroundBox;
    private readonly FlowContainer treeFlow;
    private readonly FlowContainer propertyFlow;
    private readonly ScrollableContainer parentPropertyFlow;
    private readonly Container rightContainer;
    private readonly Box highlightBox;
    private Drawable selectedDrawable;
    private readonly SpriteText currentTimeText;
    private readonly SpriteText runningTimeText;

    private Drawable lastSelectedDrawable;
    private PropertyInfo[] cachedProperties;
    private SpriteText loadStateText;
    private readonly List<(PropertyInfo prop, SpriteText textElement)> propertyTextMap = new();
    private readonly List<(Drawable drawable, int depth)> cachedTreeStructure = new();
    private readonly List<(Drawable drawable, int depth)> currentTreeStructure = new();

    private const float WIDTH_SPLIT = 0.4f;
    private const float ENTRY_HEIGHT = 20;

    public DrawVisualiser(Drawable root)
    {
        targetRoot = root;
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        AlwaysPresent = true;

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

        // Header
        Add(new SpriteText
        {
            Text = "Draw Visualiser (Ctrl + F2)",
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

        // Footer
        Add(new SpriteText
        {
            Text = "Note : Use ArrowDown and ArrowUp to dim or brighten the background!",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Position = new Vector2(10, -10),
            Color = Color.LightPink,
            RelativeSizeAxes = Axes.X,
            Height = 30
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

        Add(rightContainer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Size = new Vector2(1f - WIDTH_SPLIT, 0.75f),
            RelativePositionAxes = Axes.X,
            Position = new Vector2(WIDTH_SPLIT, 0),
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

        // 4. Highlight Box (Overlay)
        // We add this to a separate container that doesn't mask, or just on top.
        // It needs to follow the target drawable's screen position.
        Add(highlightBox = new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Color = Color.Red,
            Alpha = 0,
            Blending = BlendingMode.Additive
        });
    }

    private double timeUntilNextRefresh;

    public override void Update()
    {
        base.Update();

        refreshProperties();

        timeUntilNextRefresh -= Clock.ElapsedFrameTime;
        if (timeUntilNextRefresh <= 0)
        {
            refreshTree();
            timeUntilNextRefresh = 500;
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

        currentTimeText.Text = $"{DateTime.Now:dd MMMM yyyy HH:mm:ss tt}";
        runningTimeText.Text = $"Has been running for {TimeSpan.FromSeconds(targetRoot.Clock.CurrentTime / 1000):hh\\:mm\\:ss}";
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

        if (d is DrawVisualiser || d is FpsGraph) return;

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
            return;
        }

        Type type = selectedDrawable.GetType();

        // Rebuild UI layout only if we've selected a different drawable than last time.
        if (lastSelectedDrawable != selectedDrawable)
        {
            propertyFlow.Clear();
            propertyTextMap.Clear();
            lastSelectedDrawable = selectedDrawable;

            // Cache the reflection call so we aren't doing it every frame
            cachedProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // Header (Static, doesn't need to be tracked)
            addPropertyText($"Type: {type.Name}", Color.Yellow);

            // Load State (Tracked manually)
            loadStateText = addPropertyText($"Load State: {selectedDrawable.IsLoaded}", Color.White);

            // Properties
            foreach (var prop in cachedProperties)
            {
                if (prop.GetIndexParameters().Length > 0) continue;

                // Create the UI text once, and save a reference to it in our map
                var textElement = addPropertyText($"{prop.Name}: loading...", Color.White);
                propertyTextMap.Add((prop, textElement));
            }
        }

        // Update state in existing UI elements
        if (loadStateText != null)
        {
            loadStateText.Text = $"Load State: {selectedDrawable.IsLoaded}";
        }

        foreach (var mapping in propertyTextMap)
        {
            try
            {
                object val = mapping.prop.GetValue(selectedDrawable);
                string valStr = val?.ToString() ?? "null";

                Color textColor = Color.White;
                if (val is bool b) textColor = b ? Color.Green : Color.Red;
                else if (val is ValueType) textColor = Color.Cyan;

                string newText = $"{mapping.prop.Name}: {valStr}";
                mapping.textElement.Text = newText;
                mapping.textElement.Color = textColor;
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

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.Key == Key.Down)
        {
            backgroundBox.FadeTo(Math.Clamp(Alpha + 0.05f, 0.1f, 1f), 100);
            return true;
        }
        else if (e.Key == Key.Up)
        {
            backgroundBox.FadeTo(Math.Clamp(Alpha - 0.05f, 0.1f, 1f), 100);
            return true;
        }

        return base.OnKeyDown(e);
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
        label.Color = isSelected ? Color.Yellow : Color.White;
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        ClickAction?.Invoke();
        return true;
    }
}
