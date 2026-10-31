// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// One piece a <see cref="ParticleField"/> draws: its texture, placed, turned and tinted.
/// </summary>
/// <param name="Position">Its middle, in the field's units.</param>
/// <param name="Size">How big the texture is drawn, in the field's units.</param>
/// <param name="Rotation">How far it's turned, in radians.</param>
/// <param name="Color">Its tint.</param>
/// <param name="Alpha">How opaque, 0–1, on top of the tint's own.</param>
public readonly record struct Particle(Vector2 Position, Vector2 Size, float Rotation, Color Color, float Alpha);

/// <summary>
/// Draws many copies of one texture at once, each placed, turned and tinted as its own <see cref="Particle"/>: a storm of
/// petals in one draw rather than hundreds of sprites. Whatever moves them sets them again each frame
/// (<see cref="Clear"/>, then <see cref="Add"/>).
/// </summary>
/// <remarks>
/// It fills its parent by default. <see cref="Drawable.Texture"/> is the one texture they share; with none they're plain
/// tinted rectangles. <see cref="Drawable.Color"/> only adds its alpha: each piece has its own tint.
/// </remarks>
public partial class ParticleField : Drawable
{
    private readonly List<Particle> particles = new List<Particle>();

    /// <summary>
    /// How many pieces it's drawing.
    /// </summary>
    public int Count => particles.Count;

    public IReadOnlyList<Particle> Particles => particles;

    public ParticleField()
    {
        RelativeSizeAxes = Axes.Both;
    }

    public override bool HandlePositionalInput => false;

    public void Clear()
    {
        if (particles.Count == 0)
            return;

        particles.Clear();
        piecesChanged();
    }

    public void Add(in Particle particle)
    {
        particles.Add(particle);
        piecesChanged();
    }

    // Its vertices have to be built again; its size, which a parent would care about, hasn't changed.
    private void piecesChanged() => Invalidate(InvalidationFlags.DrawInfo, false);

    protected internal override VertexTopology Topology => VertexTopology.Triangles;

    protected override void UpdateDrawColor() => UpdateVertexAlpha();

    protected override void GenerateVertices()
    {
        Vector2 size = AppliedDrawSize;

        if (particles.Count == 0 || size.X <= 0 || size.Y <= 0)
        {
            SetVertexCount(0);
            DrawRectangle = new RectangleF(0, 0, 0, 0);
            return;
        }

        SetVertexCount(particles.Count * 6);

        RectangleF uv = Texture?.UvRect ?? new RectangleF(0, 0, 1, 1);
        var uvTopLeft = new Vector2(uv.X, uv.Y);
        var uvTopRight = new Vector2(uv.X + uv.Width, uv.Y);
        var uvBottomRight = new Vector2(uv.X + uv.Width, uv.Y + uv.Height);
        var uvBottomLeft = new Vector2(uv.X, uv.Y + uv.Height);

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        int offset = 0;

        foreach (Particle particle in particles)
        {
            float cos = MathF.Cos(particle.Rotation);
            float sin = MathF.Sin(particle.Rotation);
            Vector2 across = new Vector2(cos, sin) * (particle.Size.X / 2);
            Vector2 down = new Vector2(-sin, cos) * (particle.Size.Y / 2);

            Vector2 topLeft = toScreen(size, particle.Position - across - down);
            Vector2 topRight = toScreen(size, particle.Position + across - down);
            Vector2 bottomRight = toScreen(size, particle.Position + across + down);
            Vector2 bottomLeft = toScreen(size, particle.Position - across + down);

            Color c = particle.Color;
            var color = new Vector4(
                ColorExtensions.SrgbToLinear(c.R),
                ColorExtensions.SrgbToLinear(c.G),
                ColorExtensions.SrgbToLinear(c.B),
                DrawAlpha * particle.Alpha * (c.A / 255f) * (Color.A / 255f));

            Vertices[offset + 0] = new Vertex { Position = topLeft, TexCoords = uvTopLeft, Color = color };
            Vertices[offset + 1] = new Vertex { Position = topRight, TexCoords = uvTopRight, Color = color };
            Vertices[offset + 2] = new Vertex { Position = bottomRight, TexCoords = uvBottomRight, Color = color };
            Vertices[offset + 3] = new Vertex { Position = bottomRight, TexCoords = uvBottomRight, Color = color };
            Vertices[offset + 4] = new Vertex { Position = bottomLeft, TexCoords = uvBottomLeft, Color = color };
            Vertices[offset + 5] = new Vertex { Position = topLeft, TexCoords = uvTopLeft, Color = color };
            offset += 6;

            minX = Math.Min(minX, Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomRight.X, bottomLeft.X)));
            minY = Math.Min(minY, Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomRight.Y, bottomLeft.Y)));
            maxX = Math.Max(maxX, Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomRight.X, bottomLeft.X)));
            maxY = Math.Max(maxY, Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomRight.Y, bottomLeft.Y)));
        }

        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    private Vector2 toScreen(Vector2 size, Vector2 local) => Vector2.Transform(new Vector2(local.X / size.X, local.Y / size.Y), ModelMatrix);
}
