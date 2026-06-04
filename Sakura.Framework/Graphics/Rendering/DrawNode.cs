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
    public long InvalidationID { get; internal set; }

    public Vertex.Vertex[] PreviousVertices { get; protected set; } = Array.Empty<Vertex.Vertex>();
    public Vertex.Vertex[] CurrentVertices { get; protected set; } = Array.Empty<Vertex.Vertex>();
    public Vertex.Vertex[] Vertices { get; protected set; } = Array.Empty<Vertex.Vertex>();
    public Texture? Texture { get; protected set; }
    public BlendingMode Blending { get; protected set; }
    public float PreviousDrawAlpha { get; protected set; }
    public float CurrentDrawAlpha { get; protected set; }
    public float DrawAlpha { get; protected set; }
    public TextureFillMode FillMode { get; protected set; }
    public RectangleF DrawRectangle { get; protected set; }
    private bool hasPreviousState;

    /// <summary>
    /// Copies the required visual state from the source drawable.
    /// This should execute on the update thread.
    /// </summary>
    public virtual void ApplyState(Drawable source)
    {
        PreviousDrawAlpha = CurrentDrawAlpha;
        CurrentDrawAlpha = source.DrawAlpha;

        Texture = source.Texture;
        Blending = source.Blending;
        DrawRectangle = source.DrawRectangle;
        FillMode = source.FillMode;

        if (CurrentVertices.Length != source.Vertices.Length)
        {
            PreviousVertices = new Vertex.Vertex[source.Vertices.Length];
            CurrentVertices = new Vertex.Vertex[source.Vertices.Length];
            Vertices = new Vertex.Vertex[source.Vertices.Length];
        }

        // Shift current to previous, then grab the new current
        Array.Copy(CurrentVertices, PreviousVertices, CurrentVertices.Length);
        Array.Copy(source.Vertices, CurrentVertices, source.Vertices.Length);

        // Prevent interpolation from (0,0) on the very first frame this node exists
        if (!hasPreviousState)
        {
            Array.Copy(CurrentVertices, PreviousVertices, CurrentVertices.Length);
            PreviousDrawAlpha = CurrentDrawAlpha;
            hasPreviousState = true;
        }
    }

    /// <summary>
    /// Calculates the interpolated state before drawing.
    /// </summary>
    public virtual void PrepareForDraw(double lastUpdateTime, double currentUpdateTime, double drawTime)
    {
        float interpolationFactor = 1.0f;

        if (currentUpdateTime > lastUpdateTime)
        {
            interpolationFactor = (float)((drawTime - lastUpdateTime) / (currentUpdateTime - lastUpdateTime));
            // clamp between 0 and 1 to prevent overshooting if the draw thread runs ahead of the update thread
            interpolationFactor = Math.Clamp(interpolationFactor, 0f, 1f);
        }

        // interpolate the alpha
        DrawAlpha = PreviousDrawAlpha + (CurrentDrawAlpha - PreviousDrawAlpha) * interpolationFactor;

        for (int i = 0; i < CurrentVertices.Length; i++)
        {
            // copy base data (UVs, Colors)
            Vertices[i] = CurrentVertices[i];

            // interpolate Position
            Vertices[i].Position.X = PreviousVertices[i].Position.X + (CurrentVertices[i].Position.X - PreviousVertices[i].Position.X) * interpolationFactor;
            Vertices[i].Position.Y = PreviousVertices[i].Position.Y + (CurrentVertices[i].Position.Y - PreviousVertices[i].Position.Y) * interpolationFactor;
        }
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
