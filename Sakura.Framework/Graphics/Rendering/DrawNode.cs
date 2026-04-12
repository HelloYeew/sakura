// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Rendering;

public class DrawNode
{
    public long InvalidationID { get; internal set; }

    public Vertex.Vertex[] Vertices { get; protected set; } = Array.Empty<Vertex.Vertex>();
    public Texture? Texture { get; protected set; }
    public BlendingMode Blending { get; protected set; }
    public float DrawAlpha { get; protected set; }
    public RectangleF DrawRectangle { get; protected set; }

    /// <summary>
    /// Copies the required visual state from the source drawable.
    /// This should execute on the update thread.
    /// </summary>
    public virtual void ApplyState(Drawable source)
    {
        DrawAlpha = source.DrawAlpha;
        Texture = source.Texture;
        Blending = source.Blending;
        if (Vertices.Length != source.Vertices.Length)
            Vertices = new Vertex.Vertex[source.Vertices.Length];
        Array.Copy(source.Vertices, Vertices, source.Vertices.Length);
        DrawRectangle = source.DrawRectangle;
    }

    /// <summary>
    /// Submits the node's state to the renderer.
    /// This should execute on the draw thread.
    /// </summary>
    public virtual void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0 || Vertices.Length == 0)
            return;
        GlobalStatistics.Get<int>("Drawables", "Drawn Last Frame").Value++;
        renderer.SetBlendMode(Blending);
        renderer.DrawVertices(Vertices, Texture ?? renderer.WhitePixel);
    }
}
