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
    private bool layoutInvalidated = true;

    private readonly List<Vertex> textVertices = new();
    private ShapedText? shapedText;

    // Resolved font instance from store
    private Font? resolvedFont;

    [Resolved]
    private IFontStore fontStore { get; set; } = null!;

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
                // Force re-resolution of font object
                resolvedFont = null;
                Invalidate(InvalidationFlags.DrawInfo);
            }
        }
    }

    protected override void UpdateTransforms()
    {
        base.UpdateTransforms();

        if (layoutInvalidated)
        {
            computeLayout();
        }
    }

    private void computeLayout()
    {
        // Resolve dependency if not already (in case it wasn't injected yet)
        if (fontStore == null && Dependencies != null)
             Dependencies.Inject(this);

        if (fontStore == null) return;

        // Resolve the actual font object based on usage
        if (resolvedFont == null)
            resolvedFont = fontStore.Get(fontUsage);

        if (resolvedFont == null) return;

        // Process text using the font and the requested size
        shapedText = resolvedFont.ProcessText(Text, fontUsage.Size);

        // Update size of the drawable
        Size = new Vector2(shapedText.BoundingBox.X, shapedText.BoundingBox.Y);

        layoutInvalidated = false;
        generateTextVertices();
    }

    private void generateTextVertices()
    {
        textVertices.Clear();
        if (shapedText == null) return;

        var drawColor = new Vector4(
            ColorExtensions.SrgbToLinear(Color.R),
            ColorExtensions.SrgbToLinear(Color.G),
            ColorExtensions.SrgbToLinear(Color.B),
            DrawAlpha
        );

        foreach (var glyph in shapedText.Glyphs)
        {
            var texture = glyph.Texture;
            var pos = glyph.Position;
            var size = glyph.Size;

            // Apply ModelMatrix
            var vTopLeft = Vector4.Transform(new Vector4(pos.X, pos.Y, 0, 1), ModelMatrix);
            var vTopRight = Vector4.Transform(new Vector4(pos.X + size.X, pos.Y, 0, 1), ModelMatrix);
            var vBottomLeft = Vector4.Transform(new Vector4(pos.X, pos.Y + size.Y, 0, 1), ModelMatrix);
            var vBottomRight = Vector4.Transform(new Vector4(pos.X + size.X, pos.Y + size.Y, 0, 1), ModelMatrix);

            var uv = texture.UvRect;
            var uvTopLeft = new Vector2(uv.X, uv.Y);
            var uvBottomRight = new Vector2(uv.X + uv.Width, uv.Y + uv.Height);

            // Add Quad (Triangle list)
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

        var texture = shapedText.Glyphs[0].Texture;
        renderer.DrawVertices(CollectionsMarshal.AsSpan(textVertices), texture);
    }
}
