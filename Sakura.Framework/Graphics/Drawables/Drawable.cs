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

    public virtual void Update()
    {
        UpdateTransforms();
    }

    protected virtual void UpdateTransforms()
    {
        Vector2 parentSize = Parent?.ChildSize ?? new Vector2(800, 600); // Fallback to a default size

        Vector2 relativeSize = Vector2.Zero;
        if ((RelativeSizeAxes & Axes.X) != 0) relativeSize.X = parentSize.X;
        if ((RelativeSizeAxes & Axes.Y) != 0) relativeSize.Y = parentSize.Y;

        var drawSize = Size * (Vector2.One - relativeSize) + Size * relativeSize;

        Vector2 anchorOffset = GetAnchorOriginVector(Anchor) * parentSize;
        Vector2 originOffset = GetAnchorOriginVector(Origin) * drawSize;

        Vector2 position = Position + anchorOffset - originOffset;

        DrawRectangle = new RectangleF(position.X, position.Y, drawSize.X, drawSize.Y);

        // Simple 2D model matrix (translation and scale)
        ModelMatrix = Matrix4x4.CreateScale(drawSize.X, drawSize.Y, 1) * Matrix4x4.CreateTranslation(position.X, position.Y, 0);
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
            default: throw new ArgumentOutOfRangeException(nameof(anchor), anchor, null);
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
