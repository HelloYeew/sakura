// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Drawables;

public partial class Container : Drawable
{
    /// <summary>
    /// A version number that is incremented whenever the topology of the container changes
    /// (i.e., when children are added or removed).
    /// </summary>
    internal long TopologyVersion { get; private set; } = 1;

    private readonly List<Drawable> children = new List<Drawable>();

    private readonly List<Drawable> sortedChildren = new List<Drawable>();
    private long sortedChildrenVersion = -1;
    private long childInsertionCounter;

    private static readonly Comparison<Drawable> compare_depth = static (a, b) =>
    {
        int result = a.Depth.CompareTo(b.Depth);
        return result != 0 ? result : a.ChildInsertionOrder.CompareTo(b.ChildInsertionOrder);
    };

    /// <summary>
    /// The children of this container sorted by depth (back-to-front draw order).
    /// The list is cached and only re-sorted when children are added/removed or depths change.
    /// </summary>
    internal IReadOnlyList<Drawable> SortedChildren => getSortedChildren();

    private List<Drawable> getSortedChildren()
    {
        if (sortedChildrenVersion != TopologyVersion)
        {
            sortedChildren.Clear();
            sortedChildren.AddRange(children);
            sortedChildren.Sort(compare_depth);
            sortedChildrenVersion = TopologyVersion;
        }

        return sortedChildren;
    }

    /// <summary>
    /// The container that actually holds the children. By default, it is this container.
    /// Derived classes (like <see cref="ScrollableContainer"/>) can override this to route children to an internal container.
    /// </summary>
    protected virtual Container Content => this;

    /// <summary>
    /// If true, children will be clipped to the bounds of this container.
    /// </summary>
    public bool Masking { get; set; }

    /// <summary>
    /// The radius of the corners when masking.
    /// </summary>
    public float CornerRadius { get; set; }

    private float borderThickness;

    /// <summary>
    /// The thickness of the border when masking.
    /// </summary>
    public float BorderThickness
    {
        get => borderThickness;
        set
        {
            if (borderThickness == value) return;
            borderThickness = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    private Color borderColor = Color.White;

    /// <summary>
    /// The color of the border when masking.
    /// </summary>
    public Color BorderColor
    {
        get => borderColor;
        set
        {
            if (borderColor == value) return;
            borderColor = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    private EdgeEffectParameters edgeEffect;

    /// <summary>
    /// A soft glow or drop shadow rendered around this container's rounded-rectangle shape.
    /// Unlike <see cref="Masking"/>, the edge effect renders regardless of whether masking is enabled,
    /// using the container's draw rectangle, <see cref="CornerRadius"/> and the effect's own parameters.
    /// </summary>
    public EdgeEffectParameters EdgeEffect
    {
        get => edgeEffect;
        set
        {
            if (edgeEffect == value) return;
            edgeEffect = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// Control which axes that this container automatically sized based on its children's sizes.
    /// </summary>
    public Axes AutoSizeAxes { get; set; } = Axes.None;

    /// <summary>
    /// The duration (in milliseconds) over which the container animates toward a new auto-size target.
    /// When 0 (the default), the container snaps to its computed size instantly.
    /// Only meaningful when <see cref="AutoSizeAxes"/> is not <see cref="Axes.None"/>.
    /// </summary>
    public double AutoSizeDuration { get; set; }

    /// <summary>
    /// The easing function used when animating toward a new auto-size target.
    /// Only meaningful when <see cref="AutoSizeDuration"/> is greater than 0.
    /// </summary>
    public Easing AutoSizeEasing { get; set; } = Easing.None;

    /// <summary>
    /// The in-flight auto-size animation, if any. Re-targeted (rather than recreated) when the
    /// computed auto-size changes mid-animation so children changing rapidly produce a smooth glide.
    /// </summary>
    private AutoSizeTransform? autoSizeTransform;

    /// <summary>
    /// The auto-size target the in-flight <see cref="autoSizeTransform"/> is currently heading toward.
    /// Used to avoid re-targeting the animation when the computed size has not actually changed.
    /// </summary>
    private Vector2 autoSizeTarget;

    /// <summary>
    /// Assigns the container's size directly, bypassing the auto-size animation path.
    /// Invoked by <see cref="AutoSizeTransform"/> as the animation progresses.
    /// </summary>
    internal void ApplyAutoSize(Vector2 newSize)
    {
        if (Size != newSize)
            Size = newSize;
    }

    public IReadOnlyList<Drawable> Children
    {
        get => Content == this ? children : Content.Children;
        set
        {
            Clear();

            int count = value.Count;
            for (int i = 0; i < count; i++)
                Add(value[i]);
        }
    }

    public Drawable Child
    {
        set => Children = new[] { value };
    }

    public Vector2 ChildSize
    {
        get
        {
            var containerSize = DrawSize;
            return new Vector2(containerSize.X - Padding.Total.X, containerSize.Y - Padding.Total.Y);
        }
    }

    private Vector2 relativeChildSize = Vector2.One;

    /// <summary>
    /// The size of the relative position/size coordinate space of children of this <see cref="Container"/>.
    /// Children positioned at this size will appear as if they were positioned at <see cref="Drawable.Position"/> = <see cref="Vector2.One"/> in this <see cref="Container"/>.
    /// </summary>
    public Vector2 RelativeChildSize
    {
        get => relativeChildSize;
        set
        {
            if (relativeChildSize == value) return;

            if (value.X == 0 || value.Y == 0)
                throw new ArgumentException($"{nameof(RelativeChildSize)} must be non-zero on both axes.", nameof(value));

            relativeChildSize = value;

            // The relative coordinate space changed, so every child's resolved geometry is stale.
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    private Vector2 relativeChildOffset = Vector2.Zero;

    /// <summary>
    /// The offset of the relative position/size coordinate space of children of this <see cref="Container"/>.
    /// Children positioned at this offset will appear as if they were positioned at <see cref="Drawable.Position"/> = <see cref="Vector2.Zero"/> in this <see cref="Container"/>.
    /// </summary>
    public Vector2 RelativeChildOffset
    {
        get => relativeChildOffset;
        set
        {
            if (relativeChildOffset == value) return;

            relativeChildOffset = value;

            // The relative coordinate space changed, so every child's resolved geometry is stale.
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// Conversion multiplier that maps a child's relative coordinate (0..1 across <see cref="RelativeChildSize"/>)
    /// into the container's pixel content space. Equal to <see cref="ChildSize"/> divided by <see cref="RelativeChildSize"/>.
    /// </summary>
    internal Vector2 RelativeToAbsoluteFactor
    {
        get
        {
            var childSize = ChildSize;
            return new Vector2(childSize.X / relativeChildSize.X, childSize.Y / relativeChildSize.Y);
        }
    }

    public Container()
    {
        Texture = null;
    }

    public virtual void Add(Drawable drawable)
    {
        if (Content != this)
        {
            Content.Add(drawable);
            return;
        }

        AddInternal(drawable);
    }

    /// <summary>
    /// Adds a collection of drawables to this container.
    /// </summary>
    public void AddRange(IEnumerable<Drawable> drawables)
    {
        foreach (var drawable in drawables)
            Add(drawable);
    }

    public virtual void Remove(Drawable drawable)
    {
        if (Content != this)
        {
            Content.Remove(drawable);
            return;
        }

        RemoveInternal(drawable);
    }

    public virtual void Clear()
    {
        if (Content != this)
        {
            Content.Clear();
            return;
        }

        ClearInternal();
    }

    protected void AddInternal(Drawable drawable)
    {
        ArgumentNullException.ThrowIfNull(drawable);

        if (drawable == this)
            throw new InvalidOperationException("A container cannot be added to itself.");

        if (drawable.Parent != null)
        {
            if (drawable.Parent == this)
                throw new InvalidOperationException($"The drawable {drawable.GetDisplayName()} is already a child of this container.");

            throw new InvalidOperationException($"May not add a drawable to multiple containers. {drawable.GetDisplayName()} is already a child of {drawable.Parent.GetDisplayName()}.");
        }

        for (var p = Parent; p != null; p = p.Parent)
        {
            if (p == drawable)
                throw new InvalidOperationException("Cannot add an ancestor drawable as a child to prevent cyclic dependencies.");
        }

        drawable.Parent = this;
        drawable.ChildInsertionOrder = ++childInsertionCounter;
        children.Add(drawable);

        InvalidateTopology();

        // Share our clock by reference (unless the child has an explicitly-assigned clock).
        // This must happen before Load() so anything scheduled during load uses the right timeline.
        if (IsLoaded)
            drawable.InheritClock(Clock);

        // The (possibly re-added, previously clean) drawable needs a full geometry pass
        // relative to its new parent; its own Update cascades this through its subtree.
        drawable.Invalidate(InvalidationFlags.DrawInfo, false);

        // A new child only affects us if we layout around children (auto-size / flow).
        OnChildGeometryInvalidated();

        if (IsLoaded)
        {
            try
            {
                drawable.Load();
                drawable.LoadComplete();
                drawable.Invalidate(InvalidationFlags.DrawInfo, false);
            }
            catch
            {
                // rollback the addition if loading fails (e.g. synchronous LongRunningLoad constraint violation)
                children.Remove(drawable);
                drawable.Parent = null;
                InvalidateTopology();
                throw;
            }
        }
    }

    protected void RemoveInternal(Drawable drawable)
    {
        if (drawable == null)
            return;

        if (drawable.Parent != this)
            throw new InvalidOperationException($"Cannot remove {drawable.GetDisplayName()} because it is not a child of this container.");

        if (children.Remove(drawable))
        {
            drawable.Parent = null;
            OnChildGeometryInvalidated();
            InvalidateTopology();

            if (drawable.DisposeOnRemoval && drawable is IDisposable disposable)
                disposable.Dispose();
        }
    }

    protected void ClearInternal()
    {
        foreach (var child in children)
        {
            child.Parent = null;

            if (child.DisposeOnRemoval && child is IDisposable disposable)
                disposable.Dispose();
        }
        children.Clear();
        OnChildGeometryInvalidated();
        InvalidateTopology();
    }

    /// <summary>
    /// Invoked when a direct child's geometry has been invalidated (or children were added/removed).
    /// The default implementation only reacts when this container lays out around its children
    /// (<see cref="AutoSizeAxes"/>); other containers ignore the notification entirely, which is
    /// what prevents one moving drawable from re-invalidating the whole tree.
    /// Subclasses whose layout depends on children (e.g. flow containers) should override this.
    /// </summary>
    protected internal virtual void OnChildGeometryInvalidated()
    {
        if (AutoSizeAxes != Axes.None)
            InvalidateLayout();
    }

    internal void InvalidateTopology()
    {
        TopologyVersion++;
        MarkSubtreeDrawStateDirty();
    }

    // Bumped whenever any descendant's drawn state, topology, lifetime or masking state
    // changes. Draw nodes store the version they were generated against, letting fully
    // clean subtrees skip draw-node regeneration entirely.
    private long subtreeDrawVersion = 1;

    /// <summary>
    ///  True while this container has a subtree change that has not yet been flushed into a
    /// rebuilt draw node. Used purely to dedup the walk up the parent chain: while it is set,
    /// our ancestors have already been notified and their versions are already ahead of their
    /// cached draw nodes, so re-walking is redundant.
    ///
    /// Crucially the flag is cleared when this container's draw-node subtree is actually
    /// rebuilt (see GenerateDrawNodeSubtree) — NOT on a frame boundary. The previous design
    /// reset it in Update(), but MarkSubtreeDrawStateDirty can run before the update traversal
    /// (input is dispatched at the very start of the update frame, so a click that clears or
    /// removes drawables fires here). At that point the flag still held its stale value from the
    /// previous frame; if it was set, the walk short-circuited and the ancestors' versions were
    /// never bumped, so they kept skipping regeneration and drawing removed drawables from their
    /// cached child lists. Tying the reset to the rebuild removes that ordering dependency.
    /// </summary>
    private bool subtreeDirty;

    /// <summary>
    /// Marks this container's (and all ancestors') cached draw-node subtree as stale so it is
    /// rebuilt on the next generation. Safe to call at any point in the frame, including before
    /// the update traversal (e.g. from input handlers).
    /// </summary>
    internal void MarkSubtreeDrawStateDirty()
    {
        subtreeDrawVersion++;

        if (subtreeDirty)
            return;

        subtreeDirty = true;
        Parent?.MarkSubtreeDrawStateDirty();
    }

    public override void Update()
    {
        // Check whether our layout was dirty before base.Update() is called, as it will clear our invalidation flags.
        bool layoutWasInvalidated = (Invalidation & InvalidationFlags.DrawInfo) != 0;
        bool colorWasInvalidated = (Invalidation & InvalidationFlags.Color) != 0;

        if (AutoSizeAxes != Axes.None && layoutWasInvalidated)
        {
            UpdateAutoSize();
        }

        // Captured after auto-size so a size change resulting from layout is included.
        // Children only need re-invalidation when our own geometry changed — a child merely
        // notifying us for layout purposes must not cascade to its siblings.
        bool ownGeometryWasInvalidated = OwnGeometryInvalidated;

        base.Update();

        if (colorWasInvalidated)
        {
            foreach (var child in children)
            {
                child.Invalidate(InvalidationFlags.Color, false);
            }
        }

        if (!AlwaysPresent && Precision.AlmostEqualZero(Alpha))
            return;

        for (int i = children.Count - 1; i >= 0; i--)
        {
            var child = children[i];

            // Lifetime crossings change draw-tree membership without any invalidation,
            // so the cached draw-node subtree must be refreshed when one occurs.
            bool alive = child.IsAlive;
            if (alive != child.WasAlive)
            {
                child.WasAlive = alive;
                MarkSubtreeDrawStateDirty();
            }

            if (Clock.CurrentTime >= child.LifetimeEnd && child.RemoveWhenNotAlive)
            {
                Remove(child);
            }
        }

        if (ownGeometryWasInvalidated)
        {
            foreach (var child in children)
            {
                child.Invalidate(InvalidationFlags.DrawInfo, false);
            }
        }
    }

    public override void UpdateSubTree()
    {
        if (IsMaskedAway && !AlwaysPresent)
            return;

        // Resolve our own Update() first so our ModelMatrix and DrawRectangle are accurate
        base.UpdateSubTree();

        // Compute which children are off-screen
        UpdateSubTreeMasking();

        // Iterate backwards to safely allow children to remove themselves during their update cycles
        for (int i = children.Count - 1; i >= 0; i--)
        {
            // Optional sanity check if multiple children got removed during updates
            if (i < children.Count)
            {
                children[i].UpdateSubTree();
            }
        }
    }

    protected virtual void UpdateSubTreeMasking()
    {
        RectangleF? maskToApply = Masking ? DrawRectangle : CurrentMaskingBounds;

        for (int i = children.Count - 1; i >= 0; i--)
        {
            // sanity check in case the list was aggressively shrunk by another thread
            if (i >= children.Count)
                continue;

            var child = children[i];
            child.CurrentMaskingBounds = maskToApply;

            bool maskedAway = false;

            if (maskToApply.HasValue)
            {
                if ((child.Invalidation & InvalidationFlags.DrawInfo) != 0)
                {
                    child.UpdateTransforms();
                }

                RectangleF childRect = child.DrawRectangle;
                float leniency = 1.0f;

                bool intersects = childRect.X <= maskToApply.Value.X + maskToApply.Value.Width + leniency &&
                                  childRect.X + childRect.Width >= maskToApply.Value.X - leniency &&
                                  childRect.Y <= maskToApply.Value.Y + maskToApply.Value.Height + leniency &&
                                  childRect.Y + childRect.Height >= maskToApply.Value.Y - leniency;

                maskedAway = !intersects;
            }

            if (child.IsMaskedAway != maskedAway)
            {
                child.IsMaskedAway = maskedAway;

                // Masking transitions change draw-tree membership without an invalidation.
                MarkSubtreeDrawStateDirty();
            }
        }
    }

    protected virtual void UpdateAutoSize()
    {
        Vector2 maxBound = Vector2.Zero;

        foreach (var child in children)
        {
            if (!child.AlwaysPresent && child.Alpha <= 0)
                continue;

            // Calculate the child's size in pixels
            // We cannot rely on child.DrawSize here because that might be from the previous frame.
            // We must calculate it based on the current state.
            Vector2 childSize = child.Size;

            // Note : If a child is RelativeSize on the same axis we are AutoSizing,
            // we must ignore it to prevent circular dependency (or endless expansion).
            // e.g., Parent (AutoSize X) -> Child (Relative X) -> Paradox.

            if ((AutoSizeAxes & Axes.X) != 0 && (child.RelativeSizeAxes & Axes.X) != 0)
                childSize.X = 0; // Ignore relative width for auto-width calculation

            if ((AutoSizeAxes & Axes.Y) != 0 && (child.RelativeSizeAxes & Axes.Y) != 0)
                childSize.Y = 0; // Ignore relative height for auto-height calculation

            // Apply Scale
            Vector2 scaledSize = childSize * child.Scale;

            // Determine position.
            // Note: For advanced auto-sizing (handling rotations/shears), we would need full matrix bounding boxes.
            // For this implementation, we assume standard AABB flow (Position + Size + Margin).
            Vector2 childPos = child.Position;

            // If child is relatively positioned, it technically positions based on us.
            // But since we are determining our size, we treat relative positioning as 0 or ignore it
            // to avoid stability issues, unless we implement a multi-pass layout solver.
            // For simplicity: We use the raw local position.
            float right = childPos.X + child.Margin.Left + scaledSize.X + child.Margin.Right;
            float bottom = childPos.Y + child.Margin.Top + scaledSize.Y + child.Margin.Bottom;

            // Also account for origin offsets if necessary, but standard implementation
            // usually assumes TopLeft origin for simple flow calculations.
            // If you use Center anchors, you might need to subtract the anchor offset.
            // For now, simple bounding box extension:
            if (right > maxBound.X) maxBound.X = right;
            if (bottom > maxBound.Y) maxBound.Y = bottom;
        }

        // Apply Padding of the container itself
        maxBound += Padding.Total;

        // Apply to Size
        Vector2 currentSize = Size;

        if ((AutoSizeAxes & Axes.X) != 0)
            currentSize.X = maxBound.X;

        if ((AutoSizeAxes & Axes.Y) != 0)
            currentSize.Y = maxBound.Y;

        if (AutoSizeDuration <= 0)
        {
            if (autoSizeTransform != null)
            {
                RemoveTransform(autoSizeTransform);
                autoSizeTransform = null;
            }

            if (Size != currentSize)
                Size = currentSize;

            return;
        }

        if (autoSizeTransform == null || autoSizeTarget != currentSize)
        {
            if (autoSizeTransform == null && Size == currentSize)
                return;

            if (autoSizeTransform != null)
                RemoveTransform(autoSizeTransform);

            autoSizeTarget = currentSize;
            double startTime = Clock.CurrentTime;

            autoSizeTransform = new AutoSizeTransform
            {
                StartValue = Size,
                EndValue = currentSize,
                Easing = AutoSizeEasing,
                StartTime = startTime,
                EndTime = startTime + AutoSizeDuration
            };

            AddTransform(autoSizeTransform);
        }
    }

    protected override DrawNode CreateDrawNode() => new ContainerDrawNode();

    public override DrawNode GenerateDrawNodeSubtree(int frameIndex)
    {
        var node = (ContainerDrawNode)base.GenerateDrawNodeSubtree(frameIndex);

        // Per-buffer cache. Each of the buffered draw nodes tracks the subtree version it was
        // built against. A change bumps subtreeDrawVersion, so every buffer's node becomes stale
        // and rebuilds the next time that buffer index is generated — no separate multi-buffer
        // force counter is needed, and no buffer can retain a stale child list once the version
        // moves ahead of it.
        if (node.AppliedSubtreeVersion == subtreeDrawVersion)
            return node;

        // This buffer is stale and about to be rebuilt, flushing the pending change. Allow the
        // next change to walk the parent chain again.
        subtreeDirty = false;

        node.Children.Clear();

        var sorted = getSortedChildren();

        for (int i = 0; i < sorted.Count; i++)
        {
            var child = sorted[i];

            if ((!child.IsMaskedAway && child.IsAlive) || child.AlwaysPresent)
            {
                node.Children.Add(child.GenerateDrawNodeSubtree(frameIndex));
            }
        }

        node.AppliedSubtreeVersion = subtreeDrawVersion;

        return node;
    }

    public override void Load()
    {
        base.Load();
        foreach (var child in children)
        {
            child.InheritClock(Clock);
            child.Load();
        }
    }

    public override void LoadComplete()
    {
        base.LoadComplete();
        foreach (var child in children)
        {
            child.LoadComplete();
        }
    }

    protected override void OnClockChanged()
    {
        base.OnClockChanged();

        foreach (var child in children)
            child.InheritClock(Clock);
    }

    #region Event Propagation

    public override bool OnDragDropFile(DragDropFileEvent e)
    {
        var sorted = getSortedChildren();

        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            if (i >= sorted.Count) continue;

            var c = sorted[i];

            if (c.IsLoaded && !c.IsHidden && c.Contains(e.Position))
            {
                if (c.OnDragDropFile(e))
                    return true;
            }
        }

        return base.OnDragDropFile(e);
    }

    public override bool OnDragDropText(DragDropTextEvent e)
    {
        var sorted = getSortedChildren();

        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            if (i >= sorted.Count) continue;

            var c = sorted[i];

            if (c.IsLoaded && !c.IsHidden && c.Contains(e.Position))
            {
                if (c.OnDragDropText(e))
                    return true;
            }
        }

        return base.OnDragDropText(e);
    }

    #endregion

    #region Child Querying & Management

    /// <summary>
    /// Checks whether the specified drawable is a direct child of this container.
    /// </summary>
    public virtual bool Contains(Drawable drawable)
    {
        if (Content != this)
            return Content.Contains(drawable);

        return children.Contains(drawable);
    }

    /// <summary>
    /// Retrieves all children of a specific type.
    /// </summary>
    public IEnumerable<T> ChildrenOfType<T>() where T : Drawable
    {
        return Children.OfType<T>();
    }

    /// <summary>
    /// Removes all children that match the conditions defined by the specified predicate.
    /// </summary>
    /// <returns>The number of children removed from the container.</returns>
    public virtual int RemoveAll(Predicate<Drawable> match)
    {
        if (Content != this)
            return Content.RemoveAll(match);

        var toRemove = children.Where(match.Invoke).ToList();

        foreach (var child in toRemove)
        {
            RemoveInternal(child);
        }

        return toRemove.Count;
    }

    /// <summary>
    /// Removes a collection of drawables from this container.
    /// </summary>
    public virtual void RemoveRange(IEnumerable<Drawable> drawables)
    {
        if (Content != this)
        {
            Content.RemoveRange(drawables);
            return;
        }

        foreach (var drawable in drawables.ToList())
        {
            RemoveInternal(drawable);
        }
    }

    #endregion
}
