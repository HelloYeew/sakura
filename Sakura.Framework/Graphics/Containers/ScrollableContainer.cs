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

public partial class ScrollableContainer : Container
{
    protected override Container Content => ScrollContent;

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
    /// Distance to scroll per mouse wheel tick, in this container's units. A trackpad that says how far its fingers
    /// moved (<see cref="ScrollEvent.AbsoluteDelta"/>) scrolls that far instead.
    /// </summary>
    public float ScrollDistance { get; set; } = 100f;

    /// <summary>
    /// How fast the scroll chases the target (smoothness). Lower is smoother.
    /// </summary>
    public float ScrollDrags { get; set; } = 0.05f;

    private Vector2 targetScroll;
    private Vector2 currentScroll;

    /// <summary>
    /// The offset the last <see cref="ScrollTo"/> asked for, unclamped, while it is still being re-applied after layout.
    /// </summary>
    private Vector2? requestedScroll;

    /// <summary>
    /// The drawable the last <see cref="ScrollIntoView"/> asked to show, while it is still being re-applied after layout.
    /// </summary>
    private Drawable? requestedIntoView;

    /// <summary>
    /// Whether the pending request glides there rather than jumping.
    /// </summary>
    private bool requestAnimated;

    /// <summary>
    /// The scrollable extent the pending request was last applied against, or null until it has been applied after a
    /// layout.
    /// </summary>
    private Vector2? requestExtent;

    private bool isDraggingContent;
    private double lastActivityTime;

    private double lastDragTime;
    private double lastDragTimestamp;
    private double averageDragTime;
    private Vector2 averageDragDelta;

    public Vector2 CurrentScroll
    {
        get => currentScroll;
        set
        {
            cancelRequest();
            targetScroll = value;
            currentScroll = value;
            notifyActivity();
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Vector2 ScrollableExtent => getMaxScroll();

    /// <summary>
    /// Whether a drag in <paramref name="direction"/> (screen space) would scroll this container: its main axis
    /// is one it scrolls along, and it has content past the edge on that axis.
    /// </summary>
    internal bool ScrollsAlong(Vector2 direction)
    {
        bool horizontal = MathF.Abs(direction.X) > MathF.Abs(direction.Y);
        var extent = ScrollableExtent;

        return horizontal
            ? Direction != ScrollDirection.Vertical && extent.X > 0
            : Direction != ScrollDirection.Horizontal && extent.Y > 0;
    }

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
        verticalScrollbar.OnDragged = e => handleScrollbarDrag(toLocalDelta(e.ScreenSpaceMousePosition, e.Delta).Y, true);

        horizontalScrollbar = new ScrollbarContainer()
        {
            Name = "HorizontalScrollbar",
            Height = 10,
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Margin = new MarginPadding() { Bottom = 2 },
            Alpha = 0
        };
        horizontalScrollbar.OnDragged = e => handleScrollbarDrag(toLocalDelta(e.ScreenSpaceMousePosition, e.Delta).X, false);

        AddInternal(ScrollContent);
        AddInternal(verticalScrollbar);
        AddInternal(horizontalScrollbar);
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
        verticalScrollbar.Child = CreateVerticalScrollbar();
        horizontalScrollbar.Child = CreateHorizontalScrollbar();
    }

    public override void Clear(bool dispose = true) => ScrollContent.Clear(dispose);

    protected virtual Drawable CreateVerticalScrollbar() => new Box()
    {
        Name = "VerticalScrollbarBox",
        RelativeSizeAxes = Axes.Both,
        Anchor = Anchor.TopLeft,
        Origin = Anchor.TopLeft,
        Size = new Vector2(1),
        Color = Color.DeepPink
    };

    protected virtual Drawable CreateHorizontalScrollbar() => new Box()
    {
        Name = "HorizontalScrollbarBox",
        RelativeSizeAxes = Axes.Both,
        Anchor = Anchor.TopLeft,
        Origin = Anchor.TopLeft,
        Size = new Vector2(1),
        Color = Color.DeepPink
    };

    public override void Add(Drawable drawable)
    {
        if (drawable == ScrollContent || drawable == verticalScrollbar || drawable == horizontalScrollbar)
            base.Add(drawable);
        else
            ScrollContent.Add(drawable);
    }

    public override void Remove(Drawable drawable, bool dispose = true)
    {
        if (drawable == ScrollContent || drawable == verticalScrollbar || drawable == horizontalScrollbar)
            base.Remove(drawable, dispose);
        else
            ScrollContent.Remove(drawable, dispose);
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
    /// Scrolls precisely to the given coordinate target, clamped to the content.
    /// </summary>
    /// <remarks>
    /// The clamp waits for layout. The target applies at once, and is then applied again after each layout until the
    /// content's size stops changing, so scrolling straight after replacing the content lands where it was asked to
    /// rather than against the size the content had before. Scrolling by hand, or setting <see cref="CurrentScroll"/>,
    /// ends that early.
    /// </remarks>
    public void ScrollTo(Vector2 target, bool animated = true)
    {
        beginRequest(animated);
        requestedScroll = target;
        applyScrollTarget(getClampedScroll(target), animated);
    }

    /// <summary>
    /// Scrolls to the very top/left of the container.
    /// </summary>
    public void ScrollToStart(bool animated = true) => ScrollTo(Vector2.Zero, animated);

    /// <summary>
    /// Scrolls to the absolute bottom/right limits of the container, as far as the content reaches once it is laid out
    /// (see <see cref="ScrollTo"/>).
    /// </summary>
    public void ScrollToEnd(bool animated = true) => ScrollTo(new Vector2(float.PositiveInfinity), animated);

    /// <summary>
    /// Shifts the scroll target just enough to ensure the specified drawable is fully visible within the viewport.
    /// Like <see cref="ScrollTo"/>, this is applied again after each layout until the content's size stops changing, so
    /// a drawable that was only just added still ends up in view.
    /// </summary>
    public void ScrollIntoView(Drawable d, bool animated = true)
    {
        if (!tryGetIntoViewTarget(d, out Vector2 target))
            return;

        beginRequest(animated);
        requestedIntoView = d;
        applyScrollTarget(getClampedScroll(target), animated);
    }

    /// <summary>
    /// Starts a new scroll request, replacing any still pending.
    /// </summary>
    private void beginRequest(bool animated)
    {
        notifyActivity();
        cancelRequest();
        requestAnimated = animated;
    }

    /// <summary>
    /// Drops the pending scroll request, if any.
    /// </summary>
    private void cancelRequest()
    {
        requestedScroll = null;
        requestedIntoView = null;
        requestExtent = null;
    }

    /// <summary>
    /// Heads for <paramref name="target"/>, or jumps straight there when not <paramref name="animated"/>.
    /// </summary>
    private void applyScrollTarget(Vector2 target, bool animated)
    {
        targetScroll = target;

        if (!animated)
            currentScroll = targetScroll;
    }

    /// <summary>
    /// The unclamped scroll offset that shows all of <paramref name="d"/>, moving the current target as little as
    /// possible. False when <paramref name="d"/> is not inside this container's content.
    /// </summary>
    private bool tryGetIntoViewTarget(Drawable d, out Vector2 target)
    {
        target = targetScroll;

        if (!tryGetBoundsWithinContent(d, out Vector2 childPos, out Vector2 childSize))
            return false;

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

        return true;
    }

    /// <summary>
    /// Walks up from <paramref name="d"/> to <see cref="ScrollContent"/>, placing it in each parent as
    /// <see cref="Drawable.UpdateTransforms"/> does, to get where its top-left corner is within the scrolled content
    /// and how large it is there. Returns false when <paramref name="d"/> is not beneath this container at all, in
    /// which case there is nothing sensible to scroll to.
    /// </summary>
    /// <remarks>
    /// Worked out from the layout rather than from the drawn matrices, which a drawable scrolled out of view (and so
    /// not updated) or one added this frame doesn't have yet. Rotation and shear are left out.
    /// </remarks>
    private bool tryGetBoundsWithinContent(Drawable d, out Vector2 topLeft, out Vector2 size)
    {
        // The drawable's corners in its own units, carried up one parent at a time.
        Vector2 start = Vector2.Zero;
        Vector2 end = d.DrawSize;

        for (var current = d; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, ScrollContent))
            {
                topLeft = start;
                size = end - start;
                return true;
            }

            if (current.Parent == null)
                break;

            Vector2 corner = topLeftInParent(current, current.Parent);
            start = corner + start * current.Scale;
            end = corner + end * current.Scale;
        }

        topLeft = Vector2.Zero;
        size = Vector2.Zero;
        return false;
    }

    /// <summary>
    /// Where <paramref name="d"/>'s top-left corner sits in <paramref name="parent"/>'s box, as
    /// <see cref="Drawable.UpdateTransforms"/> places it: its position (relative axes resolved), anchor and margin,
    /// less its origin, plus the parent's padding.
    /// </summary>
    private static Vector2 topLeftInParent(Drawable d, Container parent)
    {
        Vector2 position = d.Position;
        Vector2 relativeToAbsolute = parent.RelativeToAbsoluteFactor;

        if ((d.RelativePositionAxes & Axes.X) != 0)
            position.X = (position.X - parent.RelativeChildOffset.X) * relativeToAbsolute.X;
        if ((d.RelativePositionAxes & Axes.Y) != 0)
            position.Y = (position.Y - parent.RelativeChildOffset.Y) * relativeToAbsolute.Y;

        Vector2 anchor = Drawable.GetAnchorOriginVector(d.Anchor);
        Vector2 room = parent.ChildSize - d.Margin.Total;
        Vector2 drawn = d.DrawSize * d.Scale;

        return new Vector2(
            parent.Padding.Left + position.X + anchor.X * room.X + d.Margin.Left - d.OriginVector.X * drawn.X,
            parent.Padding.Top + position.Y + anchor.Y * room.Y + d.Margin.Top - d.OriginVector.Y * drawn.Y);
    }

    #endregion

    public override void Update()
    {
        updateScrollPosition();
        updateScrollbars();
        base.Update();
    }

    /// <summary>
    /// Applies a pending <see cref="ScrollTo"/> or <see cref="ScrollIntoView"/> again now that this frame's layout is
    /// done, so it is clamped against the content's current size.
    /// </summary>
    protected override void UpdateLayoutAfterChildren()
    {
        base.UpdateLayoutAfterChildren();

        if (requestedScroll == null && requestedIntoView == null)
            return;

        Vector2 target;

        if (requestedScroll is Vector2 offset)
            target = offset;
        else if (!tryGetIntoViewTarget(requestedIntoView!, out target))
        {
            cancelRequest();
            return;
        }

        Vector2 extent = getMaxScroll();
        Vector2 scrollBefore = currentScroll;

        applyScrollTarget(getClampedScroll(target), requestAnimated);

        if (currentScroll != scrollBefore)
        {
            ScrollContent.Position = -currentScroll;
            refreshHoverAtCursor();
        }

        // Kept until the extent comes out the same two frames running: content that takes more than a frame to reach its
        // size still ends up where it was asked to, and content that changes later on leaves the view alone.
        if (requestExtent == extent)
            cancelRequest();
        else
            requestExtent = extent;
    }

    private void updateScrollPosition()
    {
        Vector2 clampedTarget = getClampedScroll(targetScroll);

        float elapsedSeconds = (float)Clock.ElapsedFrameTime / 1000f;

        // Kinetic momentum scrolling
        float scrollBlend = 1f - MathF.Exp(-(1f / ScrollDrags) * elapsedSeconds);

        // Bounce factor for over-scrolling
        float bounceBlend = 1f - MathF.Exp(-10f * elapsedSeconds);

        // Rubber-band bounce back when scrolled past the absolute edge
        if (!isDraggingContent && targetScroll != clampedTarget)
        {
            targetScroll += (clampedTarget - targetScroll) * bounceBlend;
        }

        Vector2 dist = targetScroll - currentScroll;

        Vector2 scrollBefore = currentScroll;

        // Snap to target if very close to prevent infinite micro-stutters
        if (dist.LengthSquared() < 0.1f && targetScroll == clampedTarget)
        {
            currentScroll = targetScroll;
        }
        else
        {
            // smooth momentum glide
            currentScroll += dist * scrollBlend;
        }

        ScrollContent.Position = -currentScroll;

        if (currentScroll != scrollBefore)
            refreshHoverAtCursor();
    }

    /// <summary>
    /// Re-evaluates hover after the content scrolls under a stationary cursor, so the drawable now
    /// physically under the cursor becomes hovered (and the previous one is un-hovered). The hover
    /// bookkeeping is owned by the <see cref="InputManager"/>, so we ask it to refresh. Does nothing
    /// until attached under an <see cref="IInputManagerProvider"/>.
    /// </summary>
    private void refreshHoverAtCursor() => GetContainingInputManager()?.RefreshHover();

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
        cancelRequest();

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
        cancelRequest();
        Vector2 delta = Vector2.Zero;

        // A trackpad that says how far its fingers moved moves the content as far, on screen, as a drag would; a wheel
        // moves it a set distance a notch.
        Vector2 amount = e.AbsoluteDelta is Vector2 absolute
            ? toLocalDelta(e.ScreenSpaceMousePosition, absolute)
            : e.ScrollDelta * ScrollDistance;

        if (Direction == ScrollDirection.Vertical)
            delta.Y -= amount.Y;
        else if (Direction == ScrollDirection.Horizontal)
            delta.X -= amount.X;
        else
        {
            delta.Y -= amount.Y;
            delta.X -= amount.X;
        }

        // If the user scrolls in the opposite direction of the momentum, brake immediately
        if (Direction == ScrollDirection.Vertical && Math.Sign(delta.Y) != Math.Sign(targetScroll.Y - currentScroll.Y))
            targetScroll.Y = currentScroll.Y;

        if (Direction == ScrollDirection.Horizontal && Math.Sign(delta.X) != Math.Sign(targetScroll.X - currentScroll.X))
            targetScroll.X = currentScroll.X;

        targetScroll += delta;

        // Under the fingers at once, rather than gliding there as from a wheel's notch.
        if (e.AbsoluteDelta != null)
            currentScroll += delta;

        // Prevent infinite tension when aggressively scrolling against a wall
        Vector2 maxScroll = getMaxScroll();
        float tensionLimit = 100f; // The maximum pixels the wheel can stretch past the edge

        targetScroll.X = Math.Clamp(targetScroll.X, -tensionLimit, maxScroll.X + tensionLimit);
        targetScroll.Y = Math.Clamp(targetScroll.Y, -tensionLimit, maxScroll.Y + tensionLimit);

        return true;
    }

    public override bool OnDragStart(MouseButtonEvent e)
    {
        notifyActivity();
        cancelRequest();
        isDraggingContent = true;

        lastDragTime = Clock.CurrentTime;
        lastDragTimestamp = e.Timestamp;
        averageDragTime = 0;
        averageDragDelta = Vector2.Zero;

        return true;
    }

    public override bool OnDragEnd(MouseButtonEvent e)
    {
        notifyActivity();
        isDraggingContent = false;

        // Time held still before letting go slows the throw, down to none after a pause.
        trackDrag(e.Timestamp, Vector2.Zero);

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

        Vector2 drag = toLocalDelta(e.ScreenSpaceMousePosition, e.Delta);
        trackDrag(e.Timestamp, drag);

        Vector2 clamped = getClampedScroll(targetScroll);
        float resistanceX = (targetScroll.X < 0 || targetScroll.X > clamped.X) ? 0.3f : 1f;
        float resistanceY = (targetScroll.Y < 0 || targetScroll.Y > clamped.Y) ? 0.3f : 1f;

        if (Direction == ScrollDirection.Vertical || Direction == ScrollDirection.Both)
            targetScroll.Y -= drag.Y * resistanceY;

        if (Direction == ScrollDirection.Horizontal || Direction == ScrollDirection.Both)
            targetScroll.X -= drag.X * resistanceX;

        return true;
    }

    /// <summary>
    /// <paramref name="screenDelta"/>, a movement on screen ending at <paramref name="screenPosition"/>, in this
    /// container's own units: how far the content moves to stay under the finger, however big the container is drawn.
    /// Taken straight, a phone's scaled-down layout would move the content a third of the finger's distance.
    /// </summary>
    private Vector2 toLocalDelta(Vector2 screenPosition, Vector2 screenDelta) =>
        ToLocalSpace(screenPosition) - ToLocalSpace(screenPosition - screenDelta);

    /// <summary>
    /// Folds a drag event into the running average a fling is thrown with: how far it moved, and how long since the last
    /// one. Timed by when the events happened where they say, since several can arrive in one frame, which the clock
    /// would read as no time at all; by the clock where they don't.
    /// </summary>
    private void trackDrag(double timestamp, Vector2 delta)
    {
        double clockTime = Clock.CurrentTime;
        double timeDelta = double.IsNaN(timestamp) || double.IsNaN(lastDragTimestamp)
            ? clockTime - lastDragTime
            : timestamp - lastDragTimestamp;
        double decay = Math.Pow(0.95, Math.Max(0, timeDelta));

        averageDragTime = averageDragTime * decay + Math.Max(0, timeDelta);
        averageDragDelta = averageDragDelta * (float)decay + delta;

        lastDragTime = clockTime;
        lastDragTimestamp = timestamp;
    }

    private partial class ScrollbarContainer : Container
    {
        public Action<MouseEvent>? OnDragged;

        public override bool OnClick(MouseButtonEvent e) => true;

        public override bool OnDragStart(MouseButtonEvent e) => true;

        public override bool OnDrag(MouseEvent e)
        {
            OnDragged?.Invoke(e);
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
