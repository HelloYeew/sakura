// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A drawable that renders a triangle defined by three points.
/// </summary>
public class Triangle : Drawable
{
    protected new readonly Vertex[] Vertices = new Vertex[3];

    protected override void GenerateVertices()
    {
        float rLinear = ColorExtensions.SrgbToLinear(Color.R);
        float gLinear = ColorExtensions.SrgbToLinear(Color.G);
        float bLinear = ColorExtensions.SrgbToLinear(Color.B);

        var calculatedColor = new System.Numerics.Vector4(rLinear, gLinear, bLinear, DrawAlpha);

        var localP1 = new Vector4(0.5f, 0, 0, 1);
        var localP2 = new Vector4(0, 1, 0, 1);
        var localP3 = new Vector4(1, 1, 0, 1);

        var screenP1 = Vector4.Transform(localP1, ModelMatrix);
        var screenP2 = Vector4.Transform(localP2, ModelMatrix);
        var screenP3 = Vector4.Transform(localP3, ModelMatrix);

        Vertices[0] = new Vertex
        {
            Position = new Vector2(screenP1.X, screenP1.Y),
            TexCoords = new Vector2(0.5f, 0), // TexCoords can be mapped however you like
            Color = calculatedColor
        };
        Vertices[1] = new Vertex
        {
            Position = new Vector2(screenP2.X, screenP2.Y),
            TexCoords = new Vector2(0, 1),
            Color = calculatedColor
        };
        Vertices[2] = new Vertex
        {
            Position = new Vector2(screenP3.X, screenP3.Y),
            TexCoords = new Vector2(1, 1),
            Color = calculatedColor
        };
    }

    public override void Draw(IRenderer renderer)
    {
        renderer.DrawVertices(Vertices, Texture ?? renderer.WhitePixel);
    }
}
