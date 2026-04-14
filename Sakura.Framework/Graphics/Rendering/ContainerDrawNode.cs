// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

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

    public RectangleF DrawRectangle { get; private set; }

    public override void ApplyState(Drawable source)
    {
        base.ApplyState(source);
        var container = (Container)source;
        Masking = container.Masking;
        CornerRadius = container.CornerRadius;
        BorderThickness = container.BorderThickness;
        BorderColor = container.BorderColor;
        DrawRectangle = container.DrawRectangle;
    }

    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0) return;

        if (Masking)
            renderer.PushMask(DrawRectangle, CornerRadius);

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
            renderer.PopMask(DrawRectangle, CornerRadius, BorderThickness, BorderColor, Vertices);
    }
}
