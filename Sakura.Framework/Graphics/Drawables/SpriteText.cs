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
public partial class SpriteText : Drawable
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

    private Vector2 contentSize;

    /// <summary>
    /// The actual measured size of the text content in pixels.
    /// Use this if want to set the Drawable.Size to fit the text.
    /// </summary>
    public Vector2 ContentSize
    {
        get
        {
            ensureLayout();
            return contentSize;
        }
        private set => contentSize = value;
    }

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

    /// <summary>
    /// The measured size of this sprite. Reading any size component forces a layout pass so the
    /// returned value reflects the current <see cref="Text"/> immediately (callers commonly read
    /// <see cref="Width"/> right after setting text to size a surrounding element). Without this,
    /// the getter would return the previously measured size until the next frame's update — which
    /// is especially noticeable when the text switches scripts (e.g. Latin to CJK via a fallback
    /// font) and the width changes substantially.
    /// </summary>
    public override Vector2 Size
    {
        get
        {
            ensureLayout();
            return base.Size;
        }
        set => base.Size = value;
    }

    public override float Width
    {
        get => Size.X;
        set => Size = new Vector2(value, Size.Y);
    }

    public override float Height
    {
        get => Size.Y;
        set => Size = new Vector2(Size.X, value);
    }

    /// <summary>
    /// Runs a deferred layout pass if the text/font changed since the last measurement.
    /// Safe to call before dependencies are injected: it no-ops until they are available.
    /// </summary>
    private void ensureLayout()
    {
        if (layoutInvalidated && !computingLayout)
            computeLayout();
    }

    // Guards against re-entrancy: computeLayout reads/writes Size, whose accessors call
    // ensureLayout. Without this flag those reads would recurse back into computeLayout.
    private bool computingLayout;

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
        ensureLayout();
        base.UpdateTransforms();
    }

    private void computeLayout()
    {
        if ((fontStore == null || window == null) && Dependencies != null)
            DependencyActivator.Inject(this, Dependencies);

        // Dependencies not ready yet. Leave layoutInvalidated set so we measure once they are,
        // but flip the re-entrancy guard to avoid spinning if Size is read in the meantime.
        if (fontStore == null || window == null) return;

        computingLayout = true;
        try
        {
            if (resolvedFont == null)
                resolvedFont = fontStore.Get(fontUsage);

            if (resolvedFont == null) return;

            var fallbacks = fontStore.GetFallbacks(fontUsage);

            window.GetPhysicalSize(out int physW, out int physH);
            float dpiScale = (float)physW / window.Width;

            if (dpiScale <= 0) dpiScale = 1.0f;

            var variation = fontStore.GetVariation(fontUsage);
            shapedText = resolvedFont.ProcessText(Text, fontUsage.Size, dpiScale, fallbacks, variation);
            // Assign the backing field directly; the ContentSize getter forces layout, which we're
            // already inside of (guarded by computingLayout).
            contentSize = new Vector2(shapedText.BoundingBox.X, shapedText.BoundingBox.Y);

            // Read through base.Size to avoid re-entering layout via the overridden getter.
            if (Math.Abs(base.Size.X - contentSize.X) > 1.0f || Math.Abs(base.Size.Y - contentSize.Y) > 1.0f)
            {
                // The Size setter invalidates our geometry and notifies an interested parent
                // (auto-size / flow), so no explicit parent invalidation is needed.
                base.Size = contentSize;
            }

            layoutInvalidated = false;
        }
        finally
        {
            computingLayout = false;
        }
    }

    protected override void GenerateVertices()
    {
        if (shapedText == null || shapedText.Glyphs.Count == 0)
        {
            currentVertexCount = 0;
            DrawRectangle = new RectangleF(0, 0, 0, 0); // Reset bounds if empty
            return;
        }

        currentVertexCount = shapedText.Glyphs.Count * 4;

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

        // Color glyphs (e.g. color emoji bitmaps) carry their own native colors and must not be
        // recolored by the drawable's tint, only alpha (for fades) should still apply to them.
        var colorGlyphDrawColor = new Vector4(1f, 1f, 1f, DrawAlpha);

        Vector2 originRelative = GetAnchorOriginVector(Origin);
        Vector2 availableSpace = DrawSize - contentSize;

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

        var glyphs = shapedText.Glyphs;

        for (int glyphIndex = 0; glyphIndex < glyphs.Count; glyphIndex++)
        {
            var glyph = glyphs[glyphIndex];
            var texture = glyph.Texture;
            var pos = glyph.Position;
            var size = glyph.Size;

            // Invisible glyphs (e.g. spaces) carry advance width but have no texture.
            // Emit a zero-area quad so the per-glyph vertex layout stays 1:1 with the glyph
            // list (the draw node skips these batches), while still letting the glyph
            // contribute to the bounding box.
            if (texture == null)
            {
                float gx = (pos.X + textOffset.X) * normalizationScale.X;
                float gy = (pos.Y + textOffset.Y) * normalizationScale.Y;
                var p = Vector2.Transform(new Vector2(gx, gy), ModelMatrix);

                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);

                var zero = new Vertex { Position = new Vector2(p.X, p.Y), TexCoords = Vector2.Zero, Color = drawColor };
                vertices[vIndex++] = zero;
                vertices[vIndex++] = zero;
                vertices[vIndex++] = zero;
                vertices[vIndex++] = zero;
                continue;
            }

            float pixelX = pos.X + textOffset.X;
            float pixelY = pos.Y + textOffset.Y;

            float x = pixelX * normalizationScale.X;
            float y = pixelY * normalizationScale.Y;
            float w = size.X * normalizationScale.X;
            float h = size.Y * normalizationScale.Y;

            var vTopLeft = Vector2.Transform(new Vector2(x, y), ModelMatrix);
            var vTopRight = Vector2.Transform(new Vector2(x + w, y), ModelMatrix);
            var vBottomLeft = Vector2.Transform(new Vector2(x, y + h), ModelMatrix);
            var vBottomRight = Vector2.Transform(new Vector2(x + w, y + h), ModelMatrix);

            minX = Math.Min(minX, Math.Min(vTopLeft.X, Math.Min(vTopRight.X, Math.Min(vBottomLeft.X, vBottomRight.X))));
            minY = Math.Min(minY, Math.Min(vTopLeft.Y, Math.Min(vTopRight.Y, Math.Min(vBottomLeft.Y, vBottomRight.Y))));
            maxX = Math.Max(maxX, Math.Max(vTopLeft.X, Math.Max(vTopRight.X, Math.Max(vBottomLeft.X, vBottomRight.X))));
            maxY = Math.Max(maxY, Math.Max(vTopLeft.Y, Math.Max(vTopRight.Y, Math.Max(vBottomLeft.Y, vBottomRight.Y))));

            var uv = texture.UvRect;
            var uvTopLeft = new Vector2(uv.X, uv.Y);
            var uvBottomRight = new Vector2(uv.X + uv.Width, uv.Y + uv.Height);

            var glyphColor = glyph.IsColorGlyph ? colorGlyphDrawColor : drawColor;

            // One indexed quad per glyph (TL, TR, BR, BL).
            vertices[vIndex++] = new Vertex { Position = new Vector2(vTopLeft.X, vTopLeft.Y), TexCoords = uvTopLeft, Color = glyphColor };
            vertices[vIndex++] = new Vertex { Position = new Vector2(vTopRight.X, vTopRight.Y), TexCoords = new Vector2(uvBottomRight.X, uvTopLeft.Y), Color = glyphColor };
            vertices[vIndex++] = new Vertex { Position = new Vector2(vBottomRight.X, vBottomRight.Y), TexCoords = uvBottomRight, Color = glyphColor };
            vertices[vIndex++] = new Vertex { Position = new Vector2(vBottomLeft.X, vBottomLeft.Y), TexCoords = new Vector2(uvTopLeft.X, uvBottomRight.Y), Color = glyphColor };
        }

        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    private void flushBatch(IRenderer renderer, Texture texture, int glyphStart, int glyphCount)
    {
        // 4 vertices per glyph (one indexed quad)
        int vertexStart = glyphStart * 4;
        int vertexCount = glyphCount * 4;

        var slice = textVertices.AsSpan(vertexStart, vertexCount);
        renderer.DrawQuads(slice, texture);
    }

    protected override void UpdateDrawColour()
    {
        DrawAlpha = (Parent?.DrawAlpha ?? 1f) * Alpha;

        var drawColor = new Vector4(
            ColorExtensions.SrgbToLinear(Color.R),
            ColorExtensions.SrgbToLinear(Color.G),
            ColorExtensions.SrgbToLinear(Color.B),
            DrawAlpha
        );

        // Color glyphs keep their native colors, only alpha follows the drawable's fade/tint.
        var colorGlyphDrawColor = new Vector4(1f, 1f, 1f, DrawAlpha);

        if (shapedText == null || shapedText.Glyphs.Count == 0)
        {
            for (int i = 0; i < currentVertexCount; i++)
                textVertices[i].Color = drawColor;
            return;
        }

        var glyphs = shapedText.Glyphs;
        int vIndex = 0;

        for (int glyphIndex = 0; glyphIndex < glyphs.Count; glyphIndex++)
        {
            var glyphColor = glyphs[glyphIndex].IsColorGlyph ? colorGlyphDrawColor : drawColor;

            for (int i = 0; i < 4 && vIndex < currentVertexCount; i++, vIndex++)
                textVertices[vIndex].Color = glyphColor;
        }
    }

    protected override DrawNode CreateDrawNode() => new SpriteTextDrawNode();

    /// <summary>
    /// Gets the local position of a character at the specified index.
    /// Useful for positioning a caret or IME composition text.
    /// </summary>
    public Vector2 GetCharacterPosition(int index)
    {
        // Parents (e.g. a text box positioning its caret) update before this drawable does,
        // so a query arriving right after a text change would otherwise read the previous
        // frame's shaping. Shape on demand so callers always get fresh metrics.
        ensureLayout();

        // If there is no text yet, return the starting text offset.
        if (shapedText == null || shapedText.Glyphs.Count == 0 || index < 0)
        {
            Vector2 originRelative = GetAnchorOriginVector(Origin);
            Vector2 availableSpace = DrawSize - ContentSize;
            return new Vector2(
                availableSpace.X * originRelative.X,
                availableSpace.Y * originRelative.Y
            );
        }

        Vector2 originRel = GetAnchorOriginVector(Origin);
        Vector2 availSpace = DrawSize - contentSize;
        Vector2 textOffset = new Vector2(
            availSpace.X * originRel.X,
            availSpace.Y * originRel.Y
        );

        // `index` is a UTF-16 index into the source text (what a caret uses), NOT a glyph-list
        // index. One glyph can span several UTF-16 units (an emoji surrogate pair) or vice versa, so
        // we locate the glyph whose cluster starts at/after `index`, the caret sits at its left edge.
        var glyphs = shapedText.Glyphs;
        for (int gi = 0; gi < glyphs.Count; gi++)
        {
            if (glyphs[gi].StartIndex >= index)
                return new Vector2(glyphs[gi].Position.X, glyphs[gi].Position.Y) + textOffset;
        }

        // Index is at (or past) the end of the text: place the caret at the right edge of the last glyph.
        var lastGlyph = glyphs[^1];
        return new Vector2(lastGlyph.Position.X + lastGlyph.Size.X, lastGlyph.Position.Y) + textOffset;
    }

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

        private int textVertexCount;

        protected override void ApplyVertices(Drawable source)
        {
            var text = (SpriteText)source;

            textVertexCount = text.currentVertexCount;

            // make it grow only, draw only reads up to textVertexCount via the glyph batches
            if (Vertices.Length < textVertexCount)
                Vertices = new Vertex[textVertexCount];

            Array.Copy(text.textVertices, Vertices, textVertexCount);
        }

        public override void ApplyState(Drawable source)
        {
            base.ApplyState(source);
            var text = (SpriteText)source;

            batchCount = 0;

            if (text.shapedText == null || text.shapedText.Glyphs.Count == 0)
                return;

            if (batches.Length < text.shapedText.Glyphs.Count)
                batches = new GlyphBatch[text.shapedText.Glyphs.Count];

            Texture? currentTexture = null;
            int currentVertexStart = 0;
            int currentBatchVertexCount = 0;
            // Every glyph occupies a fixed 4-vertex slot in the buffer (see GenerateVertices),
            // including invisible glyphs such as spaces. This cursor tracks the slot of the
            // current glyph so batches reference the correct vertex range even when some
            // glyphs are skipped.
            int vertexCursor = 0;

            for (int i = 0; i < text.shapedText.Glyphs.Count; i++)
            {
                var glyph = text.shapedText.Glyphs[i];

                // Invisible glyphs (null texture) are not drawn. Flush the current batch so the
                // skipped vertices don't get folded into a neighbouring texture's batch.
                if (glyph.Texture == null)
                {
                    if (currentTexture != null && currentBatchVertexCount > 0)
                    {
                        batches[batchCount++] = new GlyphBatch
                        {
                            Texture = currentTexture,
                            VertexStart = currentVertexStart,
                            VertexCount = currentBatchVertexCount
                        };
                    }
                    currentTexture = null;
                    currentBatchVertexCount = 0;
                    vertexCursor += 4;
                    currentVertexStart = vertexCursor;
                    continue;
                }

                if (currentTexture != null && currentTexture.BackendTexture?.Handle != glyph.Texture.BackendTexture?.Handle)
                {
                    batches[batchCount++] = new GlyphBatch
                    {
                        Texture = currentTexture,
                        VertexStart = currentVertexStart,
                        VertexCount = currentBatchVertexCount
                    };
                    currentVertexStart += currentBatchVertexCount;
                    currentBatchVertexCount = 0;
                }

                currentTexture = glyph.Texture;
                currentBatchVertexCount += 4;
                vertexCursor += 4;
            }

            if (currentTexture != null && currentBatchVertexCount > 0)
            {
                batches[batchCount++] = new GlyphBatch
                {
                    Texture = currentTexture,
                    VertexStart = currentVertexStart,
                    VertexCount = currentBatchVertexCount
                };
            }
        }

        public override void Draw(IRenderer renderer)
        {
            if (DrawAlpha <= 0 || textVertexCount == 0) return;

            renderer.SetBlendMode(Blending);

            for (int i = 0; i < batchCount; i++)
            {
                var batch = batches[i];
                var slice = Vertices.AsSpan(batch.VertexStart, batch.VertexCount);
                renderer.DrawQuads(slice, batch.Texture);
            }
        }
    }
}
