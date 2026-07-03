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
using Sakura.Framework.Statistic;
using Sakura.Framework.Utilities;
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

    /// <summary>
    /// <remarks>
    /// Cache stores <see cref="GlyphData"/> instead of just <see cref="Texture"/>, because we need bearing info per glyph.
    /// Key by (glyph index, physical size, variation) so one <see cref="Font"/> instance can cache multiple sizes
    /// and multiple variable-font instances (weights, fills, …) without collisions. For static fonts
    /// the variation is always the default, so the extra key component is inert.
    /// </remarks>
    /// </summary>
    private readonly Dictionary<(uint CodePoint, float PhysicalSize, FontVariation Variation), GlyphData> glyphCache = new Dictionary<(uint CodePoint, float PhysicalSize, FontVariation Variation), GlyphData>();

    private float currentPhysicalSize = 24;
    private float currentGlyphScale = 1.0f;

    /// <summary>
    /// Whether the FreeType face size / bitmap strike has actually been applied at least once. The
    /// face starts out with no size selected, so the very first <see cref="updateFontSize"/> must run
    /// even when the requested size happens to equal <see cref="currentPhysicalSize"/>'s initial value.
    /// This matters for bitmap-only color fonts (e.g. NotoColorEmoji, CBDT/CBLC): if the strike is
    /// never selected, glyph loading yields no bitmap and the emoji renders as nothing. The bug only
    /// surfaced at exactly the default size with an unscaled display (dpiScale 1, size 24 → renderFontSize 24),
    /// which is why it appeared on Windows but was masked on HiDPI/retina macOS (dpiScale ≥ 2).
    /// </summary>
    private bool fontSizeApplied;

    private FontVariation currentVariation = FontVariation.None;

    private readonly bool isVariable;

    /// <summary>
    /// Native byte width of a FreeType <c>FT_Fixed</c> / <c>FT_ULong</c> (C <c>long</c>) for the loaded
    /// native binary, detected at runtime from the axis records. The host ABI is not a reliable guide:
    /// some bundled FreeType builds (observed under virtualized Windows) are LP64 (8-byte <c>long</c>)
    /// even though 64-bit Windows itself is LLP64 (4-byte). Defaults to pointer width until probed.
    /// </summary>
    private int axisLongSize = IntPtr.Size;

    /// <summary>
    /// Variation axes exposed by this face, in the face's native axis order (order matters for
    /// <see cref="FreeTypeVariations.FT_Set_Var_Design_Coordinates"/>). Empty for static fonts.
    /// </summary>
    private readonly List<FontAxis> axisTable = new List<FontAxis>();

    private readonly Lock stateLock = new Lock();

    public string Name { get; }
    public float Size { get; private set; } = 24;

    /// <summary>
    /// True when this font is an OpenType variable font (has a <c>MULTIPLE_MASTERS</c> / <c>fvar</c>
    /// table). A single variable <see cref="Font"/> can render many weights/fills from one file.
    /// </summary>
    public bool IsVariable => isVariable;

    /// <summary>
    /// The variation axes this face exposes (tag + min/default/max), in the face's native order.
    /// Empty for static fonts.
    /// </summary>
    public IReadOnlyList<FontAxis> Axes => axisTable;

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

        // Detect variable fonts and read their axis table once at load time. Static fonts skip all
        // of this and behave exactly as before.
        unsafe
        {
            long faceFlags = (long)((FT_FaceRec_*)faceHandle)->face_flags;
            isVariable = (faceFlags & FreeTypeVariations.FT_FACE_FLAG_MULTIPLE_MASTERS) != 0;
        }

        if (isVariable)
        {
            readVariationAxes();

            if (axisTable.Count > 0)
                Logger.Debug($"Font '{Name}' is variable with axes: {string.Join(", ", axisTable)}");
            else
                Logger.Debug($"Font '{Name}' reports variable but no axes could be read; treating as static.");
        }
    }

    /// <summary>
    /// Reads the variation axis descriptors from FreeType once, storing tag + min/default/max in the
    /// face's native axis order so requested instances can be clamped and applied.
    /// </summary>
    private void readVariationAxes()
    {
        if (FreeTypeVariations.FT_Get_MM_Var(faceHandle, out nint mm) != FT_Error.FT_Err_Ok || mm == 0)
            return;

        try
        {
            // FT_MM_Var has only uint/pointer fields, so it marshals correctly on every data model.
            var header = Marshal.PtrToStructure<FT_MM_Var>(mm);

            if (header.num_axis == 0)
                return;

            // FT_Var_Axis must *not* go through Marshal.PtrToStructure: its FT_Fixed/FT_ULong fields are
            // C 'long', whose width the native binary does not always match to the host ABI (some
            // FreeType builds under virtualized Windows are LP64 with an 8-byte long even though Windows
            // is normally LLP64). So detect the width from the first record, then walk the records by
            // hand. Native layout of each record:
            //   name  : FT_String*     (pointer-sized)
            //   min   : FT_Fixed 16.16 (longSize)
            //   def   : FT_Fixed 16.16 (longSize)
            //   max   : FT_Fixed 16.16 (longSize)
            //   tag   : FT_ULong       (longSize) — packed 4-char axis tag
            //   strid : FT_UInt        (4)
            // https://github.com/HelloYeew/sakura/pull/132
            int ptrSize = IntPtr.Size;
            int longSize = detectAxisLongSize(header.axis);
            axisLongSize = longSize;
            int recSize = alignUp(ptrSize + 4 * longSize + 4, ptrSize);

            for (int i = 0; i < header.num_axis; i++)
            {
                nint fields = header.axis + i * recSize + ptrSize; // skip the FT_String* name

                // The 16.16 fixed values fit in 32 bits (including negatives, e.g. GRAD -50) and every
                // supported target is little-endian, so reading the low Int32 of each field is correct
                // regardless of the C-long width — only the field offsets depend on it.
                int min = Marshal.ReadInt32(fields, 0 * longSize);
                int def = Marshal.ReadInt32(fields, 1 * longSize);
                int max = Marshal.ReadInt32(fields, 2 * longSize);
                uint tag = (uint)Marshal.ReadInt32(fields, 3 * longSize);

                axisTable.Add(new FontAxis(tag, min / 65536f, def / 65536f, max / 65536f));
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to read variation axes for font '{Name}': {ex.Message}");
            axisTable.Clear();
        }
        finally
        {
            unsafe
            {
                FreeTypeVariations.FT_Done_MM_Var((nint)library.Native, mm);
            }
        }
    }

    private GCHandle pinnedFontData;

    public ShapedText ProcessText(string text, float fontSize, float dpiScale = 1.0f, IEnumerable<Font>? fallbacks = null, FontVariation variation = default)
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
            updateVariation(variation);
            unsafe
            {
                var face = (FT_FaceRec_*)faceHandle;
                ascenderPx = face->size->metrics.ascender / 64f;
                lineHeightPx = face->size->metrics.height / 64f;
                if (lineHeightPx <= 0)
                    lineHeightPx = renderFontSize;
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
            // Handle surrogate pairs properly (e.g., Emojis). Guard against a lone/unpaired surrogate
            // (which can briefly occur mid-edit, e.g. a caret split across an emoji): treat it as a
            // single code unit and let it resolve to .notdef instead of throwing in ConvertToUtf32.
            int charLen;
            uint codepoint;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = (uint)char.ConvertToUtf32(text[i], text[i + 1]);
                charLen = 2;
            }
            else
            {
                codepoint = text[i];
                charLen = 1;
            }

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
                    var runGlyphs = currentFont.shapeRun(runText, renderFontSize, dpiScale, ascenderPx, variation, currentRunStart, ref cursorX);
                    glyphs.AddRange(runGlyphs);
                }
                currentFont = assignedFont;
                currentRunStart = i;
            }

            i += charLen;
        }

        // Process the final remaining run
        if (currentFont != null && currentRunStart < text.Length)
        {
            string runText = text.Substring(currentRunStart);
            var runGlyphs = currentFont.shapeRun(runText, renderFontSize, dpiScale, ascenderPx, variation, currentRunStart, ref cursorX);
            glyphs.AddRange(runGlyphs);
        }

        // If the font had 0 advance (common for icons), compute width from the actual glyph bounds
        float finalWidth = cursorX;
        if (finalWidth <= 0 && glyphs.Count > 0)
        {
            float maxRight = 0;
            foreach (var g in glyphs)
            {
                // g.Position and g.Size are already divided by dpiScale, so we scale them back for this check
                float right = (g.Position.X + g.Size.X) * dpiScale;
                if (right > maxRight) maxRight = right;
            }
            finalWidth = maxRight;
        }

        return new ShapedText(glyphs, new Vector2(finalWidth / dpiScale, lineHeightPx / dpiScale));
    }

    private void updateFontSize(float renderFontSize)
    {
        // Always run on the first call: the face has no size/strike selected until we set one, so a
        // request that coincidentally matches the initial currentPhysicalSize must not be skipped
        // (otherwise bitmap-only fonts never select a strike and produce no glyphs).
        if (!fontSizeApplied || !Precision.AlmostEquals(currentPhysicalSize, renderFontSize, 0.01f))
        {
            fontSizeApplied = true;
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

    /// <summary>
    /// Applies a variable-font instance to both FreeType (drives rasterization) and HarfBuzz (drives
    /// shaping/advances and advance widths differ per weight, so the two must stay in sync). Runs under
    /// <see cref="stateLock"/> like <see cref="updateFontSize"/> and only re-applies when the
    /// coordinates actually change. No-op for static fonts, so passing a variation to a static font is
    /// harmless (full backward compatibility).
    /// </summary>
    private void updateVariation(FontVariation variation)
    {
        if (!isVariable || axisTable.Count == 0)
            return;

        if (variation.Equals(currentVariation))
            return;

        currentVariation = variation;

        int n = axisTable.Count;
        int longSize = axisLongSize;
        var hbVariations = new HarfBuzzSharp.Variation[n];

        // FreeType wants an array of native FT_Fixed (C 'long', 16.16). Build it in unmanaged memory at
        // the native library's element width (detected in readVariationAxes) instead of relying on
        // CLong[] marshalling, which mis-sizes elements.
        nint coords = Marshal.AllocHGlobal(n * longSize);

        try
        {
            for (int i = 0; i < n; i++)
            {
                var axis = axisTable[i];

                // Use the requested value for this axis if present, else the axis default. Unknown axes
                // in the request are simply never matched here, so they're ignored as intended.
                float value = variation.Get(axis.Tag) ?? axis.Default;

                // Guard against a degenerate axis range (min > max) so a bad axis read can never throw
                // ArgumentException here; Math.Clamp requires min <= max.
                float lo = MathF.Min(axis.Minimum, axis.Maximum);
                float hi = MathF.Max(axis.Minimum, axis.Maximum);
                value = Math.Clamp(value, lo, hi);

                // All axis values are small (< ~1000), so value * 65536 fits comfortably in int.
                int fixedValue = (int)MathF.Round(value * 65536f);

                if (longSize == 4)
                    Marshal.WriteInt32(coords, i * longSize, fixedValue);
                else
                    Marshal.WriteInt64(coords, i * longSize, fixedValue);

                hbVariations[i] = new HarfBuzzSharp.Variation { Tag = axis.Tag, Value = value };
            }

            var err = FreeTypeVariations.FT_Set_Var_Design_Coordinates(faceHandle, (uint)n, coords);
            if (err != FT_Error.FT_Err_Ok)
                Logger.Error($"FT_Set_Var_Design_Coordinates failed for font '{Name}': {err}");
        }
        finally
        {
            Marshal.FreeHGlobal(coords);
        }

        // Keep HarfBuzz in sync so shaped advances match the rasterized weight.
        hbFont.SetVariations(hbVariations);
    }

    /// <summary>
    /// Rounds <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>
    /// </summary>
    private static int alignUp(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

    private static int? cachedAxisLongSize;

    /// <summary>
    /// Probes the native byte width of FreeType's <c>FT_Fixed</c> / <c>FT_ULong</c> (C <c>long</c>) by
    /// reading the first axis record both ways and keeping the interpretation that yields a printable
    /// 4-character axis tag together with a sane, in-range <c>min &lt;= default &lt;= max</c> ordering.
    /// This tolerates native libraries whose <c>long</c> width disagrees with the host ABI (observed
    /// with FreeType under virtualized Windows, which reported LP64 8-byte longs). The result is the
    /// same for every face in a process, so it is cached after the first successful probe.
    /// </summary>
    private static int detectAxisLongSize(nint firstRecord)
    {
        if (cachedAxisLongSize is int cached)
            return cached;

        // Try pointer width first (8 on LP64), then 4 (LLP64). On 32-bit both are 4, so only probe once.
        ReadOnlySpan<int> candidates = IntPtr.Size == 4 ? stackalloc[] { 4 } : stackalloc[] { IntPtr.Size, 4 };

        foreach (int size in candidates)
        {
            nint fields = firstRecord + IntPtr.Size; // skip the FT_String* name

            int min = Marshal.ReadInt32(fields, 0 * size);
            int def = Marshal.ReadInt32(fields, 1 * size);
            int max = Marshal.ReadInt32(fields, 2 * size);
            uint tag = (uint)Marshal.ReadInt32(fields, 3 * size);

            bool plausibleRange = min <= def && def <= max
                                  && MathF.Abs(max / 65536f) < 100_000f
                                  && MathF.Abs(min / 65536f) < 100_000f;

            if (isPrintableTag(tag) && plausibleRange)
            {
                cachedAxisLongSize = size;
                return size;
            }
        }

        // Nothing validated; fall back to pointer width (correct for the common LP64 desktop case).
        cachedAxisLongSize = IntPtr.Size;
        return cachedAxisLongSize.Value;
    }

    /// <summary>True when all four bytes of a packed OpenType axis tag are printable ASCII.</summary>
    private static bool isPrintableTag(uint tag)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            byte b = (byte)(tag >> shift);
            if (b < 0x20 || b > 0x7E)
                return false;
        }

        return true;
    }

    private List<TextGlyph> shapeRun(string text, float renderFontSize, float dpiScale, float baselineY, FontVariation variation, int runOffset, ref float cursorX)
    {
        var glyphs = new List<TextGlyph>();

        lock (stateLock)
        {
            updateFontSize(renderFontSize);
            updateVariation(variation);

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

                // Cluster is the UTF-16 offset of this glyph within the run by add the run's start
                // offset to get the index into the full text. Consumers (e.g. caret positioning)
                // map a string index to a glyph through this, so an emoji (2 UTF-16 units, 1 glyph)
                // stays aligned.
                int startIndex = runOffset + (int)info[i].Cluster;

                float xAdvance = pos[i].XAdvance / 64.0f;

                // Cache by glyph index, size, and variation so distinct weights/fills of the same
                // glyph coexist as separate atlas entries instead of colliding.
                var cacheKey = (glyphIndex, renderFontSize, variation);

                if (!glyphCache.TryGetValue(cacheKey, out GlyphData data))
                {
                    var loaded = rasterizeGlyph(glyphIndex);
                    if (loaded.HasValue)
                    {
                        data = loaded.Value;
                        glyphCache[cacheKey] = data;
                        GlobalStatistics.Get<int>("Fonts", "Cached Glyphs").Value = glyphCache.Count;
                    }
                    else
                    {
                        // Invisible characters such as spaces produce no bitmap, but they still
                        // occupy horizontal space. We must record them as glyphs (with no texture)
                        // so that the glyph list stays 1:1 with the shaped characters. Consumers
                        // that position a caret rely on this alignment, and a missing space glyph
                        // would otherwise make the caret stick to the previous character.
                        glyphs.Add(new TextGlyph
                        {
                            Texture = null,
                            Position = new Vector2(cursorX / dpiScale, baselineY / dpiScale),
                            Size = new Vector2(xAdvance / dpiScale, 0),
                            StartIndex = startIndex
                        });
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
                    Size = new Vector2(scaledWidth / dpiScale, scaledHeight / dpiScale),
                    StartIndex = startIndex
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

    public void ClearCache()
    {
        lock (stateLock)
        {
            glyphCache.Clear();
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

/// <summary>
/// Describes a single OpenType variation axis exposed by a variable font: its packed 4-char tag and
/// its minimum / default / maximum values (in user units, e.g. <c>wght</c> 100–900).
/// </summary>
public readonly struct FontAxis
{
    public uint Tag { get; }
    public float Minimum { get; }
    public float Default { get; }
    public float Maximum { get; }

    public FontAxis(uint tag, float minimum, float def, float maximum)
    {
        Tag = tag;
        Minimum = minimum;
        Default = def;
        Maximum = maximum;
    }

    /// <summary>The axis tag as its 4-character string form (e.g. "wght").</summary>
    public string TagString => new string(new[]
    {
        (char)((Tag >> 24) & 0xFF), (char)((Tag >> 16) & 0xFF),
        (char)((Tag >> 8) & 0xFF), (char)(Tag & 0xFF)
    });

    public override string ToString() => $"{TagString}[{Minimum:0.#}..{Default:0.#}..{Maximum:0.#}]";
}

public struct TextGlyph
{
    /// <summary>
    /// The rasterized glyph texture, or null for invisible glyphs (e.g. spaces) that only
    /// contribute advance width and are never drawn.
    /// </summary>
    public Texture? Texture;
    public Vector2 Position;
    public Vector2 Size;

    /// <summary>
    /// The UTF-16 index into the source text where this glyph's cluster begins. Lets a caret map a
    /// string index to a glyph position even when one glyph spans multiple UTF-16 units (e.g. an
    /// emoji surrogate pair) or vice versa (ligatures).
    /// </summary>
    public int StartIndex;
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
