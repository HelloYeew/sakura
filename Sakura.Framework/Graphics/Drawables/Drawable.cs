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

    public Vector2 Position { get; set; }
    public Vector2 Size { get; set; } = new(100, 100);

    public Axes RelativeSizeAxes { get; set; } = Axes.None;

    public Color Color { get; set; } = Color.White;
    public float Alpha { get; set; } = 1f;

    public Texture Texture { get; set; }

    public MarginPadding Margin { get; set; }
    public MarginPadding Padding { get; set; }

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
        // Get the parent's size for anchor calculations. Fallback to a default if no parent.
        Vector2 parentSize = Parent?.ChildSize ?? new Vector2(800, 600);

        // Note: If you have relative sizing, you would calculate the final drawSize here.
        // For this example, we'll just use the drawable's local Size.
        Vector2 drawSize = Size;

        if ((RelativeSizeAxes & Axes.X) != 0) drawSize.X *= parentSize.X;
        if ((RelativeSizeAxes & Axes.Y) != 0) drawSize.Y *= parentSize.Y;

        // 2. Calculate the anchor's position in world coordinates.
        // This finds the point within the parent that we are "anchored" to.
        Vector2 anchorPosition = GetAnchorOriginVector(Anchor) * parentSize;

        // 3. Calculate the origin's offset in local coordinates.
        // This finds the offset from the top-left of our drawable to its pivot point.
        Vector2 originOffset = GetAnchorOriginVector(Origin) * drawSize;

        // 4. Calculate the final position for the top-left corner of the drawable.
        // Final Position = (Parent's Anchor Point) + (Relative Position) - (Our Origin Offset)
        Vector2 finalDrawPosition = anchorPosition + Position - originOffset;

        // 5. Create the transformation matrices.
        // The order is important: scale first, then translate.
        var scaleMatrix = Matrix4x4.CreateScale(new Vector3(drawSize.X, drawSize.Y, 1));
        var translationMatrix = Matrix4x4.CreateTranslation(new Vector3(finalDrawPosition.X, finalDrawPosition.Y, 0));

        ModelMatrix = scaleMatrix * translationMatrix;
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
