// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Statistic;
using Texture = Sakura.Framework.Graphics.Textures.Texture;

namespace Sakura.Framework.Graphics.Rendering;

public class DrawNode
{
    private static readonly GlobalStatistic<int> stat_drawn_last_frame = GlobalStatistics.Get<int>("Drawables", "Drawn Last Frame");

    public long InvalidationID { get; internal set; }

    /// <summary>
    /// Backing storage for <see cref="Vertices"/>. Grow-only, so its length is a capacity and says nothing
    /// about how many vertices are live (that is <see cref="VertexCount"/>).
    /// </summary>
    private Vertex.Vertex[] vertices = Array.Empty<Vertex.Vertex>();

    /// <summary>
    /// How many vertices this node will draw.
    /// </summary>
    public int VertexCount { get; private set; }

    /// <summary>
    /// The node's live vertices. <c>Length</c> is the vertex count, not the capacity behind it.
    /// </summary>
    public ReadOnlySpan<Vertex.Vertex> Vertices => vertices.AsSpan(0, VertexCount);

    /// <summary>
    /// The same range, writable, for subclasses that fill their vertices themselves rather than copying
    /// them from <see cref="Drawable.Vertices"/>.
    /// </summary>
    protected Span<Vertex.Vertex> WritableVertices => vertices.AsSpan(0, VertexCount);

    /// <summary>
    /// Capacity of the backing array. Exposed so a test can assert that a shrink then a regrow does not
    /// reallocate; nothing in the draw path should care about it.
    /// </summary>
    internal int VertexCapacity => vertices.Length;

    public Texture? Texture { get; protected set; }
    public BlendingMode Blending { get; protected set; }
    public float DrawAlpha { get; protected set; }
    public TextureFillMode FillMode { get; protected set; }
    public RectangleF DrawRectangle { get; protected set; }
    public VertexTopology Topology { get; protected set; }

    /// <summary>
    /// Copies the required visual state from the source drawable.
    /// This should execute on the update thread.
    /// The node is a plain snapshot of the drawable's latest updated state; the draw thread
    /// renders it as-is without any cross-frame interpolation.
    /// </summary>
    public virtual void ApplyState(Drawable source)
    {
        DrawAlpha = source.DrawAlpha;
        Texture = source.Texture;
        Blending = source.Blending;
        DrawRectangle = source.DrawRectangle;
        FillMode = source.FillMode;
        Topology = source.Topology;

        ApplyVertices(source);
    }

    /// <summary>
    /// Sets how many vertices are live, growing the backing array only when it is too small.
    /// </summary>
    protected void SetVertexCount(int count)
    {
        if (vertices.Length < count)
            vertices = new Vertex.Vertex[count];

        VertexCount = count;
    }

    /// <summary>
    /// Snapshots the source drawable's vertices into this node.
    /// Subclasses with custom vertex storage can override this to copy from their own source.
    /// </summary>
    protected virtual void ApplyVertices(Drawable source)
    {
        SetVertexCount(source.Vertices.Length);
        source.Vertices.AsSpan().CopyTo(WritableVertices);
    }

    /// <summary>
    /// Submits the node's state to the renderer.
    /// This should execute on the draw thread.
    /// </summary>
    public virtual void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0 || VertexCount == 0)
            return;

        stat_drawn_last_frame.Value++;
        renderer.SetBlendMode(Blending);

        if (Topology == VertexTopology.Quads)
            renderer.DrawQuads(Vertices, Texture ?? renderer.WhitePixel);
        else
            renderer.DrawVertices(Vertices, Texture ?? renderer.WhitePixel);
    }
}
