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

    public Vertex.Vertex[] Vertices { get; protected set; } = Array.Empty<Vertex.Vertex>();
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
    /// Snapshots the source drawable's vertices into this node.
    /// Subclasses with custom vertex storage can override this to copy from their own source.
    /// </summary>
    protected virtual void ApplyVertices(Drawable source)
    {
        if (Vertices.Length != source.Vertices.Length)
            Vertices = new Vertex.Vertex[source.Vertices.Length];

        Array.Copy(source.Vertices, Vertices, source.Vertices.Length);
    }

    /// <summary>
    /// Submits the node's state to the renderer.
    /// This should execute on the draw thread.
    /// </summary>
    public virtual void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0 || Vertices.Length == 0)
            return;

        stat_drawn_last_frame.Value++;
        renderer.SetBlendMode(Blending);

        if (Topology == VertexTopology.Quads)
            renderer.DrawQuads(Vertices, Texture ?? renderer.WhitePixel);
        else
            renderer.DrawVertices(Vertices, Texture ?? renderer.WhitePixel);
    }
}
