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
        public new RectangleF DrawRectangle { get; private set; }
        public float Radius { get; private set; }

        public float ShearX { get; private set; }
        public Vector2 DrawSize { get; private set; }
        public Matrix4x4 ModelMatrix { get; private set; }

        public override void ApplyState(Drawable source)
        {
            base.ApplyState(source);
            DrawRectangle = source.DrawRectangle;
            ShearX = source.Shear.X;
            DrawSize = source.DrawSize;
            ModelMatrix = source.ModelMatrix;
            Radius = Math.Min(DrawSize.X, DrawSize.Y) / 2f;
        }

        public override void Draw(IRenderer renderer)
        {
            if (DrawAlpha <= 0) return;

            Vector3 topLeft = Vector3.Transform(new Vector3(0, 0, 0), ModelMatrix);
            Vector3 topRight = Vector3.Transform(new Vector3(1, 0, 0), ModelMatrix);
            Vector3 bottomLeft = Vector3.Transform(new Vector3(0, 1, 0), ModelMatrix);

            Vector2 screenHalfSize = new Vector2(
                Vector2.Distance(new Vector2(topLeft.X, topLeft.Y), new Vector2(topRight.X, topRight.Y)) / 2f,
                Math.Abs(bottomLeft.Y - topLeft.Y) / 2f
            );

            Vector3 screenCenter3 = Vector3.Transform(new Vector3(0.5f, 0.5f, 0), ModelMatrix);
            Vector2 screenCenter = new Vector2(screenCenter3.X, screenCenter3.Y);

            float screenRadius = Math.Min(screenHalfSize.X, screenHalfSize.Y);

            renderer.PushMask(screenCenter, screenHalfSize, ShearX, screenRadius);

            base.Draw(renderer);

            renderer.PopMask(screenCenter, screenHalfSize, ShearX, screenRadius, 0f, Colors.Color.Transparent, Vertices);
        }
    }
}
