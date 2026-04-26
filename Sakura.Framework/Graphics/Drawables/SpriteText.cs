// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A drawable that renders text using a specified font.
/// </summary>
public class SpriteText : Drawable
{
    private string text = string.Empty;
    private FontUsage fontUsage = FontUsage.Default;

    // Tracks if the text content/font has changed, requiring re-measurement.
    private bool layoutInvalidated = true;

    private Vertex[] textVertices = Array.Empty<Vertex>();
    private int currentVertexCount = 0;
    private ShapedText? shapedText;
    private int lastCacheVersion = -1;

    private Font? resolvedFont;

    [Resolved]
    private IFontStore fontStore { get; set; } = null!;

    [Resolved]
    private IWindow window { get; set; } = null!;

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

    public override void Update()
    {
        if (fontStore != null && lastCacheVersion != fontStore.CacheVersion)
        {
            lastCacheVersion = fontStore.CacheVersion;
            layoutInvalidated = true;
            shapedText = null;
            Invalidate(InvalidationFlags.DrawInfo);
        }

        base.Update();
    }

    protected internal override void UpdateTransforms()
    {
        if (layoutInvalidated)
        {
            computeLayout();
        }
        base.UpdateTransforms();
    }

    private void computeLayout()
    {
        if ((fontStore == null || window == null) && Dependencies != null)
            Dependencies.Inject(this);

        if (fontStore == null || window == null) return;

        if (resolvedFont == null)
            resolvedFont = fontStore.Get(fontUsage);

        if (resolvedFont == null) return;

        var fallbacks = fontStore.GetFallbacks(fontUsage);

        window.GetPhysicalSize(out int physW, out int physH);
        float dpiScale = (float)physW / window.Width;

        if (dpiScale <= 0) dpiScale = 1.0f;

        shapedText = resolvedFont.ProcessText(Text, fontUsage.Size, dpiScale, fallbacks);
        ContentSize = new Vector2(shapedText.BoundingBox.X, shapedText.BoundingBox.Y);

        if (Math.Abs(Size.X - ContentSize.X) > 1.0f || Math.Abs(Size.Y - ContentSize.Y) > 1.0f)
        {
            Size = ContentSize;
            Parent?.Invalidate(InvalidationFlags.DrawInfo);
        }

        layoutInvalidated = false;
    }

    protected override void GenerateVertices()
    {
        if (shapedText == null || shapedText.Glyphs.Count == 0)
        {
            currentVertexCount = 0;
            DrawRectangle = new RectangleF(0, 0, 0, 0); // Reset bounds if empty
            return;
        }

        currentVertexCount = shapedText.Glyphs.Count * 6;

        if (textVertices.Length < currentVertexCount)
        {
            int newSize = Math.Max(textVertices.Length * 2, currentVertexCount);
            Array.Resize(ref textVertices, newSize);
        }

        Span<Vertex> vertices = textVertices.AsSpan(0, currentVertexCount);
        int vIndex = 0;

        var drawColor = new Vector4(
            ColorExtensions.SrgbToLinear(Color.R),
            ColorExtensions.SrgbToLinear(Color.G),
            ColorExtensions.SrgbToLinear(Color.B),
            DrawAlpha
        );

        Vector2 originRelative = GetAnchorOriginVector(Origin);
        Vector2 availableSpace = DrawSize - ContentSize;

        Vector2 textOffset = new Vector2(
            availableSpace.X * originRelative.X,
            availableSpace.Y * originRelative.Y
        );

        Vector2 safeDrawSize = new Vector2(
            DrawSize.X > 0 ? DrawSize.X : 1,
            DrawSize.Y > 0 ? DrawSize.Y : 1
        );
        Vector2 normalizationScale = new Vector2(1.0f / safeDrawSize.X, 1.0f / safeDrawSize.Y);

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var glyph in shapedText.Glyphs)
        {
            var texture = glyph.Texture;
            var pos = glyph.Position;
            var size = glyph.Size;

            float pixelX = pos.X + textOffset.X;
            float pixelY = pos.Y + textOffset.Y;

            float x = pixelX * normalizationScale.X;
            float y = pixelY * normalizationScale.Y;
            float w = size.X * normalizationScale.X;
            float h = size.Y * normalizationScale.Y;

            var vTopLeft = Vector4.Transform(new Vector4(x, y, 0, 1), ModelMatrix);
            var vTopRight = Vector4.Transform(new Vector4(x + w, y, 0, 1), ModelMatrix);
            var vBottomLeft = Vector4.Transform(new Vector4(x, y + h, 0, 1), ModelMatrix);
            var vBottomRight = Vector4.Transform(new Vector4(x + w, y + h, 0, 1), ModelMatrix);

            minX = Math.Min(minX, Math.Min(vTopLeft.X, Math.Min(vTopRight.X, Math.Min(vBottomLeft.X, vBottomRight.X))));
            minY = Math.Min(minY, Math.Min(vTopLeft.Y, Math.Min(vTopRight.Y, Math.Min(vBottomLeft.Y, vBottomRight.Y))));
            maxX = Math.Max(maxX, Math.Max(vTopLeft.X, Math.Max(vTopRight.X, Math.Max(vBottomLeft.X, vBottomRight.X))));
            maxY = Math.Max(maxY, Math.Max(vTopLeft.Y, Math.Max(vTopRight.Y, Math.Max(vBottomLeft.Y, vBottomRight.Y))));

            var uv = texture.UvRect;
            var uvTopLeft = new Vector2(uv.X, uv.Y);
            var uvBottomRight = new Vector2(uv.X + uv.Width, uv.Y + uv.Height);

            vertices[vIndex++] = new Vertex { Position = new Vector2(vTopLeft.X, vTopLeft.Y), TexCoords = uvTopLeft, Color = drawColor };
            vertices[vIndex++] = new Vertex { Position = new Vector2(vTopRight.X, vTopRight.Y), TexCoords = new Vector2(uvBottomRight.X, uvTopLeft.Y), Color = drawColor };
            vertices[vIndex++] = new Vertex { Position = new Vector2(vBottomRight.X, vBottomRight.Y), TexCoords = uvBottomRight, Color = drawColor };

            vertices[vIndex++] = new Vertex { Position = new Vector2(vBottomRight.X, vBottomRight.Y), TexCoords = uvBottomRight, Color = drawColor };
            vertices[vIndex++] = new Vertex { Position = new Vector2(vBottomLeft.X, vBottomLeft.Y), TexCoords = new Vector2(uvTopLeft.X, uvBottomRight.Y), Color = drawColor };
            vertices[vIndex++] = new Vertex { Position = new Vector2(vTopLeft.X, vTopLeft.Y), TexCoords = uvTopLeft, Color = drawColor };
        }

        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    private void flushBatch(IRenderer renderer, Texture texture, int glyphStart, int glyphCount)
    {
        // 6 vertices per glyph (2 triangles)
        int vertexStart = glyphStart * 6;
        int vertexCount = glyphCount * 6;

        var slice = textVertices.AsSpan(vertexStart, vertexCount);
        renderer.DrawVertices(slice, texture);
    }

    protected override DrawNode CreateDrawNode() => new SpriteTextDrawNode();

    public class SpriteTextDrawNode : DrawNode
    {
        private struct GlyphBatch
        {
            public Texture Texture;
            public int VertexStart;
            public int VertexCount;
        }

        private GlyphBatch[] batches = Array.Empty<GlyphBatch>();
        private int batchCount;

        public override void ApplyState(Drawable source)
        {
            base.ApplyState(source);
            var text = (SpriteText)source;

            // Copy the custom text vertices (not the default 6-length base.Vertices)
            if (Vertices.Length < text.currentVertexCount)
            {
                Vertices = new Vertex[text.currentVertexCount];
            }
            Array.Copy(text.textVertices, Vertices, text.currentVertexCount);

            // Generate Texture Batches
            batchCount = 0;

            if (text.shapedText == null || text.shapedText.Glyphs.Count == 0)
                return;

            if (batches.Length < text.shapedText.Glyphs.Count)
                batches = new GlyphBatch[text.shapedText.Glyphs.Count];

            Texture? currentTexture = null;
            int currentVertexStart = 0;
            int currentVertexCount = 0;

            for (int i = 0; i < text.shapedText.Glyphs.Count; i++)
            {
                var glyph = text.shapedText.Glyphs[i];

                // If texture changes, flush the current batch
                if (currentTexture != null && currentTexture.GlTexture.Handle != glyph.Texture.GlTexture.Handle)
                {
                    batches[batchCount++] = new GlyphBatch
                    {
                        Texture = currentTexture,
                        VertexStart = currentVertexStart,
                        VertexCount = currentVertexCount
                    };
                    currentVertexStart += currentVertexCount;
                    currentVertexCount = 0;
                }

                currentTexture = glyph.Texture;
                currentVertexCount += 6;
            }

            // Push the final batch
            if (currentTexture != null && currentVertexCount > 0)
            {
                batches[batchCount++] = new GlyphBatch
                {
                    Texture = currentTexture,
                    VertexStart = currentVertexStart,
                    VertexCount = currentVertexCount
                };
            }
        }

        public override void Draw(IRenderer renderer)
        {
            if (DrawAlpha <= 0 || Vertices.Length == 0) return;

            renderer.SetBlendMode(Blending);

            // Loop over the pre-calculated batches and draw them
            for (int i = 0; i < batchCount; i++)
            {
                var batch = batches[i];
                var slice = Vertices.AsSpan(batch.VertexStart, batch.VertexCount);
                renderer.DrawVertices(slice, batch.Texture);
            }
        }
    }
}
