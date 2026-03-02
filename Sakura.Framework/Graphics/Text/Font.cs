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
using Logger = Sakura.Framework.Logging.Logger;

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
    private float currentGlyphScale = 1.0f;

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

    public ShapedText ProcessText(string text, float fontSize, float dpiScale = 1.0f, IEnumerable<Font>? fallbacks = null)
    {
        if (string.IsNullOrEmpty(text))
            return ShapedText.Empty;

        float renderFontSize = fontSize * dpiScale;

        // Determine base vertical metrics from the PRIMARY font so line height stays consistent
        float ascenderPx = 0;
        float lineHeightPx = Size;

        lock (stateLock)
        {
            updateFontSize(renderFontSize);
            unsafe
            {
                var face = (FT_FaceRec_*)faceHandle;
                ascenderPx = face->size->metrics.ascender / 64f;
                lineHeightPx = face->size->metrics.height / 64f;
            }
        }

        // Segment the text into runs based on font support
        var glyphs = new List<TextGlyph>();
        float cursorX = 0;

        int currentRunStart = 0;
        Font? currentFont = null;

        int i = 0;
        while (i < text.Length)
        {
            // Handle surrogate pairs properly (e.g., Emojis)
            int charLen = char.IsSurrogatePair(text, i) ? 2 : 1;
            uint codepoint = (uint)char.ConvertToUtf32(text, i);

            // Find which font supports this codepoint
            Font assignedFont = this;
            if (!HasGlyph(codepoint))
            {
                if (fallbacks != null)
                {
                    foreach (var fallback in fallbacks)
                    {
                        if (fallback.HasGlyph(codepoint))
                        {
                            assignedFont = fallback;
                            break;
                        }
                    }
                }
            }

            // If the font changed, process the previous run
            if (currentFont != assignedFont)
            {
                if (currentFont != null)
                {
                    string runText = text.Substring(currentRunStart, i - currentRunStart);
                    var runGlyphs = currentFont.shapeRun(runText, renderFontSize, dpiScale, ascenderPx, ref cursorX);
                    glyphs.AddRange(runGlyphs);
                }
                currentFont = assignedFont;
                currentRunStart = i;
            }

            i += charLen;
        }

        // 3. Process the final remaining run
        if (currentFont != null && currentRunStart < text.Length)
        {
            string runText = text.Substring(currentRunStart);
            var runGlyphs = currentFont.shapeRun(runText, renderFontSize, dpiScale, ascenderPx, ref cursorX);
            glyphs.AddRange(runGlyphs);
        }

        return new ShapedText(glyphs, new Vector2(cursorX / dpiScale, lineHeightPx / dpiScale));
    }

    private void updateFontSize(float renderFontSize)
    {
        if (Math.Abs(currentPhysicalSize - renderFontSize) > 0.01f)
        {
            currentPhysicalSize = renderFontSize;
            currentGlyphScale = 1.0f; // Reset scale for normal fonts

            unsafe
            {
                var face = (FT_FaceRec_*)faceHandle;

                // Try to set the exact scalable pixel size
                var err = FT.FT_Set_Pixel_Sizes(face, 0, (uint)currentPhysicalSize);

                // If the font is a bitmap-only font (like Color Emojis), it will fail if the size isn't exact.
                if (err != FT_Error.FT_Err_Ok && face->num_fixed_sizes > 0)
                {
                    // Fallback to the first available fixed size strike
                    FT.FT_Select_Size(face, 0);

                    // Pull the height directly from the fixed size strike array
                    float actualSize = face->available_sizes[0].height;
                    if (actualSize > 0)
                    {
                        currentGlyphScale = currentPhysicalSize / actualSize;
                    }
                }
            }
            hbFont.SetScale((int)(currentPhysicalSize * 64), (int)(currentPhysicalSize * 64));
        }
    }

    private List<TextGlyph> shapeRun(string text, float renderFontSize, float dpiScale, float baselineY, ref float cursorX)
    {
        var glyphs = new List<TextGlyph>();

        lock (stateLock)
        {
            updateFontSize(renderFontSize);

            sharedBuffer.ClearContents();
            sharedBuffer.AddUtf16(text);
            sharedBuffer.GuessSegmentProperties();
            hbFont.Shape(sharedBuffer);

            int length = sharedBuffer.Length;
            var info = sharedBuffer.GlyphInfos;
            var pos = sharedBuffer.GlyphPositions;

            for (int i = 0; i < length; i++)
            {
                // HarfBuzz info[i].Codepoint is actually the specific glyph index for THIS font face.
                uint glyphIndex = info[i].Codepoint;

                float xAdvance = pos[i].XAdvance / 64.0f;

                // Cache by glyph index and size.
                var cacheKey = (glyphIndex, renderFontSize);

                if (!glyphCache.TryGetValue(cacheKey, out GlyphData data))
                {
                    var loaded = rasterizeGlyph(glyphIndex);
                    if (loaded.HasValue)
                    {
                        data = loaded.Value;
                        glyphCache[cacheKey] = data;
                    }
                    else
                    {
                        // Move the cursor even if it's an invisible character like a space
                        cursorX += xAdvance;
                        continue;
                    }
                }

                float hbXOffset = pos[i].XOffset / 64.0f;
                float hbYOffset = pos[i].YOffset / 64.0f;

                float scaledLeft = data.BitmapLeft * currentGlyphScale;
                float scaledTop = data.BitmapTop * currentGlyphScale;
                float scaledWidth = data.Texture.Width * currentGlyphScale;
                float scaledHeight = data.Texture.Height * currentGlyphScale;

                float finalX = cursorX + hbXOffset + scaledLeft;
                float finalY = baselineY - hbYOffset - scaledTop;

                glyphs.Add(new TextGlyph
                {
                    Texture = data.Texture,
                    Position = new Vector2(finalX / dpiScale, finalY / dpiScale),
                    Size = new Vector2(scaledWidth / dpiScale, scaledHeight / dpiScale)
                });

                cursorX += xAdvance;
            }
        }

        return glyphs;
    }

    private unsafe GlyphData? rasterizeGlyph(uint glyphIndex)
    {
        var facePtr = (FT_FaceRec_*)faceHandle;

        // FT_LOAD_COLOR is 1 << 20 in FreeType. This tells FreeType to load colored emoji bitmaps if they exist.
        const int FT_LOAD_COLOR = 1 << 20;
        int loadFlags = 0 | FT_LOAD_COLOR; // 0 is FT_LOAD_DEFAULT

        // Load glyph
        var err = FT.FT_Load_Glyph(facePtr, glyphIndex, (FreeTypeSharp.FT_LOAD)loadFlags);
        if (err != FT_Error.FT_Err_Ok)
        {
            Logger.Error($"Failed to load glyph index {glyphIndex} with FT_LOAD_COLOR. Error: {err}. Retrying without color flag.");
            err = FT.FT_Load_Glyph(facePtr, glyphIndex, FreeTypeSharp.FT_LOAD.FT_LOAD_DEFAULT);
            if (err != FT_Error.FT_Err_Ok) return null;
        }

        var glyphSlotPtr = facePtr->glyph;

        // Only render if the glyph is a vector outline
        // If it's an emoji, it will already be FT_GLYPH_FORMAT_BITMAP
        if (glyphSlotPtr->format != FreeTypeSharp.FT_Glyph_Format_.FT_GLYPH_FORMAT_BITMAP)
        {
            err = FT.FT_Render_Glyph(glyphSlotPtr, FreeTypeSharp.FT_Render_Mode_.FT_RENDER_MODE_NORMAL);
            if (err != FT_Error.FT_Err_Ok) return null;
        }

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

        int pitch = Math.Abs(bitmap.pitch);

        if (bitmap.pixel_mode == FT_Pixel_Mode_.FT_PIXEL_MODE_BGRA)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int src = y * pitch + x * 4;
                    int dst = (y * width + x) * 4;
                    // Convert BGRA to RGBA for OpenGL
                    rgba[dst + 0] = buffer[src + 2]; // R
                    rgba[dst + 1] = buffer[src + 1]; // G
                    rgba[dst + 2] = buffer[src + 0]; // B
                    rgba[dst + 3] = buffer[src + 3]; // A
                }
            }
        }
        else
        {
            // Standard grayscale anti-aliased font
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int src = y * pitch + x;
                    int dst = (y * width + x) * 4;
                    byte val = buffer[src];
                    rgba[dst + 0] = 255;
                    rgba[dst + 1] = 255;
                    rgba[dst + 2] = 255;
                    rgba[dst + 3] = val;
                }
            }
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

    public bool HasGlyph(uint codepoint)
    {
        unsafe
        {
            // FT_Get_Char_Index returns 0 if the glyph is missing
            return FT.FT_Get_Char_Index((FT_FaceRec_*)faceHandle, codepoint) > 0;
        }
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
