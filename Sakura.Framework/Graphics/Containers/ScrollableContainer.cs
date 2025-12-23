// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Containers;

public class ScrollableContainer : Container
{
    /// <summary>
    /// The container that holds the scrolling content.
    /// Children added to the ScrollableContainer are actually added here.
    /// </summary>
    protected Container ScrollContent { get; }

    private readonly Container verticalScrollbar;
    private readonly Container horizontalScrollbar;

    public ScrollDirection Direction { get; set; } = ScrollDirection.Vertical;

    /// <summary>
    /// Distance to scroll per mouse wheel tick.
    /// </summary>
    public float ScrollDistance { get; set; } = 100f;

    /// <summary>
    /// How fast the scroll chases the target (smoothness). Lower is smoother.
    /// </summary>
    public float ScrollDrags { get; set; } = 0.05f;

    private Vector2 targetScroll;
    private Vector2 currentScroll;

    public Vector2 CurrentScroll
    {
        get => currentScroll;
        set
        {
            targetScroll = value;
            currentScroll = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public ScrollableContainer()
    {
        Masking = true;

        ScrollContent = new Container()
        {
            Name = $"ScrollContent",
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Width = 1f,
        };

        verticalScrollbar = new Container()
        {
            Name = $"VerticalScrollbar",
            Width = 20,
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Margin = new MarginPadding()
            {
                Right = 2
            },
            Alpha = 0
        };

        horizontalScrollbar = new Container()
        {
            Name = $"HorizontalScrollbar",
            Height = 20,
            // RelativeSizeAxes = Axes.X,
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Margin = new MarginPadding()
            {
                Bottom = 2
            },
            Alpha = 0
        };

        base.Add(new Box()
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Color = Color.Gray,
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre
        });

        base.Add(ScrollContent);
        base.Add(verticalScrollbar);
        base.Add(horizontalScrollbar);
    }

    public override void Load()
    {
        base.Load();
        CreateVerticalScrollbar();
        CreateHorizontalScrollbar();
    }

    protected virtual void CreateVerticalScrollbar()
    {
        verticalScrollbar.Child = new Box()
        {
            Name = "VerticalScrollbarBox",
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(1),
            Color = Color.Purple
        };
    }

    protected virtual void CreateHorizontalScrollbar()
    {
        horizontalScrollbar.Child = new Box()
        {
            Name = "HorizontalScrollbarBox",
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(1),
            Color = Color.Purple
        };
    }

    public override void Add(Drawable drawable)
    {
        if (drawable == ScrollContent || drawable == verticalScrollbar || drawable == horizontalScrollbar)
            base.Add(drawable);
        else
            ScrollContent.Add(drawable);
    }

    public override void Remove(Drawable drawable)
    {
        if (drawable == ScrollContent || drawable == verticalScrollbar || drawable == horizontalScrollbar)
            base.Remove(drawable);
        else
            ScrollContent.Remove(drawable);
    }

    public override void Update()
    {
        base.Update();
        updateScrollPosition();
        updateScrollbars();
    }

    private void updateScrollPosition()
    {
        // Calculate the maximum we can scroll
        // Scrollable range = Content Size - Viewport Size
        // We usually only scroll if Content > Viewport
        Vector2 maxScroll = Vector2.Zero;
        maxScroll.X = Math.Max(0, ScrollContent.DrawSize.X - DrawSize.X);
        maxScroll.Y = Math.Max(0, ScrollContent.DrawSize.Y - DrawSize.Y);

        // Clamp target to bounds
        targetScroll.X = Math.Clamp(targetScroll.X, 0, maxScroll.X);
        targetScroll.Y = Math.Clamp(targetScroll.Y, 0, maxScroll.Y);

        Vector2 dist = targetScroll - currentScroll;

        if (dist.LengthSquared() < 0.1f)
        {
            currentScroll = targetScroll;
        }
        else
        {
            currentScroll += dist * (1f - MathF.Pow(ScrollDrags, 0.5f));
        }

        ScrollContent.Position = -currentScroll;
    }

    private void updateScrollbars()
    {
        float contentHeight = ScrollContent.DrawSize.Y;
        float viewportHeight = DrawSize.Y;

        if (contentHeight > viewportHeight && Direction != ScrollDirection.Horizontal)
        {
            verticalScrollbar.Alpha = 1;

            float ratio = viewportHeight / contentHeight;
            float barHeight = Math.Max(20, viewportHeight * ratio);
            verticalScrollbar.Height = barHeight;

            float maxScroll = contentHeight - viewportHeight;
            float currentRatio = currentScroll.Y / maxScroll;

            float availableTrack = viewportHeight - barHeight;

            verticalScrollbar.Position = new Vector2(0, availableTrack * currentRatio);
        }
        else
        {
            verticalScrollbar.Alpha = 0;
        }

        float contentWidth = ScrollContent.DrawSize.X;
        float viewportWidth = DrawSize.X;

        if (contentWidth > viewportWidth && Direction != ScrollDirection.Vertical)
        {
            horizontalScrollbar.Alpha = 1;

            float ratio = viewportWidth / contentWidth;
            float barWidth = Math.Max(20, viewportWidth * ratio);
            horizontalScrollbar.Width = barWidth;

            float maxScroll = contentWidth - viewportWidth;
            float currentRatio = currentScroll.X / maxScroll;
            float availableTrack = viewportWidth - barWidth;

            horizontalScrollbar.Position = new Vector2(availableTrack * currentRatio, 0);
        }
        else
        {
            horizontalScrollbar.Alpha = 0;
        }
    }

    public override bool OnScroll(ScrollEvent e)
    {
        if (Direction == ScrollDirection.Vertical)
        {
            targetScroll.Y -= e.ScrollDelta.Y * ScrollDistance;
            return true;
        }

        if (Direction == ScrollDirection.Horizontal)
        {
            targetScroll.X -= e.ScrollDelta.Y * ScrollDistance; // Usually wheel maps to X for horz-only
            return true;
        }

        // Both
        // Standard: Shift+Scroll or separate wheels could handle X
        // For now, map primary scroll to Y
        targetScroll.Y -= e.ScrollDelta.Y * ScrollDistance;
        targetScroll.X -= e.ScrollDelta.X * ScrollDistance;
        return true;
    }

    public override bool OnDragStart(MouseButtonEvent e)
    {
        targetScroll = currentScroll;
        return true;
    }

    public override bool OnDrag(MouseEvent e)
    {
        targetScroll -= e.Delta;
        currentScroll = targetScroll;
        return true;
    }
}

public enum ScrollDirection
{
    Horizontal,
    Vertical,
    Both
}
