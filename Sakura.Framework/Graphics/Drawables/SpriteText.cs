// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Drawables;

public class SpriteText : Drawable
{
    private string text = string.Empty;
    private FontUsage fontUsage = FontUsage.Default;

    // Tracks if the text content/font has changed, requiring re-measurement.
    private bool layoutInvalidated = true;

    private readonly List<Vertex> textVertices = new();
    private ShapedText? shapedText;

    private Font? resolvedFont;

    [Resolved]
    private IFontStore fontStore { get; set; } = null!;

    /// <summary>
    /// The actual measured size of the text content in pixels.
    /// Use this if want to set the Drawable.Size to fit the text.
    /// </summary>
    public Vector2 ContentSize { get; private set; }

    public string Text
    {
        get => text;
        set
        {
            if (text != value)
            {
                text = value;
                layoutInvalidated = true;
                Invalidate(InvalidationFlags.DrawInfo);
            }
        }
    }

    public FontUsage Font
    {
        get => fontUsage;
        set
        {
            if (fontUsage != value)
            {
                fontUsage = value;
                layoutInvalidated = true;
                resolvedFont = null;
                Invalidate(InvalidationFlags.DrawInfo);
            }
        }
    }

    protected override void UpdateTransforms()
    {
        if (layoutInvalidated)
        {
            computeLayout();
        }
        base.UpdateTransforms();
    }

    private void computeLayout()
    {
        if (fontStore == null && Dependencies != null)
             Dependencies.Inject(this);

        if (fontStore == null) return;

        if (resolvedFont == null)
            resolvedFont = fontStore.Get(fontUsage);

        if (resolvedFont == null) return;

        shapedText = resolvedFont.ProcessText(Text, fontUsage.Size);
        ContentSize = new Vector2(shapedText.BoundingBox.X, shapedText.BoundingBox.Y);

        layoutInvalidated = false;
    }

    protected override void GenerateVertices()
    {
        textVertices.Clear();
        if (shapedText == null) return;

        // If the user hasn't given us any size to draw into, we can't project the vertices.
        // Alternatively, you could default to ContentSize if Size is Zero,
        // but typically Size.Zero means "don't draw".
        if (Size.X <= 0 || Size.Y <= 0)
            return;

        var drawColor = new Vector4(
            ColorExtensions.SrgbToLinear(Color.R),
            ColorExtensions.SrgbToLinear(Color.G),
            ColorExtensions.SrgbToLinear(Color.B),
            DrawAlpha
        );

        // We need to normalize pixel coordinates to 0..1 space because Drawable's ModelMatrix
        // scales (0..1) up to (Size.X..Size.Y).
        Vector2 normalizationScale = new Vector2(1.0f / Size.X, 1.0f / Size.Y);

        foreach (var glyph in shapedText.Glyphs)
        {
            var texture = glyph.Texture;
            var pos = glyph.Position;
            var size = glyph.Size;

            // Normalize coordinates to 0..1 relative to the Drawable.Size
            float x = pos.X * normalizationScale.X;
            float y = pos.Y * normalizationScale.Y;
            float w = size.X * normalizationScale.X;
            float h = size.Y * normalizationScale.Y;

            // Transform 0..1 local coords to World coords using the matrix
            var vTopLeft = Vector4.Transform(new Vector4(x, y, 0, 1), ModelMatrix);
            var vTopRight = Vector4.Transform(new Vector4(x + w, y, 0, 1), ModelMatrix);
            var vBottomLeft = Vector4.Transform(new Vector4(x, y + h, 0, 1), ModelMatrix);
            var vBottomRight = Vector4.Transform(new Vector4(x + w, y + h, 0, 1), ModelMatrix);

            var uv = texture.UvRect;
            var uvTopLeft = new Vector2(uv.X, uv.Y);
            var uvBottomRight = new Vector2(uv.X + uv.Width, uv.Y + uv.Height);

            textVertices.Add(new Vertex { Position = new Vector2(vTopLeft.X, vTopLeft.Y), TexCoords = uvTopLeft, Color = drawColor });
            textVertices.Add(new Vertex { Position = new Vector2(vTopRight.X, vTopRight.Y), TexCoords = new Vector2(uvBottomRight.X, uvTopLeft.Y), Color = drawColor });
            textVertices.Add(new Vertex { Position = new Vector2(vBottomRight.X, vBottomRight.Y), TexCoords = uvBottomRight, Color = drawColor });

            textVertices.Add(new Vertex { Position = new Vector2(vBottomRight.X, vBottomRight.Y), TexCoords = uvBottomRight, Color = drawColor });
            textVertices.Add(new Vertex { Position = new Vector2(vBottomLeft.X, vBottomLeft.Y), TexCoords = new Vector2(uvTopLeft.X, uvBottomRight.Y), Color = drawColor });
            textVertices.Add(new Vertex { Position = new Vector2(vTopLeft.X, vTopLeft.Y), TexCoords = uvTopLeft, Color = drawColor });
        }
    }

    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0 || textVertices.Count == 0 || shapedText == null || shapedText.Glyphs.Count == 0)
            return;

        // Assuming all glyphs share the same texture page (atlas)
        var texture = shapedText.Glyphs[0].Texture;
        renderer.DrawVertices(CollectionsMarshal.AsSpan(textVertices), texture);
    }
}
