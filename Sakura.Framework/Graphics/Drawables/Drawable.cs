// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
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

    protected readonly Vertex[] Vertices = new Vertex[6];

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

    public Texture? Texture { get; set; }

    // Caches for computed values
    public RectangleF DrawRectangle { get; private set; }
    public Vector2 DrawSize { get; private set; }
    public Matrix4x4 ModelMatrix = Matrix4x4.Identity;

    public Drawable()
    {
        Texture = Texture.WhitePixel;
    }

    public virtual void Update()
    {
        if (Invalidation == InvalidationFlags.None)
            return;

        if ((Invalidation & (InvalidationFlags.DrawInfo | InvalidationFlags.Colour)) != 0)
        {
            UpdateTransforms();
        }

        Invalidation = InvalidationFlags.None;
    }

    protected virtual void UpdateTransforms()
    {
        Matrix4x4 localMatrix;
        Vector2 finalDrawSize;

        if (Parent == null)
        {
            // This is the root drawable.

            // The root's transform is absolute, not relative. It establishes the world space.
            // It scales a 1x1 quad up to its own pixel size.
            finalDrawSize = this.Size;
            Vector2 originOffset = GetAnchorOriginVector(Origin) * finalDrawSize * Scale;

            // The root's position is typically (0,0), but we'll respect the Position property.
            // No anchor is applied as there's no parent to be anchored to.
            Vector2 finalPosition = Position - originOffset;

            var scaleMatrix = Matrix4x4.CreateScale(new Vector3(finalDrawSize.X * Scale.X, finalDrawSize.Y * Scale.Y, 1));
            var translationMatrix = Matrix4x4.CreateTranslation(new Vector3(finalPosition.X, finalPosition.Y, Depth));

            localMatrix = scaleMatrix * translationMatrix;
            ModelMatrix = localMatrix; // No parent matrix to multiply with.
        }
        else
        {
            // This is a child drawable

            // Final pixel size of the parent's content area.
            Vector2 parentPixelSize = Parent.ChildSize;

            // prevent division by zero for drawables with no area.
            if (parentPixelSize.X == 0) parentPixelSize.X = 1;
            if (parentPixelSize.Y == 0) parentPixelSize.Y = 1;

            // Calculate scale relative to parent
            Vector2 localScale = Size;
            if ((RelativeSizeAxes & Axes.X) == 0)
                localScale.X /= parentPixelSize.X;
            if ((RelativeSizeAxes & Axes.Y) == 0)
                localScale.Y /= parentPixelSize.Y;

            // Calculate position relative to parent
            Vector2 localPosition = Position;
            if ((RelativePositionAxes & Axes.X) == 0)
                localPosition.X /= parentPixelSize.X;
            if ((RelativePositionAxes & Axes.Y) == 0)
                localPosition.Y /= parentPixelSize.Y;

            MarginPadding localMargin = Margin;
            localMargin.Left /= parentPixelSize.X;
            localMargin.Right /= parentPixelSize.X;
            localMargin.Top /= parentPixelSize.Y;
            localMargin.Bottom /= parentPixelSize.Y;

            // Anchor and Origin are naturally relative, so they work correctly in this 0-1 space.
            Vector2 anchorPosition = GetAnchorOriginVector(Anchor);
            Vector2 originOffset = GetAnchorOriginVector(Origin) * localScale * Scale;
            var marginOffset = new Vector2(localMargin.Left - localMargin.Right, localMargin.Top - localMargin.Bottom);

            Vector2 finalLocalPosition = anchorPosition + localPosition + marginOffset - originOffset;

            // Create local matrix
            // This matrix transforms a unit (1x1) quad into our desired size and position
            // within the parent's 1x1 local space.
            var scaleMatrix = Matrix4x4.CreateScale(new Vector3(localScale.X * Scale.X, localScale.Y * Scale.Y, 1));
            var translationMatrix = Matrix4x4.CreateTranslation(new Vector3(finalLocalPosition.X, finalLocalPosition.Y, Depth));
            localMatrix = scaleMatrix * translationMatrix;

            ModelMatrix = localMatrix * Parent.ModelMatrix;

            // Cache final pixel size
            finalDrawSize = Size;
            if ((RelativeSizeAxes & Axes.X) != 0) finalDrawSize.X *= parentPixelSize.X;
            if ((RelativeSizeAxes & Axes.Y) != 0) finalDrawSize.Y *= parentPixelSize.Y;
        }

        DrawSize = finalDrawSize;

        GenerateVertices();
    }

    protected virtual void GenerateVertices()
    {
        var calculatedColor = new System.Numerics.Vector4(Color.R / 255f, Color.G / 255f, Color.B / 255f, Alpha);

        var vTopLeft = Vector4.Transform(new Vector4(0, 0, 0, 1), ModelMatrix);
        var vTopRight = Vector4.Transform(new Vector4(1, 0, 0, 1), ModelMatrix);
        var vBottomLeft = Vector4.Transform(new Vector4(0, 1, 0, 1), ModelMatrix);
        var vBottomRight = Vector4.Transform(new Vector4(1, 1, 0, 1), ModelMatrix);

        var topLeft = new Vertex { Position = new Vector2(vTopLeft.X, vTopLeft.Y), TexCoords = new Vector2(0, 0), Color = calculatedColor };
        var topRight = new Vertex { Position = new Vector2(vTopRight.X, vTopRight.Y), TexCoords = new Vector2(1, 0), Color = calculatedColor };
        var bottomLeft = new Vertex { Position = new Vector2(vBottomLeft.X, vBottomLeft.Y), TexCoords = new Vector2(0, 1), Color = calculatedColor };
        var bottomRight = new Vertex { Position = new Vector2(vBottomRight.X, vBottomRight.Y), TexCoords = new Vector2(1, 1), Color = calculatedColor };

        // Triangle 1
        Vertices[0] = topLeft;
        Vertices[1] = topRight;
        Vertices[2] = bottomRight;

        // Triangle 2
        Vertices[3] = bottomRight;
        Vertices[4] = bottomLeft;
        Vertices[5] = topLeft;

        // Calculate DrawRectangle in screen space
        float minX = Math.Min(vTopLeft.X, Math.Min(vTopRight.X, Math.Min(vBottomLeft.X, vBottomRight.X)));
        float minY = Math.Min(vTopLeft.Y, Math.Min(vTopRight.Y, Math.Min(vBottomLeft.Y, vBottomRight.Y)));
        float maxX = Math.Max(vTopLeft.X, Math.Max(vTopRight.X, Math.Max(vBottomLeft.X, vBottomRight.X)));
        float maxY = Math.Max(vTopLeft.Y, Math.Max(vTopRight.Y, Math.Max(vBottomLeft.Y, vBottomRight.Y)));

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
        renderer.DrawVertices(Vertices, Texture ?? Texture.WhitePixel);
    }

    /// <summary>
    /// Marks all or part of this drawable as requiring re-computation.
    /// </summary>
    /// <param name="flags">An <see cref="InvalidationFlags"/> flag representing which aspects of the drawable need to be recomputed.</param>
    /// <param name="propagateToParent">Whether this invalidation should also invalidate this drawable's parent.</param>
    public virtual void Invalidate(InvalidationFlags flags = InvalidationFlags.All, bool propagateToParent = true)
    {
        if ((Invalidation & flags) == flags)
            return; // Already invalidated for these flags.

        Invalidation |= flags;

        if (propagateToParent && (flags & InvalidationFlags.DrawInfo) != 0)
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
