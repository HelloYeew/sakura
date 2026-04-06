// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Cursor;

public class CursorContainer : Container
{
    public Drawable ActiveCursor { get; protected set; }

    public CursorContainer()
    {
        Depth = float.MaxValue;
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
        Add(ActiveCursor = CreateCursor());
    }

    protected virtual Drawable CreateCursor() => new DefaultCursor();

    public override bool OnMouseMove(MouseEvent e)
    {
        Vector2 localPosition;

        // Invert the ModelMatrix to map screen space back to normalized local space (0..1)
        if (Matrix4x4.Invert(ModelMatrix, out var inverse))
        {
            var localNormalized = Vector4.Transform(
                new Vector4(e.MouseState.Position.X, e.MouseState.Position.Y, 0, 1),
                inverse
            );

            // Multiply by DrawSize to convert the 0..1 ratio into actual local pixels
            localPosition = new Vector2(localNormalized.X * DrawSize.X, localNormalized.Y * DrawSize.Y);
        }
        else
        {
            // Fallback for non-invertible matrices
            localPosition = e.MouseState.Position - new Vector2(DrawRectangle.X, DrawRectangle.Y);
        }

        ActiveCursor.Position = localPosition;
        return false;
    }

    private class DefaultCursor : Container
    {
        private readonly IconSprite iconSprite;

        public DefaultCursor()
        {
            Size = new Vector2(25);

            Add(iconSprite = new IconSprite()
            {
                Origin = Anchor.TopLeft,
                Anchor = Anchor.TopLeft,
                Color = Color.DeepPink,
                Icon = IconUsage.ArrowSelectorTool,
                IconSize = 25f,
                Position = new Vector2(-6, -5) // Just adjust position to make left-top corner the "tip" of the cursor
            });
        }

        public override bool OnClick(MouseButtonEvent e)
        {
            iconSprite.FlashColour(Color.White, 300, Easing.OutQuint);
            return base.OnClick(e);
        }
    }
}
