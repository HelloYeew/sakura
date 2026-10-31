// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;

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

    /// <summary>
    /// How many of <see cref="textVertices"/> are the shadow's. They come first, so it's drawn behind the text.
    /// </summary>
    private int shadowVertexCount;

    private ShapedText? shapedText;
    private int lastCacheVersion = -1;

    /// <summary>
    /// The DPI generation the current layout was measured at
    /// </summary>
    private int lastDpiScaleVersion = -1;

    [Resolved]
    private IFontStore fontStore { get; set; } = null!;

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
                MarkSizesChanged();
                Invalidate(InvalidationFlags.DrawInfo);
            }
        }
    }

    private bool truncate;
    private float maxWidth = float.PositiveInfinity;
    private string ellipsis = TextTruncation.DEFAULT_ELLIPSIS;
    private string displayedText = string.Empty;

    /// <summary>
    /// Whether text wider than <see cref="MaxWidth"/> is shortened to fit, with <see cref="Ellipsis"/>
    /// standing in for what was dropped. Has no effect while <see cref="MaxWidth"/> is unlimited.
    /// </summary>
    /// <remarks>
    /// This sprite still sizes itself to its content, truncating is what keeps that content inside the
    /// budget. It does not wrap, the text stays on one line.
    /// </remarks>
    public bool Truncate
    {
        get => truncate;
        set
        {
            if (truncate == value)
                return;

            truncate = value;
            layoutInvalidated = true;
            MarkSizesChanged();
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// The width budget in local pixels used when <see cref="Truncate"/> is set. Unlimited by default.
    /// </summary>
    public float MaxWidth
    {
        get => maxWidth;
        set
        {
            if (maxWidth.Equals(value))
                return;

            maxWidth = value;
            layoutInvalidated = true;
            MarkSizesChanged();
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// Appended to shortened text. Defaults to a single "…". Set it to an empty string to cut with no
    /// marker at all.
    /// </summary>
    public string Ellipsis
    {
        get => ellipsis;
        set
        {
            value ??= string.Empty;

            if (ellipsis == value)
                return;

            ellipsis = value;
            layoutInvalidated = true;
            MarkSizesChanged();
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// The text actually shaped and drawn: <see cref="Text"/> unless it was shortened, in which case a
    /// prefix of it plus <see cref="Ellipsis"/>.
    /// </summary>
    public string DisplayedText
    {
        get
        {
            ensureLayout();
            return displayedText;
        }
    }

    /// <summary>
    /// Whether <see cref="Text"/> did not fit <see cref="MaxWidth"/> and was shortened.
    /// </summary>
    public bool IsTruncated
    {
        get
        {
            ensureLayout();
            return isTruncated;
        }
    }

    private bool isTruncated;

    private float letterSpacing;

    /// <summary>
    /// Extra room between one glyph and the next, in pixels; negative draws them closer. There's none after the last, so
    /// centered text stays centered. It counts towards <see cref="ContentSize"/> and <see cref="MaxWidth"/>.
    /// </summary>
    public float LetterSpacing
    {
        get => letterSpacing;
        set
        {
            if (letterSpacing.Equals(value))
                return;

            letterSpacing = value;
            layoutInvalidated = true;
            MarkSizesChanged();
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    private Func<Vector2, Color>? colorAt;

    /// <summary>
    /// Colors the text by where it is: asked for the color at a point in the text, in its own pixels from its top-left, for each
    /// corner of each glyph, so a color can run across a letter as well as along the word. It takes the place of
    /// <see cref="Drawable.ColorInfo"/> while set. Null by default.
    /// </summary>
    /// <remarks>
    /// It's asked when the text is built or its color changes. For colors that move, call
    /// <see cref="Drawable.Invalidate"/> with <see cref="InvalidationFlags.Color"/> each frame, which doesn't build the glyphs
    /// again. Color glyphs (an emoji's bitmap) keep their own colors, as they do with any tint.
    /// </remarks>
    public Func<Vector2, Color>? ColorAt
    {
        get => colorAt;
        set
        {
            if (colorAt == value)
                return;

            colorAt = value;
            Invalidate(InvalidationFlags.Color);
        }
    }

    private bool shadow;
    private Color shadowColor = Color.Black.WithAlpha(0.5f);
    private Vector2 shadowOffset = new Vector2(0, 2);

    /// <summary>
    /// Whether the text is drawn again behind itself, in <see cref="ShadowColor"/> at <see cref="ShadowOffset"/>. It's a sharp
    /// copy, which costs twice the vertices and no framebuffer; a soft one is the text again in a
    /// <see cref="Containers.BufferedContainer"/> with a blur. It takes no room in layout.
    /// </summary>
    public bool Shadow
    {
        get => shadow;
        set
        {
            if (shadow == value)
                return;

            shadow = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// The color of <see cref="Shadow"/>, its alpha included. Black at half alpha by default.
    /// </summary>
    public Color ShadowColor
    {
        get => shadowColor;
        set
        {
            if (shadowColor == value)
                return;

            shadowColor = value;
            Invalidate(InvalidationFlags.Color);
        }
    }

    /// <summary>
    /// How far <see cref="Shadow"/> sits from the text, in pixels, turning and scaling with it. (0, 2) by default.
    /// </summary>
    public Vector2 ShadowOffset
    {
        get => shadowOffset;
        set
        {
            if (shadowOffset == value)
                return;

            shadowOffset = value;
            Invalidate(InvalidationFlags.DrawInfo);
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
                MarkSizesChanged();
                Invalidate(InvalidationFlags.DrawInfo);
            }
        }
    }

    public override void Update()
    {
        if (fontStore != null && (lastCacheVersion != fontStore.CacheVersion || lastDpiScaleVersion != fontStore.DpiScaleVersion))
        {
            lastCacheVersion = fontStore.CacheVersion;
            lastDpiScaleVersion = fontStore.DpiScaleVersion;
            layoutInvalidated = true;
            MarkSizesChanged();
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
        if (fontStore == null && Dependencies != null)
            DependencyActivator.Inject(this, Dependencies);

        if (fontStore == null) return;

        computingLayout = true;
        try
        {
            float dpiScale = fontStore.DpiScale;

            lastDpiScaleVersion = fontStore.DpiScaleVersion;

            // Shaped through the store rather than by resolving a Font and calling ProcessText: the store
            // caches the result, and only it knows when one has been invalidated by a font being
            // registered, a fallback family changing, or the glyph atlas being cleared. The result is
            // shared with every other drawable showing the same text at the same size, so it is read-only.
            if (truncate)
            {
                // Measures the full text first and only re-shapes when it overflows, so a sprite that
                // fits pays exactly what it did before.
                var truncation = TextTruncation.Apply(fontStore, fontUsage, Text, dpiScale, maxWidth, ellipsis, letterSpacing);
                shapedText = truncation.Shaped;
                displayedText = truncation.Text;
                isTruncated = truncation.Truncated;
            }
            else
            {
                shapedText = fontStore.Shape(fontUsage, Text, dpiScale);
                displayedText = Text;
                isTruncated = false;
            }

            // Assign the backing field directly; the ContentSize getter forces layout, which we're
            // already inside of (guarded by computingLayout).
            contentSize = new Vector2(
                shapedText.BoundingBox.X + Math.Max(0, shapedText.Glyphs.Count - 1) * letterSpacing,
                shapedText.BoundingBox.Y);

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
            shadowVertexCount = 0;
            DrawRectangle = new RectangleF(0, 0, 0, 0); // Reset bounds if empty
            return;
        }

        var glyphs = shapedText.Glyphs;

        shadowVertexCount = shadow ? glyphs.Count * 4 : 0;
        currentVertexCount = shadowVertexCount + glyphs.Count * 4;

        if (textVertices.Length < currentVertexCount)
        {
            int newSize = Math.Max(textVertices.Length * 2, currentVertexCount);
            Array.Resize(ref textVertices, newSize);
        }

        Span<Vertex> vertices = textVertices.AsSpan(0, currentVertexCount);

        // Glyphs are placed through the matrix, so in the size it was built for.
        Vector2 drawSize = AppliedDrawSize;
        Vector2 originRelative = OriginVector;
        Vector2 availableSpace = drawSize - contentSize;

        Vector2 textOffset = new Vector2(
            availableSpace.X * originRelative.X,
            availableSpace.Y * originRelative.Y
        );

        Vector2 safeDrawSize = new Vector2(
            drawSize.X > 0 ? drawSize.X : 1,
            drawSize.Y > 0 ? drawSize.Y : 1
        );
        Vector2 normalizationScale = new Vector2(1.0f / safeDrawSize.X, 1.0f / safeDrawSize.Y);

        // The shadow's offset on screen: the difference of two points, so it turns and scales with the text.
        Vector2 shadowStep = shadow
            ? Vector2.Transform(new Vector2(shadowOffset.X * normalizationScale.X, shadowOffset.Y * normalizationScale.Y), ModelMatrix) - Vector2.Transform(Vector2.Zero, ModelMatrix)
            : Vector2.Zero;

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        for (int glyphIndex = 0; glyphIndex < glyphs.Count; glyphIndex++)
        {
            var glyph = glyphs[glyphIndex];
            var texture = glyph.Texture;
            var pos = glyph.Position;
            var size = glyph.Size;

            // Every glyph has fixed slots: 4 vertices of its own, and 4 of the shadow's if there is one.
            int slot = shadowVertexCount + glyphIndex * 4;
            int shadowSlot = glyphIndex * 4;

            float pixelX = pos.X + glyphIndex * letterSpacing + textOffset.X;
            float pixelY = pos.Y + textOffset.Y;

            // Invisible glyphs (e.g. spaces) carry advance width but have no texture.
            // Emit a zero-area quad so the per-glyph vertex layout stays 1:1 with the glyph
            // list (the draw node skips these batches), while still letting the glyph
            // contribute to the bounding box.
            if (texture == null)
            {
                var p = Vector2.Transform(new Vector2(pixelX * normalizationScale.X, pixelY * normalizationScale.Y), ModelMatrix);

                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);

                var zero = new Vertex { Position = new Vector2(p.X, p.Y), TexCoords = Vector2.Zero };
                vertices.Slice(slot, 4).Fill(zero);

                if (shadow)
                    vertices.Slice(shadowSlot, 4).Fill(zero);

                continue;
            }

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
            var uvTopRight = new Vector2(uvBottomRight.X, uvTopLeft.Y);
            var uvBottomLeft = new Vector2(uvTopLeft.X, uvBottomRight.Y);

            // One indexed quad per glyph (TL, TR, BR, BL). Their colors are written after.
            vertices[slot + 0] = new Vertex { Position = vTopLeft, TexCoords = uvTopLeft };
            vertices[slot + 1] = new Vertex { Position = vTopRight, TexCoords = uvTopRight };
            vertices[slot + 2] = new Vertex { Position = vBottomRight, TexCoords = uvBottomRight };
            vertices[slot + 3] = new Vertex { Position = vBottomLeft, TexCoords = uvBottomLeft };

            if (shadow)
            {
                vertices[shadowSlot + 0] = new Vertex { Position = vTopLeft + shadowStep, TexCoords = uvTopLeft };
                vertices[shadowSlot + 1] = new Vertex { Position = vTopRight + shadowStep, TexCoords = uvTopRight };
                vertices[shadowSlot + 2] = new Vertex { Position = vBottomRight + shadowStep, TexCoords = uvBottomRight };
                vertices[shadowSlot + 3] = new Vertex { Position = vBottomLeft + shadowStep, TexCoords = uvBottomLeft };
            }
        }

        // The text's rectangle, and the same again where the shadow is.
        minX = Math.Min(minX, minX + shadowStep.X);
        minY = Math.Min(minY, minY + shadowStep.Y);
        maxX = Math.Max(maxX, maxX + shadowStep.X);
        maxY = Math.Max(maxY, maxY + shadowStep.Y);

        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);

        writeVertexColors();
    }

    private void flushBatch(IRenderer renderer, Texture texture, int glyphStart, int glyphCount)
    {
        // 4 vertices per glyph (one indexed quad)
        int vertexStart = glyphStart * 4;
        int vertexCount = glyphCount * 4;

        var slice = textVertices.AsSpan(vertexStart, vertexCount);
        renderer.DrawQuads(slice, texture);
    }

    protected override void UpdateDrawColor()
    {
        DrawAlpha = (Parent?.ChildDrawAlpha ?? 1f) * Alpha;

        writeVertexColors();

        // This override does not chain to base.UpdateDrawColor, so it has to stamp the new state itself.
        MarkDrawStateRecomputed();
    }

    /// <summary>
    /// A color as a vertex carries it: linear, with <see cref="Drawable.DrawAlpha"/> and its own alpha in the <c>W</c>.
    /// </summary>
    private Vector4 vertexColor(Color c) => new Vector4(
        ColorExtensions.SrgbToLinear(c.R),
        ColorExtensions.SrgbToLinear(c.G),
        ColorExtensions.SrgbToLinear(c.B),
        DrawAlpha * (c.A / 255f));

    /// <summary>
    /// Writes the color of every vertex: shared by <see cref="GenerateVertices"/> and <see cref="UpdateDrawColor"/>.
    /// </summary>
    private void writeVertexColors()
    {
        var drawColor = vertexColor(Color);

        if (shapedText == null || shapedText.Glyphs.Count == 0)
        {
            for (int i = 0; i < currentVertexCount; i++)
                textVertices[i].Color = drawColor;

            return;
        }

        if (shadowVertexCount > 0)
        {
            var shadowDrawColor = vertexColor(shadowColor);

            for (int i = 0; i < shadowVertexCount; i++)
                textVertices[i].Color = shadowDrawColor;
        }

        // Color glyphs (e.g. color emoji bitmaps) carry their own native colors and must not be
        // recolored by the drawable's tint, only alpha (for fades) should still apply to them.
        var colorGlyphDrawColor = new Vector4(1f, 1f, 1f, drawColor.W);

        ColorInfo info = ColorInfo;
        bool varies = colorAt != null || !info.HasSingleColor;

        // A gradient runs across the text as it does across a box: between its corners, in linear space.
        bool gradient = varies && colorAt == null;
        Vector4 topLeft = gradient ? vertexColor(info.TopLeft) : drawColor;
        Vector4 topRight = gradient ? vertexColor(info.TopRight) : drawColor;
        Vector4 bottomLeft = gradient ? vertexColor(info.BottomLeft) : drawColor;
        Vector4 bottomRight = gradient ? vertexColor(info.BottomRight) : drawColor;

        Vector4 colorOf(float x, float y)
        {
            if (colorAt != null)
                return vertexColor(colorAt(new Vector2(x, y)));

            float across = contentSize.X > 0 ? Math.Clamp(x / contentSize.X, 0, 1) : 0;
            float down = contentSize.Y > 0 ? Math.Clamp(y / contentSize.Y, 0, 1) : 0;

            return Vector4.Lerp(Vector4.Lerp(topLeft, topRight, across), Vector4.Lerp(bottomLeft, bottomRight, across), down);
        }

        var glyphs = shapedText.Glyphs;
        int vIndex = shadowVertexCount;

        for (int glyphIndex = 0; glyphIndex < glyphs.Count && vIndex < currentVertexCount; glyphIndex++)
        {
            var glyph = glyphs[glyphIndex];

            if (!varies || glyph.IsColorGlyph || glyph.Texture == null)
            {
                var glyphColor = glyph.IsColorGlyph ? colorGlyphDrawColor : drawColor;

                for (int i = 0; i < 4 && vIndex < currentVertexCount; i++, vIndex++)
                    textVertices[vIndex].Color = glyphColor;

                continue;
            }

            float left = glyph.Position.X + glyphIndex * letterSpacing;
            float top = glyph.Position.Y;
            float right = left + glyph.Size.X;
            float bottom = top + glyph.Size.Y;

            // In the order of its vertices (TL, TR, BR, BL).
            textVertices[vIndex++].Color = colorOf(left, top);
            textVertices[vIndex++].Color = colorOf(right, top);
            textVertices[vIndex++].Color = colorOf(right, bottom);
            textVertices[vIndex++].Color = colorOf(left, bottom);
        }
    }

    protected override DrawNode CreateDrawNode() => new SpriteTextDrawNode();

    /// <summary>
    /// Gets the local position of a character at the specified index.
    /// Useful for positioning a caret or IME composition text.
    /// </summary>
    /// <remarks>
    /// The index runs over <see cref="DisplayedText"/>, which differs from <see cref="Text"/> while
    /// <see cref="IsTruncated"/> — a caret should not be driven from a truncating sprite.
    /// </remarks>
    public Vector2 GetCharacterPosition(int index)
    {
        // Parents (e.g. a text box positioning its caret) update before this drawable does,
        // so a query arriving right after a text change would otherwise read the previous
        // frame's shaping. Shape on demand so callers always get fresh metrics.
        ensureLayout();

        // If there is no text yet, return the starting text offset.
        if (shapedText == null || shapedText.Glyphs.Count == 0 || index < 0)
        {
            Vector2 originRelative = OriginVector;
            Vector2 availableSpace = DrawSize - ContentSize;
            return new Vector2(
                availableSpace.X * originRelative.X,
                availableSpace.Y * originRelative.Y
            );
        }

        Vector2 originRel = OriginVector;
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
                return new Vector2(glyphs[gi].Position.X + gi * letterSpacing, glyphs[gi].Position.Y) + textOffset;
        }

        // Index is at (or past) the end of the text: place the caret at the right edge of the last glyph.
        var lastGlyph = glyphs[^1];
        return new Vector2(lastGlyph.Position.X + lastGlyph.Size.X + (glyphs.Count - 1) * letterSpacing, lastGlyph.Position.Y) + textOffset;
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

        protected override void ApplyVertices(Drawable source)
        {
            var text = (SpriteText)source;

            // The source buffer is itself grow-only, so it is sliced to the live count rather than copied
            // whole. The node's storage is grow-only too (see DrawNode.SetVertexCount).
            SetVertexCount(text.currentVertexCount);
            text.textVertices.AsSpan(0, VertexCount).CopyTo(WritableVertices);
        }

        public override void ApplyState(Drawable source)
        {
            base.ApplyState(source);
            var text = (SpriteText)source;

            batchCount = 0;

            if (text.shapedText == null || text.shapedText.Glyphs.Count == 0)
                return;

            int glyphCount = text.shapedText.Glyphs.Count;
            int mostBatches = text.shadowVertexCount > 0 ? glyphCount * 2 : glyphCount;

            if (batches.Length < mostBatches)
                batches = new GlyphBatch[mostBatches];

            // The shadow's first, so it's drawn behind: the same glyphs, in the vertices before the text's.
            if (text.shadowVertexCount > 0)
                addBatches(text.shapedText, 0);

            addBatches(text.shapedText, text.shadowVertexCount);
        }

        private void addBatches(ShapedText shaped, int firstVertex)
        {
            Texture? currentTexture = null;
            int currentVertexStart = firstVertex;
            int currentBatchVertexCount = 0;
            // Every glyph occupies a fixed 4-vertex slot in the buffer (see GenerateVertices),
            // including invisible glyphs such as spaces. This cursor tracks the slot of the
            // current glyph so batches reference the correct vertex range even when some
            // glyphs are skipped.
            int vertexCursor = firstVertex;

            for (int i = 0; i < shaped.Glyphs.Count; i++)
            {
                var glyph = shaped.Glyphs[i];

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
            if (DrawAlpha <= 0 || VertexCount == 0) return;

            renderer.SetBlendMode(Blending);

            for (int i = 0; i < batchCount; i++)
            {
                var batch = batches[i];
                renderer.DrawQuads(Vertices.Slice(batch.VertexStart, batch.VertexCount), batch.Texture);
            }
        }
    }
}
