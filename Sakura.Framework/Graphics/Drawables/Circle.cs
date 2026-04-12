// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A circle drawable that uses masking to draw a circle shape.
/// </summary>
public class Circle : Drawable
{
    protected override DrawNode CreateDrawNode() => new CircleDrawNode();

    public class CircleDrawNode : DrawNode
    {
        public RectangleF DrawRectangle { get; private set; }
        public float Radius { get; private set; }

        public override void ApplyState(Drawable source)
        {
            base.ApplyState(source);
            DrawRectangle = source.DrawRectangle;
            Radius = Math.Min(DrawRectangle.Width, DrawRectangle.Height) / 2f;
        }

        public override void Draw(IRenderer renderer)
        {
            if (DrawAlpha <= 0) return;

            renderer.PushMask(DrawRectangle, Radius);
            base.Draw(renderer);
            renderer.PopMask(DrawRectangle, Radius, 0f, Colors.Color.Transparent);
        }
    }
}
