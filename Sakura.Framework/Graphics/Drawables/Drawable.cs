// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A lowest level of the component hierarchy. All drawable components should be inherited from this class.
/// </summary>
public class Drawable
{
    public Container Parent { get; internal set; }

    public Anchor Anchor { get; set; } = Anchor.Centre;
    public Anchor Origin { get; set; } = Anchor.TopLeft;

    public Vector2 Position { get; set; } = Vector2.Zero;
    public Vector2 Size { get; set; } = Vector2.Zero;

    public Axes RelativeSizeAxes { get; set; } = Axes.None;

    public Color Color { get; set; } = Color.White;
    public float Alpha { get; set; } = 1f;

    public Texture Texture { get; set; }

    public MarginPadding Margin { get; set; }
    public MarginPadding Padding { get; set; }

    /// <summary>
    /// The depth at which this drawable should be drawn.
    /// A higher value means the drawable will be drawn in front of others.
    /// </summary>
    public float Depth { get; set; }

    // Caches for computed values
    public RectangleF DrawRectangle { get; private set; }
    public Matrix4x4 ModelMatrix = Matrix4x4.Identity;

    public Drawable()
    {
        Texture = Texture.WhitePixel;
    }

    public virtual void Update()
    {
        UpdateTransforms();
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
        MarginPadding relativeMargin = Margin;

        if ((RelativeSizeAxes & Axes.X) != 0)
        {
            relativePosition.X *= parentSize.X;
            relativeMargin.Left *= parentSize.X;
            relativeMargin.Right *= parentSize.X;
        }
        if ((RelativeSizeAxes & Axes.Y) != 0)
        {
            relativePosition.Y *= parentSize.Y;
            relativeMargin.Top *= parentSize.Y;
            relativeMargin.Bottom *= parentSize.Y;
        }

        // Determine the space we are being positioned within.
        Vector2 positioningSpace = Parent?.ChildSize ?? drawSize;
        Vector2 anchorPosition = GetAnchorOriginVector(Anchor) * positioningSpace;
        Vector2 originOffset = GetAnchorOriginVector(Origin) * drawSize;

        // Apply the now-relative margin to get the margin offset.
        var marginOffset = new Vector2(relativeMargin.Left - relativeMargin.Right, relativeMargin.Top - relativeMargin.Bottom);

        // Calculate the final position using the now-relative position and margin.
        Vector2 finalDrawPosition = anchorPosition + relativePosition + marginOffset - originOffset;

        // Create the transformation matrices.
        var scaleMatrix = Matrix4x4.CreateScale(new Vector3(drawSize.X, drawSize.Y, 1));
        // Use the Depth property for the Z-coordinate of the translation.
        var translationMatrix = Matrix4x4.CreateTranslation(new Vector3(finalDrawPosition.X, finalDrawPosition.Y, Depth));

        ModelMatrix = scaleMatrix * translationMatrix;

        // Update the final absolute draw rectangle.
        DrawRectangle = new RectangleF(finalDrawPosition, drawSize);
    }

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

    public virtual void Draw(IRenderer renderer)
    {
        renderer.DrawDrawable(this);
    }
}

[Flags]
public enum Invalidation
{
    None = 0,
    DrawInfo = 1 << 0,
    Colour = 1 << 1,

    all = ~None
}
