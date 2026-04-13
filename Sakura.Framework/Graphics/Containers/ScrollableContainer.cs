// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Containers;

public class ScrollableContainer : Container
{
    /// <summary>
    /// The container that holds the scrolling content.
    /// Children added to the ScrollableContainer are actually added here.
    /// </summary>
    protected Container ScrollContent { get; }

    private readonly ScrollbarContainer verticalScrollbar;
    private readonly ScrollbarContainer horizontalScrollbar;

    private bool verticalScrollbarVisible;
    private bool horizontalScrollbarVisible;

    private ScrollDirection direction = ScrollDirection.Vertical;

    public ScrollDirection Direction
    {
        get => direction;
        set
        {
            if (direction == value) return;
            direction = value;
            updateContentAxes();
        }
    }

    /// <summary>
    /// Whether scrollbars should automatically hide when not in use.
    /// If false, scrollbars will always be visible when content exceeds the viewport.
    /// </summary>
    public bool AutoHideScrollbars { get; set; } = false;

    /// <summary>
    /// Time for scrollbars to fade out after the last scroll activity when <see cref="AutoHideScrollbars"/> is enabled.
    /// </summary>
    public float InactiveDuration { get; set; } = 1500f;

    /// <summary>
    /// Time taken for scrollbars to fade in/out when <see cref="AutoHideScrollbars"/> is enabled.
    /// </summary>
    public float ScrollbarFadeDuration { get; set; } = 250f;

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

    private bool isDraggingContent;
    private double lastActivityTime;

    private double lastDragTime;
    private double averageDragTime;
    private Vector2 averageDragDelta;

    public Vector2 CurrentScroll
    {
        get => currentScroll;
        set
        {
            targetScroll = value;
            currentScroll = value;
            notifyActivity();
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Vector2 ScrollableExtent => getMaxScroll();

    public ScrollableContainer()
    {
        Masking = true;

        ScrollContent = new Container()
        {
            Name = "ScrollContent",
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Width = 1f
        };
        updateContentAxes();

        verticalScrollbar = new ScrollbarContainer()
        {
            Name = "VerticalScrollbar",
            Width = 10,
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Margin = new MarginPadding() { Right = 2 },
            Alpha = 0
        };
        verticalScrollbar.OnDragged = delta => handleScrollbarDrag(delta.Y, true);

        horizontalScrollbar = new ScrollbarContainer()
        {
            Name = "HorizontalScrollbar",
            Height = 10,
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Margin = new MarginPadding() { Bottom = 2 },
            Alpha = 0
        };
        horizontalScrollbar.OnDragged = delta => handleScrollbarDrag(delta.X, false);

        base.Add(ScrollContent);
        base.Add(verticalScrollbar);
        base.Add(horizontalScrollbar);
    }

    private void updateContentAxes()
    {
        switch (direction)
        {
            case ScrollDirection.Vertical:
                ScrollContent.RelativeSizeAxes = Axes.X;
                ScrollContent.AutoSizeAxes = Axes.Y;
                break;
            case ScrollDirection.Horizontal:
                ScrollContent.RelativeSizeAxes = Axes.Y;
                ScrollContent.AutoSizeAxes = Axes.X;
                break;
            case ScrollDirection.Both:
                ScrollContent.RelativeSizeAxes = Axes.None;
                ScrollContent.AutoSizeAxes = Axes.Both;
                break;
        }
    }

    public override void Load()
    {
        base.Load();
        CreateVerticalScrollbar();
        CreateHorizontalScrollbar();
    }

    public override void Clear() => ScrollContent.Clear();

    protected virtual void CreateVerticalScrollbar()
    {
        verticalScrollbar.Child = new Box()
        {
            Name = "VerticalScrollbarBox",
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(1),
            Color = Color.DeepPink
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
            Color = Color.DeepPink
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

    #region Programmatic Scrolling

    private void notifyActivity() => lastActivityTime = Clock.CurrentTime;

    private Vector2 getMaxScroll()
    {
        return new Vector2(
            Direction == ScrollDirection.Vertical ? 0 : Math.Max(0, ScrollContent.DrawSize.X - DrawSize.X),
            Direction == ScrollDirection.Horizontal ? 0 : Math.Max(0, ScrollContent.DrawSize.Y - DrawSize.Y)
        );
    }

    private Vector2 getClampedScroll(Vector2 target)
    {
        Vector2 maxScroll = getMaxScroll();
        return new Vector2(
            Math.Clamp(target.X, 0, maxScroll.X),
            Math.Clamp(target.Y, 0, maxScroll.Y)
        );
    }

    /// <summary>
    /// Scrolls precisely to the given coordinate target.
    /// </summary>
    public void ScrollTo(Vector2 target, bool animated = true)
    {
        notifyActivity();
        targetScroll = getClampedScroll(target);

        if (!animated)
            currentScroll = targetScroll;
    }

    /// <summary>
    /// Scrolls to the very top/left of the container.
    /// </summary>
    public void ScrollToStart(bool animated = true) => ScrollTo(Vector2.Zero, animated);

    /// <summary>
    /// Scrolls to the absolute bottom/right limits of the container.
    /// </summary>
    public void ScrollToEnd(bool animated = true) => ScrollTo(ScrollableExtent, animated);

    /// <summary>
    /// Shifts the scroll target just enough to ensure the specified drawable is fully visible within the viewport.
    /// </summary>
    public void ScrollIntoView(Drawable d, bool animated = true)
    {
        // For accurate offsets, we grab the raw position and size relative to the scroll content bounds.
        Vector2 childPos = d.Position;
        Vector2 childSize = d.DrawSize * d.Scale;

        Vector2 target = targetScroll;
        Vector2 viewport = DrawSize;

        // Vertical Visibility
        if (Direction == ScrollDirection.Vertical || Direction == ScrollDirection.Both)
        {
            if (childPos.Y < target.Y || childSize.Y > viewport.Y)
                target.Y = childPos.Y; // Snap to top of item
            else if (childPos.Y + childSize.Y > target.Y + viewport.Y)
                target.Y = childPos.Y + childSize.Y - viewport.Y; // Snap to bottom of item
        }

        // Horizontal Visibility
        if (Direction == ScrollDirection.Horizontal || Direction == ScrollDirection.Both)
        {
            if (childPos.X < target.X || childSize.X > viewport.X)
                target.X = childPos.X; // Snap to left of item
            else if (childPos.X + childSize.X > target.X + viewport.X)
                target.X = childPos.X + childSize.X - viewport.X; // Snap to right of item
        }

        ScrollTo(target, animated);
    }

    #endregion

    public override void Update()
    {
        updateScrollPosition();
        updateScrollbars();
        base.Update();
    }

    private void updateScrollPosition()
    {
        Vector2 clampedTarget = getClampedScroll(targetScroll);

        if (!isDraggingContent && targetScroll != clampedTarget)
        {
            targetScroll += (clampedTarget - targetScroll) * 0.15f;
        }

        Vector2 dist = targetScroll - currentScroll;

        if (dist.LengthSquared() < 0.1f && targetScroll == clampedTarget)
            currentScroll = targetScroll;
        else
            currentScroll += dist * (1f - MathF.Pow(ScrollDrags, 0.5f));

        ScrollContent.Position = -currentScroll;
    }

    private void updateScrollbars()
    {
        float contentHeight = ScrollContent.DrawSize.Y;
        float viewportHeight = DrawSize.Y;

        bool isRecentlyActive = !AutoHideScrollbars || Clock.CurrentTime - lastActivityTime < InactiveDuration;

        bool needsVertical = contentHeight > viewportHeight && Direction != ScrollDirection.Horizontal;
        bool shouldShowVertical = needsVertical && isRecentlyActive;

        if (shouldShowVertical != verticalScrollbarVisible)
        {
            verticalScrollbarVisible = shouldShowVertical;
            verticalScrollbar.FadeTo(shouldShowVertical ? 1f : 0f, ScrollbarFadeDuration, Easing.OutQuint);
        }

        if (needsVertical)
        {
            float ratio = viewportHeight / contentHeight;
            float barHeight = Math.Max(20, viewportHeight * ratio);
            float maxScroll = contentHeight - viewportHeight;
            float currentRatio = maxScroll > 0 ? currentScroll.Y / maxScroll : 0;
            float availableTrack = viewportHeight - barHeight;

            if (currentScroll.Y < 0)
            {
                float overscroll = -currentScroll.Y;
                verticalScrollbar.Height = Math.Max(5, barHeight - overscroll);
                verticalScrollbar.Position = new Vector2(0, 0);
            }
            else if (currentScroll.Y > maxScroll)
            {
                float overscroll = currentScroll.Y - maxScroll;
                float shrunkHeight = Math.Max(5, barHeight - overscroll);
                verticalScrollbar.Height = shrunkHeight;
                verticalScrollbar.Position = new Vector2(0, viewportHeight - shrunkHeight);
            }
            else
            {
                if (!Precision.AlmostEquals(verticalScrollbar.Height, barHeight))
                    verticalScrollbar.Height = barHeight;
                float newY = availableTrack * currentRatio;
                if (!Precision.AlmostEquals(verticalScrollbar.Position.Y, newY))
                    verticalScrollbar.Position = new Vector2(0, newY);
            }
        }

        float contentWidth = ScrollContent.DrawSize.X;
        float viewportWidth = DrawSize.X;

        bool needsHorizontal = contentWidth > viewportWidth && Direction != ScrollDirection.Vertical;
        bool shouldShowHorizontal = needsHorizontal && isRecentlyActive;

        if (shouldShowHorizontal != horizontalScrollbarVisible)
        {
            horizontalScrollbarVisible = shouldShowHorizontal;
            horizontalScrollbar.FadeTo(shouldShowHorizontal ? 1f : 0f, ScrollbarFadeDuration, Easing.OutQuint);
        }

        if (needsHorizontal)
        {
            float ratio = viewportWidth / contentWidth;
            float barWidth = Math.Max(20, viewportWidth * ratio);
            float maxScroll = contentWidth - viewportWidth;
            float currentRatio = maxScroll > 0 ? currentScroll.X / maxScroll : 0;
            float availableTrack = viewportWidth - barWidth;

            if (currentScroll.X < 0)
            {
                float overscroll = -currentScroll.X;
                horizontalScrollbar.Width = Math.Max(5, barWidth - overscroll);
                horizontalScrollbar.Position = new Vector2(0, 0);
            }
            else if (currentScroll.X > maxScroll)
            {
                float overscroll = currentScroll.X - maxScroll;
                float shrunkWidth = Math.Max(5, barWidth - overscroll);
                horizontalScrollbar.Width = shrunkWidth;
                horizontalScrollbar.Position = new Vector2(viewportWidth - shrunkWidth, 0);
            }
            else
            {
                horizontalScrollbar.Width = barWidth;
                horizontalScrollbar.Position = new Vector2(availableTrack * currentRatio, 0);
            }
        }
    }

    private void handleScrollbarDrag(float delta, bool isVertical)
    {
        notifyActivity();

        if (isVertical)
        {
            float contentHeight = ScrollContent.DrawSize.Y;
            float viewportHeight = DrawSize.Y;
            if (contentHeight <= viewportHeight)
                return;

            float barHeight = Math.Max(20, viewportHeight * (viewportHeight / contentHeight));
            float availableTrack = viewportHeight - barHeight;
            if (availableTrack <= 0)
                return;

            float maxScroll = contentHeight - viewportHeight;
            targetScroll.Y += delta * (maxScroll / availableTrack);

            targetScroll = getClampedScroll(targetScroll);
            currentScroll.Y = targetScroll.Y;
        }
        else
        {
            float contentWidth = ScrollContent.DrawSize.X;
            float viewportWidth = DrawSize.X;
            if (contentWidth <= viewportWidth)
                return;

            float barWidth = Math.Max(20, viewportWidth * (viewportWidth / contentWidth));
            float availableTrack = viewportWidth - barWidth;
            if (availableTrack <= 0)
                return;

            float maxScroll = contentWidth - viewportWidth;
            targetScroll.X += delta * (maxScroll / availableTrack);

            targetScroll = getClampedScroll(targetScroll);
            currentScroll.X = targetScroll.X;
        }
    }

    public override bool OnScroll(ScrollEvent e)
    {
        notifyActivity();
        Vector2 delta = Vector2.Zero;

        if (Direction == ScrollDirection.Vertical)
        {
            delta.Y -= e.ScrollDelta.Y * ScrollDistance;
        }
        else if (Direction == ScrollDirection.Horizontal)
        {
            delta.X -= e.ScrollDelta.Y * ScrollDistance;
        }
        else
        {
            delta.Y -= e.ScrollDelta.Y * ScrollDistance;
            delta.X -= e.ScrollDelta.X * ScrollDistance;
        }

        targetScroll += delta;
        return true;
    }

    public override bool OnDragStart(MouseButtonEvent e)
    {
        notifyActivity();
        isDraggingContent = true;

        lastDragTime = Clock.CurrentTime;
        averageDragTime = 0;
        averageDragDelta = Vector2.Zero;

        return true;
    }

    public override bool OnDragEnd(MouseButtonEvent e)
    {
        notifyActivity();
        isDraggingContent = false;

        if (averageDragTime > 0.0)
        {
            Vector2 velocity = averageDragDelta / (float)averageDragTime;

            if (velocity.LengthSquared() > 0.01f)
            {
                float distanceDecayDrag = 0.0035f;
                Vector2 distance = velocity / (1f - MathF.Exp(-distanceDecayDrag));

                if (Direction == ScrollDirection.Vertical || Direction == ScrollDirection.Both)
                    targetScroll.Y -= distance.Y;

                if (Direction == ScrollDirection.Horizontal || Direction == ScrollDirection.Both)
                    targetScroll.X -= distance.X;
            }
        }

        return true;
    }

    public override bool OnDrag(MouseEvent e)
    {
        notifyActivity();

        double currentTime = Clock.CurrentTime;
        double timeDelta = currentTime - lastDragTime;
        double decay = Math.Pow(0.95, timeDelta);

        averageDragTime = averageDragTime * decay + timeDelta;
        averageDragDelta = averageDragDelta * (float)decay + e.Delta;
        lastDragTime = currentTime;

        Vector2 clamped = getClampedScroll(targetScroll);
        float resistanceX = (targetScroll.X < 0 || targetScroll.X > clamped.X) ? 0.3f : 1f;
        float resistanceY = (targetScroll.Y < 0 || targetScroll.Y > clamped.Y) ? 0.3f : 1f;

        if (Direction == ScrollDirection.Vertical || Direction == ScrollDirection.Both)
            targetScroll.Y -= e.Delta.Y * resistanceY;

        if (Direction == ScrollDirection.Horizontal || Direction == ScrollDirection.Both)
            targetScroll.X -= e.Delta.X * resistanceX;

        return true;
    }

    private class ScrollbarContainer : Container
    {
        public Action<Vector2>? OnDragged;

        public override bool OnDragStart(MouseButtonEvent e) => true;

        public override bool OnDrag(MouseEvent e)
        {
            OnDragged?.Invoke(e.Delta);
            return true;
        }
    }
}

public enum ScrollDirection
{
    Horizontal,
    Vertical,
    Both
}
