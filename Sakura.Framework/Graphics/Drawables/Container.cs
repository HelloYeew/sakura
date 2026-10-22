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

    /// <summary>
    /// <see cref="Children"/> sorted by depth (back-to-front draw order), following any
    /// <see cref="Content"/> override the way <see cref="Children"/> does.
    /// </summary>
    internal IReadOnlyList<Drawable> SortedContentChildren => Content == this ? getSortedChildren() : Content.SortedContentChildren;

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

    private MarginPadding maskingFade;

    /// <summary>
    /// How far in from each edge the children fade out when masking: transparent at the edge, opaque this
    /// far in. 0 on a side (the default) is a hard edge there. A soft edge for a scrolling list, say, so
    /// what scrolls out fades away rather than being cut off.
    /// </summary>
    /// <remarks>
    /// Measured from the container's bounding box, so a sheared container's slanted sides aren't
    /// followed. A masking container inside it fades with it, unless it fades a side itself, which then
    /// replaces this one on that side. Content composited premultiplied (a <see cref="BufferedContainer"/>)
    /// has only its alpha faded, so it lightens rather than fades.
    /// </remarks>
    public MarginPadding MaskingFade
    {
        get => maskingFade;
        set
        {
            if (maskingFade.Equals(value)) return;
            maskingFade = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    private float borderThickness;

    /// <summary>
    /// The thickness of the border when masking.
    /// </summary>
    public float BorderThickness
    {
        get => borderThickness;
        set
        {
            if (Precision.AlmostEquals(borderThickness, value)) return;
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
            if (borderColor.Equals(value)) return;
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

    private Axes autoSizeAxes;

    /// <summary>
    /// The axes on which this container sizes itself to fit its children. Each child counts as placed
    /// by its own anchor, origin, position, scale and margins, so a centred child grows the container
    /// evenly both ways and a right-anchored one from the right.
    /// Children sized relative to this container on an auto-sized axis, positioned relatively on one,
    /// or bypassing it (<see cref="Drawable.BypassAutoSizeAxes"/>) don't count on that axis.
    /// </summary>
    /// <remarks>
    /// The size is worked out when read as well as on update, so it's right straight after a child
    /// changes. Rotation and shear of children aren't accounted for.
    /// </remarks>
    public Axes AutoSizeAxes
    {
        get => autoSizeAxes;
        set
        {
            if (autoSizeAxes == value) return;
            autoSizeAxes = value;
            InvalidateChildLayout();
        }
    }

    private Vector2 autoSizeMinimum;

    /// <summary>
    /// The smallest an auto-sized axis gets, padding included. Zero (no minimum) by default.
    /// </summary>
    public Vector2 AutoSizeMinimum
    {
        get => autoSizeMinimum;
        set
        {
            if (autoSizeMinimum == value) return;
            autoSizeMinimum = value;
            InvalidateChildLayout();
        }
    }

    private Vector2 autoSizeMaximum = new Vector2(float.PositiveInfinity);

    /// <summary>
    /// The largest an auto-sized axis gets, padding included. Unbounded by default.
    /// </summary>
    public Vector2 AutoSizeMaximum
    {
        get => autoSizeMaximum;
        set
        {
            if (autoSizeMaximum == value) return;
            autoSizeMaximum = value;
            InvalidateChildLayout();
        }
    }

    /// <summary>
    /// The duration (in milliseconds) over which the container animates toward a new auto-size target.
    /// When 0 (the default), the container snaps to its computed size instantly.
    /// Only meaningful when <see cref="AutoSizeAxes"/> is not <see cref="Axes.None"/>.
    /// </summary>
    public double AutoSizeDuration
    {
        get => autoSizeDuration;
        set
        {
            if (autoSizeDuration == value) return;
            autoSizeDuration = value;
            // Turned off mid-animation, it snaps to where it was going.
            InvalidateChildLayout();
        }
    }

    private double autoSizeDuration;

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
        if (base.Size != newSize)
            base.Size = newSize;
    }

    /// <summary>
    /// This container's size. While it auto-sizes, reading it first brings that layout up to date if a
    /// child (or anything else it depends on) changed, so it's never a frame behind; mid-animation
    /// (<see cref="AutoSizeDuration"/>) it's the size the animation has reached.
    /// </summary>
    public override Vector2 Size
    {
        get
        {
            if (autoSizeAxes != Axes.None)
                EnsureChildLayout();

            return base.Size;
        }
        set => base.Size = value;
    }

    /// <summary>Whether the layout that depends on the children has to run again (see <see cref="EnsureChildLayout"/>).</summary>
    private bool childLayoutPending = true;

    /// <summary>
    /// Whether a child's own layout is pending, so its size may be about to change. The child is settled
    /// first, and only if its size really changes does this container lay out again.
    /// </summary>
    private bool childLayoutMaybe;

    private bool childLayoutRunning;

    /// <summary>
    /// Whether this container's layout depends on its children: it auto-sizes, or (a flow) arranges them.
    /// Only then does a child's change make it lay out again.
    /// </summary>
    protected virtual bool LaysOutAroundChildren => autoSizeAxes != Axes.None;

    /// <summary>
    /// Whether something the layout used other than the children has changed since it last ran, such as a
    /// flow's own width. Only asked when <see cref="ChildLayoutTracksOwnSize"/> is set.
    /// </summary>
    protected virtual bool IsChildLayoutStale() => false;

    /// <summary>
    /// Set (in the constructor) by a container whose child layout depends on its own size, so
    /// <see cref="IsChildLayoutStale"/> is asked every update. Others skip the question entirely.
    /// </summary>
    protected bool ChildLayoutTracksOwnSize { get; init; }

    /// <summary>Whether <see cref="EnsureChildLayout"/> could have anything to do: the per-frame fast path.</summary>
    private bool mayNeedChildLayout => childLayoutPending || childLayoutMaybe || ChildLayoutTracksOwnSize;

    /// <summary>
    /// Marks the layout that depends on the children (auto-size, a flow's arrangement) as needing to run
    /// again, and this container as needing an update for it.
    /// </summary>
    protected void InvalidateChildLayout()
    {
        InvalidateLayout();

        // Already pending: whatever depends on us was told then, and hasn't read us since (a read
        // would have run the layout).
        if (childLayoutPending)
            return;

        childLayoutPending = true;

        // Our size follows our children, so it may be about to change: cached sizes can't be trusted,
        // and whatever lays out around us has to settle us before trusting ours.
        if (autoSizeAxes != Axes.None)
        {
            MarkSizesChanged();
            Parent?.onChildLayoutPending();
        }
    }

    /// <summary>A child's layout is pending, so its size may change when it runs (see <see cref="childLayoutMaybe"/>).</summary>
    private void onChildLayoutPending()
    {
        if (!LaysOutAroundChildren || childLayoutPending || childLayoutMaybe)
            return;

        childLayoutMaybe = true;
        InvalidateLayout();

        if (autoSizeAxes != Axes.None)
            Parent?.onChildLayoutPending();
    }

    /// <summary>
    /// Runs the pending layout of each child that might resize. Any that does tells this container (as any
    /// resize does), which makes its own layout pending; the rest leave it as it was.
    /// </summary>
    private void settleChildren()
    {
        foreach (var child in children)
        {
            if (child is not Container container || container.autoSizeAxes == Axes.None)
                continue;

            if (!container.childLayoutPending && !container.childLayoutMaybe)
                continue;

            if (!child.AlwaysPresent && child.Alpha <= 0)
                continue;

            container.EnsureChildLayout();
        }
    }

    /// <summary>
    /// Runs the layout that depends on the children (<see cref="PerformChildLayout"/>) now, if anything it
    /// depends on changed since it last ran. It runs by itself when this container updates (while
    /// visible) and when its size is read while auto-sizing.
    /// </summary>
    /// <remarks>
    /// Re-entrant reads (a layout that reads this container's own size, as a flow does for children sized
    /// relative to it) get the current size rather than starting the layout again. Changes the layout makes
    /// itself, like moving a child into place, don't mark it pending again.
    /// </remarks>
    protected void EnsureChildLayout()
    {
        if (childLayoutRunning || !mayNeedChildLayout)
            return;

        // Guarded from the first check: settling a child or checking our own space can read our size
        // back, which must get the current size rather than come round here again.
        childLayoutRunning = true;

        try
        {
            if (!childLayoutPending && ChildLayoutTracksOwnSize && IsChildLayoutStale())
                childLayoutPending = true;

            if (childLayoutMaybe)
            {
                childLayoutMaybe = false;

                if (!childLayoutPending)
                    settleChildren();
            }

            if (!childLayoutPending)
                return;

            PerformChildLayout();
            childLayoutPending = false;
        }
        finally
        {
            childLayoutRunning = false;
        }
    }

    /// <summary>
    /// The layout that depends on the children: here, auto-size. A flow arranges its children here too.
    /// Call <see cref="EnsureChildLayout"/> rather than this.
    /// </summary>
    protected virtual void PerformChildLayout()
    {
        if (autoSizeAxes != Axes.None)
            UpdateAutoSize();
    }

    /// <summary>
    /// Every child this container holds directly, including those added with
    /// <see cref="AddInternal"/> and regardless of any <see cref="Content"/> override.
    /// </summary>
    public IReadOnlyList<Drawable> InternalChildren => children;

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

    /// <summary>
    /// The size of the space children are laid out in, as it is now: <see cref="Drawable.DrawSize"/> less
    /// <see cref="Drawable.Padding"/>.
    /// </summary>
    public Vector2 ChildSize => DrawSize - Padding.Total;

    /// <summary>
    /// <see cref="ChildSize"/> as of the last layout (<see cref="Drawable.AppliedDrawSize"/>), which this
    /// container's matrix is for, so children's matrices nest into it.
    /// </summary>
    internal Vector2 AppliedChildSize => AppliedDrawSize - Padding.Total;

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
            MarkSizesChanged();

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
    internal Vector2 RelativeToAbsoluteFactor => toRelativeFactor(ChildSize);

    /// <summary><see cref="RelativeToAbsoluteFactor"/> as of the last layout (see <see cref="AppliedChildSize"/>).</summary>
    internal Vector2 AppliedRelativeToAbsoluteFactor => toRelativeFactor(AppliedChildSize);

    private Vector2 toRelativeFactor(Vector2 childSize) => new Vector2(childSize.X / relativeChildSize.X, childSize.Y / relativeChildSize.Y);

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

    /// <summary>
    /// Removes a child from this container and disposes it.
    /// </summary>
    /// <param name="drawable">The child to remove.</param>
    /// <param name="dispose">
    /// Whether the removed drawable should be disposed, recursively through its own children. Defaults to
    /// true: a drawable that leaves the tree is assumed to be finished with, and this is the only thing
    /// that releases the textures, framebuffers and decoders below it. Pass false to remove a drawable in
    /// order to add it to another container. A drawable that sets
    /// <see cref="Drawable.DisposeOnRemoval"/> to false is never disposed here regardless.
    /// </param>
    /// <remarks>
    /// Disposal may be deferred by up to a frame (see <see cref="DrawableDisposalQueue"/>), so it is not
    /// guaranteed to have completed by the time this returns. The drawable is detached immediately either
    /// way, so it stops updating and drawing at once.
    /// </remarks>
    public virtual void Remove(Drawable drawable, bool dispose = true)
    {
        if (Content != this)
        {
            Content.Remove(drawable, dispose);
            return;
        }

        RemoveInternal(drawable, dispose);
    }

    /// <summary>
    /// Removes every child of this container and disposes them.
    /// </summary>
    /// <param name="dispose">
    /// Whether the removed children should be disposed, recursively. See
    /// <see cref="Remove(Drawable, bool)"/> for when to pass false.
    /// </param>
    public virtual void Clear(bool dispose = true)
    {
        if (Content != this)
        {
            Content.Clear(dispose);
            return;
        }

        ClearInternal(dispose);
    }

    protected void AddInternal(Drawable drawable)
    {
        ArgumentNullException.ThrowIfNull(drawable);

        if (drawable == this)
            throw new InvalidOperationException("A container cannot be added to itself.");

        if (drawable.IsDisposed)
            throw new InvalidOperationException($"Cannot add {drawable.GetDisplayName()} because it has been disposed. Remove it with dispose: false if it is to be re-added elsewhere.");

        // Removed with disposal pending and re-added before the queue got to it: it is live again, so
        // cancel the disposal rather than handing the caller a drawable that dies a frame later.
        drawable.DisposalPending = false;

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
                drawable.CompleteLoad();
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

    protected void RemoveInternal(Drawable drawable, bool dispose = true)
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

            disposeRemoved(drawable, dispose);
        }
    }

    protected void ClearInternal(bool dispose = true)
    {
        // Each child is taken off the list before it is detached, rather than iterating and clearing
        // afterwards. Detaching runs OnParentChanged — that is where a pooled child returns itself to its
        // pool — and an override there is free to touch this container, which iterate-then-clear could not
        // survive. Draining from the end also keeps this allocation-free, and tearing down a screen runs
        // it once per container in the subtree.
        while (children.Count > 0)
        {
            var child = children[^1];
            children.RemoveAt(children.Count - 1);

            child.Parent = null;
            disposeRemoved(child, dispose);
        }

        OnChildGeometryInvalidated();
        InvalidateTopology();
    }

    /// <summary>
    /// Applies removal-triggered disposal to a drawable that has just left this container, honouring
    /// both the remover's request and the drawable's own <see cref="Drawable.DisposeOnRemoval"/>.
    /// </summary>
    private static void disposeRemoved(Drawable drawable, bool dispose)
    {
        if (!dispose || !drawable.DisposeOnRemoval)
            return;

        DrawableDisposalQueue.Enqueue(drawable);
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
        if (LaysOutAroundChildren)
            InvalidateChildLayout();
    }

    protected internal override void OnLayoutInputChanged()
    {
        base.OnLayoutInputChanged();
        InvalidateChildLayout();
    }

    protected internal override void OnTransformsCleared()
    {
        base.OnTransformsCleared();

        // An auto-size animation went with the rest: forget it and size afresh, from where it stopped.
        if (autoSizeTransform != null)
        {
            autoSizeTransform = null;
            InvalidateChildLayout();
        }
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
        // Check before base.Update() is called, as it will clear our invalidation flags.
        bool colorWasInvalidated = (Invalidation & InvalidationFlags.Color) != 0;

        // Hidden, the layout waits until it's shown, or until something reads the size.
        if (mayNeedChildLayout && !IsEffectivelyHidden)
            EnsureChildLayout();

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

        bool hiddenBelow = IsEffectivelyHidden;

        // Iterate backwards to safely allow children to remove themselves during their update cycles
        for (int i = children.Count - 1; i >= 0; i--)
        {
            // Optional sanity check if multiple children got removed during updates
            if (i < children.Count)
            {
                children[i].SetAncestorHidden(hiddenBelow);
                children[i].UpdateSubTree();
            }
        }

        UpdateLayoutAfterChildren();
    }

    /// <summary>
    /// Re-resolves this container's own layout now that its children have updated, so a nested
    /// composition settles within one frame. Everything that sizes a container from its children
    /// necessarily runs against stale values when it runs in <see cref="Update"/>, because children
    /// update afterward; this is the corrective pass.
    /// </summary>
    protected virtual void UpdateLayoutAfterChildren()
    {
        if (!(childLayoutPending || childLayoutMaybe) || IsEffectivelyHidden)
            return;

        Vector2 sizeBefore = base.Size;

        EnsureChildLayout();

        if (base.Size != sizeBefore)
            UpdateTransforms();
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

    /// <summary>
    /// Sizes this container around its children on <see cref="AutoSizeAxes"/> (see there for what counts),
    /// clamped to <see cref="AutoSizeMinimum"/>/<see cref="AutoSizeMaximum"/>, snapping or animating per
    /// <see cref="AutoSizeDuration"/>. Children are read through <see cref="Drawable.Size"/>, which
    /// measures text and resolves nested auto-size on demand, so the result is current.
    /// </summary>
    protected virtual void UpdateAutoSize()
    {
        Axes axes = autoSizeAxes & ~RelativeSizeAxes;
        Vector2 required = Vector2.Zero;

        foreach (var child in children)
        {
            if (!child.AlwaysPresent && child.Alpha <= 0)
                continue;

            Vector2 size = child.Size * child.Scale;
            Vector2 position = child.Position;
            Vector2 margin = child.Margin.Total;

            // The common case, placed from the top-left corner both ways: just where it reaches to.
            if (child.Anchor == Anchor.TopLeft && child.Origin == Anchor.TopLeft && child.RelativePositionAxes == Axes.None)
            {
                if ((axes & Axes.X) != 0 && countsOn(child, Axes.X))
                    required.X = Math.Max(required.X, position.X + size.X + margin.X);

                if ((axes & Axes.Y) != 0 && countsOn(child, Axes.Y))
                    required.Y = Math.Max(required.Y, position.Y + size.Y + margin.Y);

                continue;
            }

            Vector2 anchor = GetAnchorOriginVector(child.Anchor);
            Vector2 origin = GetAnchorOriginVector(child.Origin);

            if ((axes & Axes.X) != 0 && countsOn(child, Axes.X))
            {
                float x = (child.RelativePositionAxes & Axes.X) != 0 ? 0 : position.X;
                required.X = Math.Max(required.X, RequiredExtent(x, size.X, margin.X, anchor.X, origin.X));
            }

            if ((axes & Axes.Y) != 0 && countsOn(child, Axes.Y))
            {
                float y = (child.RelativePositionAxes & Axes.Y) != 0 ? 0 : position.Y;
                required.Y = Math.Max(required.Y, RequiredExtent(y, size.Y, margin.Y, anchor.Y, origin.Y));
            }
        }

        Vector2 currentSize = base.Size;

        if ((axes & Axes.X) != 0)
            currentSize.X = ClampAutoSize(required.X + Padding.Total.X, Axes.X);

        if ((axes & Axes.Y) != 0)
            currentSize.Y = ClampAutoSize(required.Y + Padding.Total.Y, Axes.Y);

        ApplyAutoSizeTarget(currentSize);
    }

    /// <summary>Whether <paramref name="child"/> sizes this container on <paramref name="axis"/>.</summary>
    private static bool countsOn(Drawable child, Axes axis) =>
        (child.RelativeSizeAxes & axis) == 0 && (child.BypassAutoSizeAxes & axis) == 0;

    /// <summary>
    /// How big a parent's content has to be on one axis for a child to fit in it, placed at
    /// <paramref name="anchor"/> (0 start, 0.5 centre, 1 end) with its <paramref name="origin"/> at
    /// <paramref name="position"/>, at <paramref name="size"/> (scaled), with <paramref name="margin"/>
    /// (both sides) around it. It has to reach neither past the far edge nor before the near one: for a
    /// start anchor and origin that's just position, size and margin.
    /// </summary>
    protected internal static float RequiredExtent(float position, float size, float margin, float anchor, float origin)
    {
        float need = float.NegativeInfinity;

        if (anchor < 1)
            need = Math.Max(need, (position + (1 - origin) * size) / (1 - anchor));

        if (anchor > 0)
            need = Math.Max(need, (origin * size - position) / anchor);

        return margin + need;
    }

    /// <summary>Clamps an auto-sized <paramref name="axis"/> to <see cref="AutoSizeMinimum"/>/<see cref="AutoSizeMaximum"/>.</summary>
    protected float ClampAutoSize(float value, Axes axis)
    {
        float min = axis == Axes.X ? autoSizeMinimum.X : autoSizeMinimum.Y;
        float max = axis == Axes.X ? autoSizeMaximum.X : autoSizeMaximum.Y;

        return Math.Clamp(Math.Max(0, value), min, Math.Max(min, max));
    }

    /// <summary>
    /// Takes <paramref name="target"/> as this container's size: at once, or animated over
    /// <see cref="AutoSizeDuration"/> (re-aiming one already running rather than starting over).
    /// </summary>
    protected void ApplyAutoSizeTarget(Vector2 target)
    {
        if (AutoSizeDuration <= 0)
        {
            if (autoSizeTransform != null)
            {
                RemoveTransform(autoSizeTransform);
                autoSizeTransform = null;
            }

            if (base.Size != target)
                base.Size = target;

            return;
        }

        if (autoSizeTransform != null && autoSizeTarget == target)
            return;

        if (autoSizeTransform == null && base.Size == target)
            return;

        if (autoSizeTransform != null)
            RemoveTransform(autoSizeTransform);

        autoSizeTarget = target;
        double startTime = Clock.CurrentTime;

        autoSizeTransform = new AutoSizeTransform
        {
            StartValue = base.Size,
            EndValue = target,
            Easing = AutoSizeEasing,
            StartTime = startTime,
            EndTime = startTime + AutoSizeDuration
        };

        AddTransform(autoSizeTransform);
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

    protected override void LoadComplete()
    {
        base.LoadComplete();

        foreach (var child in children)
            child.CompleteLoad();
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
    /// Removes all children that match the conditions defined by the specified predicate, disposing them.
    /// </summary>
    /// <param name="match">The predicate selecting children to remove.</param>
    /// <param name="dispose">
    /// Whether the removed children should be disposed. See <see cref="Remove(Drawable, bool)"/>.
    /// </param>
    /// <returns>The number of children removed from the container.</returns>
    public virtual int RemoveAll(Predicate<Drawable> match, bool dispose = true)
    {
        if (Content != this)
            return Content.RemoveAll(match, dispose);

        var toRemove = children.Where(match.Invoke).ToList();

        foreach (var child in toRemove)
        {
            RemoveInternal(child, dispose);
        }

        return toRemove.Count;
    }

    /// <summary>
    /// Removes a collection of drawables from this container, disposing them.
    /// </summary>
    /// <param name="drawables">The children to remove.</param>
    /// <param name="dispose">
    /// Whether the removed children should be disposed. See <see cref="Remove(Drawable, bool)"/>.
    /// </param>
    public virtual void RemoveRange(IEnumerable<Drawable> drawables, bool dispose = true)
    {
        if (Content != this)
        {
            Content.RemoveRange(drawables, dispose);
            return;
        }

        foreach (var drawable in drawables.ToList())
        {
            RemoveInternal(drawable, dispose);
        }
    }

    #endregion

    /// <summary>
    /// Disposes this container and, recursively, everything below it.
    /// </summary>
    /// <remarks>
    /// Children are detached and routed through the same removal-triggered disposal as
    /// <see cref="Remove(Drawable, bool)"/>, so a child that opted out of it
    /// (<see cref="Drawable.DisposeOnRemoval"/>) survives its parent — which is what lets an in-use
    /// pooled drawable go back to its pool when the screen holding it is torn down, instead of being
    /// destroyed while the pool still expects it back.
    /// </remarks>
    protected override void Dispose(bool isDisposing)
    {
        if (IsDisposed)
            return;

        ClearInternal();

        base.Dispose(isDisposing);
    }
}
