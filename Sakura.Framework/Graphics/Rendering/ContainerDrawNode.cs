// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Rendering;

public class ContainerDrawNode : DrawNode
{
    public long TopologyInvalidationID { get; internal set; }

    public List<DrawNode> Children { get; } = new();
    public bool Masking { get; private set; }
    public float CornerRadius { get; private set; }
    public float BorderThickness { get; private set; }
    public Color BorderColor { get; private set; }

    public float ShearX { get; private set; }
    public Vector2 DrawSize { get; private set; }
    public Matrix4x4 ModelMatrix { get; private set; }

    public override void ApplyState(Drawable source)
    {
        base.ApplyState(source);
        var container = (Container)source;
        Masking = container.Masking;
        CornerRadius = container.CornerRadius;
        BorderThickness = container.BorderThickness;
        BorderColor = container.BorderColor;
        ShearX = container.Shear.X;
        DrawSize = container.DrawSize;
        ModelMatrix = container.ModelMatrix;
    }

    public override void PrepareForDraw(double lastUpdateTime, double currentUpdateTime, double drawTime)
    {
        base.PrepareForDraw(lastUpdateTime, currentUpdateTime, drawTime);

        foreach (var child in Children)
        {
            child.PrepareForDraw(lastUpdateTime, currentUpdateTime, drawTime);
        }
    }

    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0) return;

        Vector3 screenCenter3 = Vector3.Transform(new Vector3(0.5f, 0.5f, 0), ModelMatrix);
        Vector2 screenCenter = new Vector2(screenCenter3.X, screenCenter3.Y);

        Vector3 topLeft = Vector3.Transform(new Vector3(0, 0, 0), ModelMatrix);
        Vector3 topRight = Vector3.Transform(new Vector3(1, 0, 0), ModelMatrix);
        Vector3 bottomLeft = Vector3.Transform(new Vector3(0, 1, 0), ModelMatrix);

        Vector2 screenHalfSize = new Vector2(
            Vector2.Distance(new Vector2(topLeft.X, topLeft.Y), new Vector2(topRight.X, topRight.Y)) / 2f,
            Math.Abs(bottomLeft.Y - topLeft.Y) / 2f
        );

        if (Masking)
            renderer.PushMask(screenCenter, screenHalfSize, ShearX, CornerRadius);

        foreach (var child in Children)
        {
            if (Masking)
            {
                var cr = DrawRectangle;
                var dr = child.DrawRectangle;

                bool isVisible = dr.X <= cr.X + cr.Width &&
                                 dr.X + dr.Width >= cr.X &&
                                 dr.Y <= cr.Y + cr.Height &&
                                 dr.Y + dr.Height >= cr.Y;

                if (!isVisible)
                {
                    GlobalStatistics.Get<int>("Drawables", "Culled").Value++;
                    continue;
                }
            }

            child.Draw(renderer);
        }

        if (Masking)
            renderer.PopMask(screenCenter, screenHalfSize, ShearX, CornerRadius, BorderThickness, BorderColor, Vertices);
    }
}
