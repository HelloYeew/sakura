// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using FreeTypeSharp;
using HarfBuzzSharp;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Text;

/// <summary>
/// Represents a font loaded from a font file, capable of shaping and rasterizing text.
/// </summary>
public class Font : IDisposable
{
    private readonly TextureAtlas atlas;

    private readonly FreeTypeLibrary library;
    private readonly IntPtr faceHandle;

    private readonly HarfBuzzSharp.Blob hbBlob;
    private readonly HarfBuzzSharp.Face hbFace;
    private readonly HarfBuzzSharp.Font hbFont;

    private readonly HarfBuzzSharp.Buffer sharedBuffer = new HarfBuzzSharp.Buffer();

    // Cache stores GlyphData instead of just Texture, because we need bearing info per glyph
    // Key by (CodePoint, Size) so multiple sizes can be cached in the same Font instance.
    private readonly Dictionary<(uint CodePoint, float PhysicalSize), GlyphData> glyphCache = new();

    private float currentPhysicalSize = 24;

    private readonly Lock stateLock = new Lock();

    public string Name { get; }
    public float Size { get; private set; } = 24;

    /// <summary>
    /// Helper struct to cache texture and placement info
    /// </summary>
    private struct GlyphData
    {
        public Texture Texture;
        public int BitmapLeft;
        public int BitmapTop;
    }

    public Font(string name, byte[] fontData, TextureAtlas atlas)
    {
        Name = name;
        this.atlas = atlas;

        library = new FreeTypeLibrary();

        pinnedFontData = GCHandle.Alloc(fontData, GCHandleType.Pinned);
        IntPtr fontPtr = pinnedFontData.AddrOfPinnedObject();

        unsafe
        {
            IntPtr facePtr = IntPtr.Zero;
            FT_Error err = FT.FT_New_Memory_Face(library.Native, (byte*)fontPtr, fontData.Length, 0, (FT_FaceRec_**)&facePtr);
            if (err != FT_Error.FT_Err_Ok) throw new Exception($"Failed to load font face: {err}");
            faceHandle = facePtr;
        }

        hbBlob = new Blob(fontPtr, fontData.Length, MemoryMode.Duplicate);
        hbFace = new Face(hbBlob, 0);
        hbFont = new HarfBuzzSharp.Font(hbFace);
    }

    private GCHandle pinnedFontData;

    public ShapedText ProcessText(string text, float fontSize, float dpiScale = 1.0f)
    {
        if (string.IsNullOrEmpty(text))
            return ShapedText.Empty;

        lock (stateLock)
        {
            float renderFontSize = fontSize * dpiScale;

            // 1. Update Font Size if needed
            if (Math.Abs(currentPhysicalSize - renderFontSize) > 0.01f)
            {
                currentPhysicalSize = renderFontSize;
                unsafe
                {
                    FT.FT_Set_Pixel_Sizes((FT_FaceRec_*)faceHandle, 0, (uint)currentPhysicalSize);
                }
                hbFont.SetScale((int)(currentPhysicalSize * 64), (int)(currentPhysicalSize * 64));
            }

            // 2. Get Vertical Metrics
            float ascenderPx = 0;
            float lineHeightPx = Size;

            unsafe
            {
                var face = (FT_FaceRec_*)faceHandle;
                ascenderPx = face->size->metrics.ascender / 64f;
                lineHeightPx = face->size->metrics.height / 64f;
            }

            // 3. Shape Text
            sharedBuffer.ClearContents();
            sharedBuffer.AddUtf16(text);
            sharedBuffer.GuessSegmentProperties();
            hbFont.Shape(sharedBuffer);

            int length = sharedBuffer.Length;
            var info = sharedBuffer.GlyphInfos;
            var pos = sharedBuffer.GlyphPositions;

            var glyphs = new List<TextGlyph>(length);

            float cursorX = 0;
            float baselineY = ascenderPx;

            for (int i = 0; i < length; i++)
            {
                uint codepoint = info[i].Codepoint;

                float xAdvance = pos[i].XAdvance / 64.0f;
                float yAdvance = pos[i].YAdvance / 64.0f;

                // Since change render to DPI scaling, change to cache by real render size
                var cacheKey = (codepoint, renderFontSize);

                if (!glyphCache.TryGetValue(cacheKey, out GlyphData data))
                {
                    // Rasterize will use the current 'Size' set above
                    var loaded = rasterizeGlyph(codepoint);
                    if (loaded.HasValue)
                    {
                        data = loaded.Value;
                        glyphCache[cacheKey] = data;
                    }
                    else
                    {
                        cursorX += xAdvance;
                        continue;
                    }
                }

                float hbXOffset = pos[i].XOffset / 64.0f;
                float hbYOffset = pos[i].YOffset / 64.0f;

                float finalX = cursorX + hbXOffset + data.BitmapLeft;
                float finalY = baselineY - hbYOffset - data.BitmapTop;

                glyphs.Add(new TextGlyph
                {
                    Texture = data.Texture,
                    Position = new Vector2(finalX / dpiScale, finalY / dpiScale),
                    Size = new Vector2(data.Texture.Width / dpiScale, data.Texture.Height / dpiScale)
                });

                cursorX += xAdvance;
            }

            return new ShapedText(glyphs, new Vector2(cursorX / dpiScale, lineHeightPx / dpiScale));
        }
    }

    private unsafe GlyphData? rasterizeGlyph(uint glyphIndex)
    {
        var facePtr = (FT_FaceRec_*)faceHandle;
        const int ft_load_default = 0;

        // Load glyph
        var err = FT.FT_Load_Glyph(facePtr, glyphIndex, ft_load_default);
        if (err != FT_Error.FT_Err_Ok) return null;

        var glyphSlotPtr = facePtr->glyph;

        // Render to bitmap
        err = FT.FT_Render_Glyph(glyphSlotPtr, FT_Render_Mode_.FT_RENDER_MODE_NORMAL);
        if (err != FT_Error.FT_Err_Ok) return null;

        // Get bitmap info
        FT_Bitmap_ bitmap = glyphSlotPtr->bitmap;
        if (bitmap.width == 0 || bitmap.rows == 0)
        {
            // Even if empty (e.g. space char), we return data with null texture but valid advance/metrics if needed.
            // But for now, we just return null and let spacing handle via Advance.
            // Actually, 'space' usually produces empty bitmap but has Advance.
            // Our loop handles Advance regardless of texture existence,
            // but we need to ensure we don't crash on texture creation.
            // For this simple implementation, we assume we only draw visible glyphs.
             return null;
        }

        int width = (int)bitmap.width;
        int height = (int)bitmap.rows;
        byte[] rgba = new byte[width * height * 4];
        byte* buffer = bitmap.buffer;

        for (int i = 0; i < width * height; i++)
        {
            byte val = buffer[i];
            rgba[i * 4 + 0] = 255;
            rgba[i * 4 + 1] = 255;
            rgba[i * 4 + 2] = 255;
            rgba[i * 4 + 3] = val;
        }

        var texture = atlas.AddRegion(width, height, rgba);
        if (texture == null) return null;

        return new GlyphData
        {
            Texture = texture,
            BitmapLeft = glyphSlotPtr->bitmap_left,
            BitmapTop = glyphSlotPtr->bitmap_top
        };
    }

    public void Dispose()
    {
        sharedBuffer.Dispose();
        hbFont.Dispose();
        hbFace.Dispose();
        hbBlob.Dispose();

        if (faceHandle != IntPtr.Zero)
        {
            unsafe { FT.FT_Done_Face((FT_FaceRec_*)faceHandle); }
        }

        library.Dispose();

        if (pinnedFontData.IsAllocated)
            pinnedFontData.Free();
    }
}

public struct TextGlyph
{
    public Texture Texture;
    public Vector2 Position;
    public Vector2 Size;
}

public class ShapedText
{
    public List<TextGlyph> Glyphs { get; }
    public Vector2 BoundingBox { get; }
    public static ShapedText Empty => new ShapedText(new List<TextGlyph>(), Vector2.Zero);

    public ShapedText(List<TextGlyph> glyphs, Vector2 bounds)
    {
        Glyphs = glyphs;
        BoundingBox = bounds;
    }
}
