// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A lowest level of the component hierarchy. All drawable components should be inherited from this class.
/// </summary>
public class Drawable
{
    public Container Parent { get; internal set; }

    public bool IsHovered { get; private set; }
    public bool IsDragged { get; private set; }
    internal bool IsLoaded { get; private set; }

    private Anchor anchor = Anchor.Centre;
    private Anchor origin = Anchor.TopLeft;
    private Vector2 position = Vector2.Zero;
    private Vector2 size = Vector2.Zero;
    private Vector2 scale = Vector2.One;
    private Axes relativeSizeAxes = Axes.None;
    private Axes relativePositionAxes = Axes.None;
    private Color color = Color.White;
    private float alpha = 1f;
    private MarginPadding margin;
    private MarginPadding padding;
    private float depth;

    /// <summary>
    /// An invalidation flag representing which aspects of the drawable need to be recomputed.
    /// </summary>
    protected InvalidationFlags Invalidation = InvalidationFlags.All;

    public Anchor Anchor
    {
        get => anchor;
        set
        {
            if (anchor == value) return;
            anchor = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Anchor Origin
    {
        get => origin;
        set
        {
            if (origin == value) return;
            origin = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Vector2 Position
    {
        get => position;
        set
        {
            if (position == value) return;
            position = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Vector2 Size
    {
        get => size;
        set
        {
            if (size == value) return;
            size = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Vector2 Scale
    {
        get => scale;
        set
        {
            if (scale == value) return;
            scale = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Axes RelativeSizeAxes
    {
        get => relativeSizeAxes;
        set
        {
            if (relativeSizeAxes == value) return;
            relativeSizeAxes = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Axes RelativePositionAxes
    {
        get => relativePositionAxes;
        set
        {
            if (relativePositionAxes == value) return;
            relativePositionAxes = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Color Color
    {
        get => color;
        set
        {
            if (color == value) return;
            color = value;
            Invalidate(InvalidationFlags.Colour);
        }
    }

    public float Alpha
    {
        get => alpha;
        set
        {
            if (Math.Abs(alpha - value) < 0.0001f) return;
            alpha = Math.Clamp(value, 0f, 1f);
            Invalidate(InvalidationFlags.Colour);
        }
    }

    public float Depth
    {
        get => depth;
        set
        {
            if (Math.Abs(depth - value) < 0.0001f) return;
            depth = value;
            // Re-sort children in parent required
            Parent?.Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public MarginPadding Margin
    {
        get => margin;
        set
        {
            if (margin.Equals(value)) return;
            margin = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public MarginPadding Padding
    {
        get => padding;
        set
        {
            if (padding.Equals(value)) return;
            padding = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Texture Texture { get; set; }

    // Caches for computed values
    public RectangleF DrawRectangle { get; private set; }
    public Matrix4x4 ModelMatrix = Matrix4x4.Identity;

    public Drawable()
    {
        Texture = Texture.WhitePixel;
    }

    public virtual void Update()
    {
        if (Invalidation == InvalidationFlags.None)
            return;

        if ((Invalidation & InvalidationFlags.DrawInfo) != 0)
        {
            UpdateTransforms();
        }

        Invalidation = InvalidationFlags.None;
    }

    protected virtual void UpdateTransforms()
    {
        // Determine the size of our parent's drawable area.
        Vector2 parentSize = Parent?.ChildSize ?? Vector2.Zero;

        // Calculate our final draw size based on our local Size and relative axes.
        Vector2 drawSize = Size;
        if ((RelativeSizeAxes & Axes.X) != 0)
            drawSize.X *= parentSize.X;
        if ((RelativeSizeAxes & Axes.Y) != 0)
            drawSize.Y *= parentSize.Y;

        // Calculate margin, padding and position based on relative axes settings.
        Vector2 relativePosition = Position;
        if ((RelativePositionAxes & Axes.X) != 0)
            relativePosition.X *= parentSize.X;
        if ((RelativePositionAxes & Axes.Y) != 0)
            relativePosition.Y *= parentSize.Y;

        MarginPadding relativeMargin = Margin;
        // Margin relativity is tied to size relativity.
        if ((RelativeSizeAxes & Axes.X) != 0)
        {
            relativeMargin.Left *= parentSize.X;
            relativeMargin.Right *= parentSize.X;
        }
        if ((RelativeSizeAxes & Axes.Y) != 0)
        {
            relativeMargin.Top *= parentSize.Y;
            relativeMargin.Bottom *= parentSize.Y;
        }

        // Determine the space we are being positioned within.
        Vector2 positioningSpace = Parent?.ChildSize ?? drawSize;
        Vector2 anchorPosition = GetAnchorOriginVector(Anchor) * positioningSpace;
        Vector2 originOffset = GetAnchorOriginVector(Origin) * drawSize;

        // Apply the now-relative margin to get the margin offset.
        var marginOffset = new Vector2(relativeMargin.Left - relativeMargin.Right, relativeMargin.Top - relativeMargin.Bottom);
        Vector2 localPosition = anchorPosition + relativePosition + marginOffset - originOffset;

        // Create the local transformation matrix.
        // For row-major matrices (like System.Numerics), the correct order is Scale then Translate.
        var scaleMatrix = Matrix4x4.CreateScale(new Vector3(drawSize.X * Scale.X, drawSize.Y * Scale.Y, 1));
        var translationMatrix = Matrix4x4.CreateTranslation(new Vector3(localPosition.X, localPosition.Y, Depth));
        var localMatrix = scaleMatrix * translationMatrix;

        // Combine with parent's matrix to get the world matrix.
        // For row-major matrices, the world matrix is local * parent.
        ModelMatrix = localMatrix;
        if (Parent != null)
            ModelMatrix = localMatrix * Parent.ModelMatrix;

        ModelMatrix = scaleMatrix * translationMatrix;

        // Calculate the screen-space bounding box (DrawRectangle) by transforming the corners
        // of a unit quad (which the renderer uses) by the final ModelMatrix to determine the final screen coordinates and size
        var topLeft = Vector4.Transform(new Vector4(0, 0, 0, 1), ModelMatrix);
        var topRight = Vector4.Transform(new Vector4(1, 0, 0, 1), ModelMatrix);
        var bottomLeft = Vector4.Transform(new Vector4(0, 1, 0, 1), ModelMatrix);
        var bottomRight = Vector4.Transform(new Vector4(1, 1, 0, 1), ModelMatrix);

        float minX = Math.Min(topLeft.X, Math.Min(topRight.X, Math.Min(bottomLeft.X, bottomRight.X)));
        float minY = Math.Min(topLeft.Y, Math.Min(topRight.Y, Math.Min(bottomLeft.Y, bottomRight.Y)));
        float maxX = Math.Max(topLeft.X, Math.Max(topRight.X, Math.Max(bottomLeft.X, bottomRight.X)));
        float maxY = Math.Max(topLeft.Y, Math.Max(topRight.Y, Math.Max(bottomLeft.Y, bottomRight.Y)));

        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    public bool Contains(Vector2 screenSpacePos) => DrawRectangle.Contains(screenSpacePos);

    public static Vector2 GetAnchorOriginVector(Anchor anchor)
    {
        switch (anchor)
        {
            case Anchor.TopLeft: return new Vector2(0.0f, 0.0f);
            case Anchor.TopCentre: return new Vector2(0.5f, 0.0f);
            case Anchor.TopRight: return new Vector2(1.0f, 0.0f);
            case Anchor.CentreLeft: return new Vector2(0.0f, 0.5f);
            case Anchor.Centre: return new Vector2(0.5f, 0.5f);
            case Anchor.CentreRight: return new Vector2(1.0f, 0.5f);
            case Anchor.BottomLeft: return new Vector2(0.0f, 1.0f);
            case Anchor.BottomCentre: return new Vector2(0.5f, 1.0f);
            case Anchor.BottomRight: return new Vector2(1.0f, 1.0f);
            default: return Vector2.Zero;
        }
    }

    /// <summary>
    /// Called to perform loading tasks to load required resources and dependencies.
    /// Called once before the first update.
    /// This method is recursively called down the drawable hierarchy.
    /// </summary>
    public virtual void Load()
    {
        IsLoaded = true;
    }

    /// <summary>
    /// Called after the drawable and all its children have been loaded.
    /// This method is recursively called down the drawable hierarchy.
    /// </summary>
    public virtual void LoadComplete()
    {
    }

    public virtual void Draw(IRenderer renderer)
    {
        renderer.DrawDrawable(this);
    }

    /// <summary>
    /// Marks all or part of this drawable as requiring re-computation.
    /// </summary>
    /// <param name="flags">An <see cref="InvalidationFlags"/> flag representing which aspects of the drawable need to be recomputed.</param>
    public virtual void Invalidate(InvalidationFlags flags = InvalidationFlags.All)
    {
        if ((Invalidation & flags) == flags)
            return; // Already invalidated for these flags.

        Invalidation |= flags;

        if ((flags & InvalidationFlags.DrawInfo) != 0)
            Parent?.Invalidate(InvalidationFlags.DrawInfo);
    }

    #region Event Handlers

    public virtual bool OnMouseDown(MouseButtonEvent e)
    {
        if (e.Clicks >= 3)
            return OnTripleClick(e);
        if (e.Clicks == 2)
            return OnDoubleClick(e);
        if (e.Clicks == 1)
            OnClick(e);

        // Potential start of a drag operation.
        if (e.Button == MouseButton.Left)
        {
            IsDragged = true;
            OnDragStart(e);
        }

        return false;
    }

    public virtual bool OnMouseUp(MouseButtonEvent e)
    {
        if (IsDragged)
        {
            IsDragged = false;
            return OnDragEnd(e);
        }

        return false;
    }

    public virtual bool OnClick(MouseButtonEvent e) => false;
    public virtual bool OnDoubleClick(MouseButtonEvent e) => false;
    public virtual bool OnTripleClick(MouseButtonEvent e) => false;

    public virtual bool OnMouseMove(MouseEvent e)
    {
        if (IsDragged)
            return OnDrag(e);

        if (!IsHovered && Contains(e.ScreenSpaceMousePosition))
        {
            IsHovered = true;
            return OnHover(e);
        }

        if(IsHovered && !Contains(e.ScreenSpaceMousePosition))
        {
            IsHovered = false;
            return OnHoverLost(e);
        }

        return false;
    }

    public virtual bool OnHover(MouseEvent e) => false;
    public virtual bool OnHoverLost(MouseEvent e) => false;

    public virtual bool OnDragStart(MouseButtonEvent e) => false;
    public virtual bool OnDrag(MouseEvent e) => false;
    public virtual bool OnDragEnd(MouseButtonEvent e) => false;

    public virtual bool OnScroll(ScrollEvent e) => false;

    public virtual bool OnKeyDown(KeyEvent e) => false;
    public virtual bool OnKeyUp(KeyEvent e) => false;

    #endregion
}

/// <summary>
/// Represents the state of a drawable that requires re-computation.
/// </summary>
[Flags]
public enum InvalidationFlags
{
    /// <summary>
    /// The drawable is in a clean state and requires no updates.
    /// </summary>
    None = 0,

    /// <summary>
    /// The drawable's position, size, or other spatial properties have changed.
    /// This requires recalculating the model matrix and draw rectangle.
    /// </summary>
    DrawInfo = 1 << 0,

    /// <summary>
    /// The color of the drawable has changed.
    /// </summary>
    Colour = 1 << 1,

    /// <summary>
    /// A catch-all for all invalidation types.
    /// </summary>
    All = ~None
}
