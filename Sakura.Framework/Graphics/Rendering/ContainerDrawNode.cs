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
    private static readonly GlobalStatistic<int> stat_culled = GlobalStatistics.Get<int>("Drawables", "Culled");

    public long TopologyInvalidationID { get; internal set; }

    /// <summary>
    /// The container's subtree-draw version this node's child list was generated against.
    /// When it still matches, the whole subtree generation is skipped.
    /// </summary>
    public long AppliedSubtreeVersion { get; internal set; } = -1;

    public List<DrawNode> Children { get; } = new();
    public bool Masking { get; private set; }
    public float CornerRadius { get; private set; }
    public float BorderThickness { get; private set; }
    public Color BorderColor { get; private set; }

    public float ShearX { get; private set; }
    public Vector2 DrawSize { get; private set; }
    public Matrix3x2 ModelMatrix { get; private set; }

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

    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0) return;

        Vector2 screenCenter = Vector2.Transform(new Vector2(0.5f, 0.5f), ModelMatrix);

        Vector2 topLeft = Vector2.Transform(new Vector2(0, 0), ModelMatrix);
        Vector2 topRight = Vector2.Transform(new Vector2(1, 0), ModelMatrix);
        Vector2 bottomLeft = Vector2.Transform(new Vector2(0, 1), ModelMatrix);

        Vector2 screenHalfSize = new Vector2(
            Vector2.Distance(topLeft, topRight) / 2f,
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
                    stat_culled.Value++;
                    continue;
                }
            }

            child.Draw(renderer);
        }

        if (Masking)
            renderer.PopMask(screenCenter, screenHalfSize, ShearX, CornerRadius, BorderThickness, BorderColor, Vertices);
    }
}
