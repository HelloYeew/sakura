// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
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

    public EdgeEffectParameters EdgeEffect { get; private set; }

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
        EdgeEffect = container.EdgeEffect;
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

        // Local-to-screen scale factors, used to convert local-pixel edge-effect parameters
        // (radius, roundness, corner radius, offset) into screen space.
        float scaleX = DrawSize.X > 0 ? screenHalfSize.X / (DrawSize.X * 0.5f) : 1f;
        float scaleY = DrawSize.Y > 0 ? screenHalfSize.Y / (DrawSize.Y * 0.5f) : 1f;
        float uniformScale = (scaleX + scaleY) * 0.5f;

        // CornerRadius and BorderThickness are authored in local (logical) pixels, but the mask/border
        // shader compares them against the screen-space half-size, which already includes every ancestor
        // Scale. Convert both into screen space so a CircularContainer (CornerRadius == DrawSize/2) stays
        // a circle under scale instead of turning into a squircle. Matches the edge-effect convention.
        // Non-uniform scale (scaleX != scaleY) can't be expressed by a single-float radius; the average
        // is the pragmatic choice and matches the edge-effect path.
        float cornerRadiusScreen = CornerRadius * uniformScale;
        float borderThicknessScreen = BorderThickness * uniformScale;

        bool hasEdgeEffect = EdgeEffect.Type != EdgeEffectType.None && EdgeEffect.Color.A > 0;

        // Shadows render behind the container's contents.
        if (hasEdgeEffect && EdgeEffect.Type == EdgeEffectType.Shadow)
            drawEdgeEffect(renderer, screenCenter, screenHalfSize, scaleX, scaleY, uniformScale, cornerRadiusScreen);

        if (Masking)
            renderer.PushMask(screenCenter, screenHalfSize, ShearX, cornerRadiusScreen);

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
            renderer.PopMask(screenCenter, screenHalfSize, ShearX, cornerRadiusScreen, borderThicknessScreen, BorderColor, Vertices);

        // Glows render on top of the container's contents.
        if (hasEdgeEffect && EdgeEffect.Type == EdgeEffectType.Glow)
            drawEdgeEffect(renderer, screenCenter, screenHalfSize, scaleX, scaleY, uniformScale, cornerRadiusScreen);
    }

    /// <summary>
    /// Builds the expanded screen-space quad and submits the edge effect to the renderer.
    /// The quad covers the container shape inflated by the (screen-space) edge radius so that the
    /// shader's signed-distance falloff has room to render; the shape itself is shaded in the fragment shader.
    /// </summary>
    private void drawEdgeEffect(IRenderer renderer, Vector2 screenCenter, Vector2 screenHalfSize, float scaleX, float scaleY, float uniformScale, float cornerRadiusScreen)
    {
        float edgeRadiusScreen = Math.Max(0f, EdgeEffect.Radius) * uniformScale;

        // The effect follows the container's exact corner curvature, so it shares the same screen-space
        // corner radius the masking/border path uses. Roundness is an optional adjustment expressed in
        // local pixels, scaled to match.
        float cornerRadius = cornerRadiusScreen + Math.Max(0f, EdgeEffect.Roundness) * uniformScale;

        Vector2 offsetScreen = new Vector2(EdgeEffect.Offset.X * scaleX, EdgeEffect.Offset.Y * scaleY);

        // Inflate the half-size by the falloff radius so the quad covers the soft edge.
        Vector2 expandedHalf = new Vector2(
            screenHalfSize.X + edgeRadiusScreen,
            screenHalfSize.Y + edgeRadiusScreen
        );

        Vector2 quadCenter = screenCenter + offsetScreen;

        // Account for shear when laying out the corners (matches the masking shape's shear handling).
        float skew = ShearX * expandedHalf.Y;

        Vector2 tl = new Vector2(quadCenter.X - expandedHalf.X + skew, quadCenter.Y - expandedHalf.Y);
        Vector2 tr = new Vector2(quadCenter.X + expandedHalf.X + skew, quadCenter.Y - expandedHalf.Y);
        Vector2 br = new Vector2(quadCenter.X + expandedHalf.X - skew, quadCenter.Y + expandedHalf.Y);
        Vector2 bl = new Vector2(quadCenter.X - expandedHalf.X - skew, quadCenter.Y + expandedHalf.Y);

        var quad = new Vertex.Vertex[4];
        quad[0] = new Vertex.Vertex { Position = tl, TexCoords = new Vector2(0, 0), Color = new Vector4(1, 1, 1, 1) };
        quad[1] = new Vertex.Vertex { Position = tr, TexCoords = new Vector2(1, 0), Color = new Vector4(1, 1, 1, 1) };
        quad[2] = new Vertex.Vertex { Position = br, TexCoords = new Vector2(1, 1), Color = new Vector4(1, 1, 1, 1) };
        quad[3] = new Vertex.Vertex { Position = bl, TexCoords = new Vector2(0, 1), Color = new Vector4(1, 1, 1, 1) };

        // Premultiply the effect alpha by the container's overall draw alpha so it fades with the container.
        var color = EdgeEffect.Color;
        if (DrawAlpha < 1f)
            color = Color.FromArgb((int)Math.Clamp(color.A * DrawAlpha, 0f, 255f), color);

        renderer.DrawEdgeEffect(
            screenCenter,
            screenHalfSize,
            ShearX,
            cornerRadius,
            edgeRadiusScreen,
            offsetScreen,
            color,
            EdgeEffect.Type == EdgeEffectType.Glow,
            EdgeEffect.Hollow,
            quad);
    }
}
