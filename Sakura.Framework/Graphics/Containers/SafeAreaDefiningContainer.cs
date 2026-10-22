// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A screen, real or simulated, whose content is laid out in its safe area: clear of notches, cutouts,
/// rounded corners and the system bars.
/// <para>
/// <see cref="App"/> puts all of its content in one of these, so a whole app is safe by default and game
/// code never handles inset sizes. A drawable that should still reach the edge of the screen, such as a
/// background, sets <see cref="Drawable.Bleed"/>, and a <see cref="SafeAreaContainer"/> makes content
/// safe again inside something that bleeds.
/// </para>
/// <para>
/// Nested inside another one, this container passes the outer safe area through, unless
/// <see cref="OverrideSafeArea"/> is set: then it is a simulated screen of its own, its insets are those,
/// and drawables below it bleed to its edges. That's how visual tests show a notch on desktop.
/// </para>
/// </summary>
[Cached]
public partial class SafeAreaDefiningContainer : Container
{
    /// <summary>
    /// How far this container pads its content to keep it safe, in its own units.
    /// </summary>
    public Reactive<MarginPadding> SafeAreaPadding { get; } = new Reactive<MarginPadding>(new MarginPadding());

    private MarginPadding? overrideSafeArea;

    /// <summary>
    /// When set, these insets (in this container's own units) are used instead of the window's or the outer
    /// safe area, and this container becomes the screen for everything below it. Set to null to resume following them.
    /// </summary>
    public MarginPadding? OverrideSafeArea
    {
        get => overrideSafeArea;
        set
        {
            if (Nullable.Equals(overrideSafeArea, value))
                return;

            overrideSafeArea = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// The screen, as far as bleeding below this container goes, in screen space.
    /// </summary>
    internal RectangleF ScreenSpaceBounds { get; private set; }

    /// <summary>
    /// The part of <see cref="ScreenSpaceBounds"/> where content is safe, in screen space.
    /// </summary>
    internal RectangleF ScreenSpaceSafeArea { get; private set; }

    /// <summary>
    /// Whether the safe area has been worked out yet (it is at this container's first layout).
    /// </summary>
    internal bool HasSafeArea { get; private set; }

    private IWindow? window;
    private MarginPadding windowInsets;

    public SafeAreaDefiningContainer()
    {
        RelativeSizeAxes = Axes.Both;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Resolved here rather than at load: the app caches its window after its own children have loaded.
        window = Dependencies.TryGet<IWindow>();
    }

    public override void Update()
    {
        // Poll rather than subscribe: window reactives fire on the main thread, while the scene graph lives
        // on the update thread. Invalidating before the base update lays this container and everything below
        // it out again this frame.
        if (window != null)
        {
            var insets = window.SafeAreaPadding.Value;

            if (!insets.Equals(windowInsets))
            {
                windowInsets = insets;

                if (overrideSafeArea == null && FindFor(this) == null)
                    Invalidate(InvalidationFlags.DrawInfo);
            }
        }

        base.Update();
    }

    protected internal override void UpdateTransforms()
    {
        base.UpdateTransforms();

        RectangleF bounds;
        RectangleF safe;

        var outer = FindFor(this);

        if (overrideSafeArea is MarginPadding insets)
        {
            bounds = DrawRectangle;
            safe = deflate(bounds, insets, screenScale(this));
        }
        else if (outer is { HasSafeArea: true })
        {
            bounds = outer.ScreenSpaceBounds;
            safe = outer.ScreenSpaceSafeArea;
        }
        else
        {
            // The outermost definition covers the window, whose insets are already in screen units.
            bounds = DrawRectangle;
            safe = deflate(bounds, windowInsets, Vector2.One);
        }

        bool changed = !HasSafeArea || bounds != ScreenSpaceBounds || safe != ScreenSpaceSafeArea;

        ScreenSpaceBounds = bounds;
        ScreenSpaceSafeArea = safe;
        HasSafeArea = true;

        var padding = PaddingWithin(this, safe);

        if (!padding.Equals(Padding))
        {
            Padding = padding;
            SafeAreaPadding.Value = padding;
            changed = true;
        }

        // Everything below may pad or bleed differently now, even where its own geometry didn't change.
        if (changed)
        {
            var children = InternalChildren;
            for (int i = 0; i < children.Count; i++)
                children[i].Invalidate(InvalidationFlags.DrawInfo, false);
        }
    }

    /// <summary>
    /// The nearest definition above <paramref name="drawable"/>, or null when there is none.
    /// </summary>
    internal static SafeAreaDefiningContainer? FindFor(Drawable drawable)
    {
        for (Drawable? p = drawable.Parent; p != null; p = p.Parent)
        {
            if (p is SafeAreaDefiningContainer definition)
                return definition;
        }

        return null;
    }

    /// <summary>
    /// How far <paramref name="drawable"/> has to pad its content, in its own units, to keep it inside
    /// <paramref name="safe"/>: however much of the drawable lies outside it, edge by edge.
    /// </summary>
    internal static MarginPadding PaddingWithin(Drawable drawable, RectangleF safe)
    {
        RectangleF rect = drawable.DrawRectangle;
        Vector2 size = drawable.AppliedDrawSize;

        if (rect.Width <= 0 || rect.Height <= 0 || size.X <= 0 || size.Y <= 0)
            return new MarginPadding();

        float toLocalX = size.X / rect.Width;
        float toLocalY = size.Y / rect.Height;

        return new MarginPadding
        {
            Top = overlap(safe.Y - rect.Y, rect.Height) * toLocalY,
            Left = overlap(safe.X - rect.X, rect.Width) * toLocalX,
            Bottom = overlap(rect.Y + rect.Height - (safe.Y + safe.Height), rect.Height) * toLocalY,
            Right = overlap(rect.X + rect.Width - (safe.X + safe.Width), rect.Width) * toLocalX,
        };

        // A drawable laid out flush against the boundary under a scale lands a rounding error either side
        // of it; that isn't an overlap.
        static float overlap(float distance, float extent) => distance < 0.01f ? 0 : Math.Min(distance, extent);
    }

    /// <summary>
    /// Screen pixels per unit of <paramref name="drawable"/>'s own space.
    /// </summary>
    private static Vector2 screenScale(Drawable drawable)
    {
        Vector2 size = drawable.AppliedDrawSize;
        RectangleF rect = drawable.DrawRectangle;

        return new Vector2(size.X > 0 ? rect.Width / size.X : 1, size.Y > 0 ? rect.Height / size.Y : 1);
    }

    private static RectangleF deflate(RectangleF rect, MarginPadding insets, Vector2 scale)
    {
        float left = Math.Max(0, insets.Left) * scale.X;
        float top = Math.Max(0, insets.Top) * scale.Y;
        float width = Math.Max(0, rect.Width - left - Math.Max(0, insets.Right) * scale.X);
        float height = Math.Max(0, rect.Height - top - Math.Max(0, insets.Bottom) * scale.Y);

        return new RectangleF(rect.X + left, rect.Y + top, width, height);
    }
}
