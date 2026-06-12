// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
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

    private Drawable? draggedChild;

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

    /// <summary>
    /// Control which axes that this container automatically sized based on its children's sizes.
    /// </summary>
    public Axes AutoSizeAxes { get; set; } = Axes.None;

    public IReadOnlyList<Drawable> Children
    {
        get => Content == this ? children : Content.Children;
        set
        {
            Clear();
            foreach (var child in value)
            {
                Add(child);
            }
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
    private bool subtreeDirtyNotified;

    /// <summary>
    /// Marks this container's (and all ancestors') cached draw-node subtree as stale.
    /// The walk up the parent chain short-circuits once per frame per container.
    /// </summary>
    internal void MarkSubtreeDrawStateDirty()
    {
        subtreeDrawVersion++;

        if (subtreeDirtyNotified)
            return;

        subtreeDirtyNotified = true;
        Parent?.MarkSubtreeDrawStateDirty();
    }

    public override void Update()
    {
        // Allow a fresh subtree-dirty notification walk this frame.
        subtreeDirtyNotified = false;

        // Check whether our layout was dirty before base.Update() is called, as it will clear our invalidation flags.
        bool layoutWasInvalidated = (Invalidation & InvalidationFlags.DrawInfo) != 0;
        bool colourWasInvalidated = (Invalidation & InvalidationFlags.Colour) != 0;

        if (AutoSizeAxes != Axes.None && layoutWasInvalidated)
        {
            UpdateAutoSize();
        }

        // Captured after auto-size so a size change resulting from layout is included.
        // Children only need re-invalidation when our own geometry changed — a child merely
        // notifying us for layout purposes must not cascade to its siblings.
        bool ownGeometryWasInvalidated = OwnGeometryInvalidated;

        base.Update();

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

        if (colourWasInvalidated)
        {
            foreach (var child in children)
            {
                child.Invalidate(InvalidationFlags.Colour, false);
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

        // Only assign if changed to prevent constant invalidation.
        // The Size setter raises the own-geometry invalidation (cascading to children)
        // and notifies an interested parent, so no explicit invalidation is needed here.
        if (Size != currentSize)
            Size = currentSize;
    }

    protected override DrawNode CreateDrawNode() => new ContainerDrawNode();

    public override DrawNode GenerateDrawNodeSubtree(int frameIndex)
    {
        var node = (ContainerDrawNode)base.GenerateDrawNodeSubtree(frameIndex);

        // If nothing in this subtree changed since this buffer's node was last generated
        // (no invalidations, topology, lifetime or masking transitions), the cached child
        // node list is still valid and the entire subtree walk can be skipped.
        if (node.AppliedSubtreeVersion == subtreeDrawVersion)
            return node;

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

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        var sorted = getSortedChildren();

        // Iterate front-to-back (highest depth first).
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            if (i >= sorted.Count) continue; // handler may have mutated the hierarchy

            var c = sorted[i];

            if (c.IsLoaded && c.IsAlive && !c.IsHidden && c.Contains(e.ScreenSpaceMousePosition) && c.OnMouseDown(e))
            {
                draggedChild = c;
                return true;
            }
        }

        return base.OnMouseDown(e);
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        bool handled = false;

        // If a drag was in progress, only the dragged child should receive the OnMouseUp event.
        if (draggedChild != null)
        {
            bool result = draggedChild.OnMouseUp(e);
            draggedChild = null; // The drag operation concludes.
            return result;
        }

        var sorted = getSortedChildren();

        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            if (i >= sorted.Count) continue;

            var c = sorted[i];

            if (c.IsLoaded && c.IsAlive && !c.IsHidden && c.Contains(e.ScreenSpaceMousePosition) && c.OnMouseUp(e))
                return true;
        }

        return base.OnMouseUp(e);
    }

    public override bool OnMouseMove(MouseEvent e)
    {
        bool handled = false;

        // If a drag is in progress, the dragged child must get the event first.
        if (draggedChild != null)
        {
            handled = draggedChild.OnMouseMove(e);
        }

        // Continue routing to other children, front-to-back.
        var sorted = getSortedChildren();

        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            if (i >= sorted.Count) continue;

            var c = sorted[i];

            if (c == draggedChild)
                continue;

            if (c.IsLoaded && c.IsAlive && !c.IsHidden && (c.IsHovered || c.Contains(e.ScreenSpaceMousePosition)))
            {
                if (c.OnMouseMove(e))
                {
                    handled = true;
                    break;
                }
            }
        }

        if (handled)
            return true;

        return base.OnMouseMove(e);
    }

    public override bool OnScroll(ScrollEvent e)
    {
        // Propagate to the first child that contains the mouse position, front-to-back.
        var sorted = getSortedChildren();

        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            if (i >= sorted.Count) continue;

            var c = sorted[i];

            if (c.IsLoaded && c.IsAlive && !c.IsHidden && c.Contains(e.ScreenSpaceMousePosition) && c.OnScroll(e))
                return true;
        }

        return base.OnScroll(e);
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].OnKeyDown(e))
                return true;
        }

        return false;
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].OnKeyUp(e))
                return true;
        }

        return false;
    }

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

    public override bool OnTextInput(TextInputEvent e)
    {
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].OnTextInput(e))
                return true;
        }

        return false;
    }

    public override bool OnTextEditing(TextEditingEvent e)
    {
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].OnTextEditing(e))
                return true;
        }

        return false;
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
