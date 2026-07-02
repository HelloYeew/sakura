// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using FreeTypeSharp;

namespace Sakura.Framework.Graphics.Text;

/// <summary>
/// P/Invoke shim for the small set of FreeType "Multiple Masters" (variable font) functions that
/// are <b>not</b> exposed by FreeTypeSharp 3.1.0 but exported by the bundled native
/// FreeType binary. The functions are declared against the bare module name
/// <c>"freetype"</c> which is the exact name FreeTypeSharp itself imports, so they bind to the same
/// already-loaded, in-process native library rather than loading a second copy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cross-platform integer widths.</b> <c>FT_Fixed</c> and <c>FT_ULong</c> are C
/// <c>long</c>/<c>unsigned long</c>, whose byte width (4 or 8) depends on the native binary's data
/// model and, as observed under virtualized Windows, does not always match the host ABI. The
/// <see cref="CLong"/>/<see cref="CULong"/> runtime types also fail to reproduce that width when used
/// as struct fields via <see cref="Marshal.PtrToStructure{T}(nint)"/>. So we neither marshal
/// <c>FT_Var_Axis</c> as a struct nor assume a width: <c>Font.readVariationAxes</c> detects the width
/// at runtime, walks the axis records by hand, and passes coordinates as a hand-built native buffer.
/// </para>
/// </remarks>
internal static class FreeTypeVariations
{
    /// <summary>
    /// FreeType face flag indicating a variable ("Multiple Masters") font.
    /// </summary>
    internal const long FT_FACE_FLAG_MULTIPLE_MASTERS = 1 << 8;

    /// <summary>
    /// Retrieves the variation axis and named-instance descriptors for a variable font. The returned
    /// <c>aMaster</c> pointer must be released with <see cref="FT_Done_MM_Var"/>.
    /// </summary>
    [DllImport("freetype")]
    internal static extern FT_Error FT_Get_MM_Var(nint face, out nint aMaster);

    /// <summary>
    /// Sets the design coordinates for the current variation instance. <paramref name="coords"/> points
    /// at an array of <c>numCoords</c> native <c>FT_Fixed</c> (C <c>long</c>, 16.16 fixed-point) values
    /// in the face's axis order. The caller owns the buffer and sizes each element at the width detected
    /// by <c>Font.readVariationAxes</c>.
    /// </summary>
    [DllImport("freetype")]
    internal static extern FT_Error FT_Set_Var_Design_Coordinates(nint face, uint numCoords, nint coords);

    /// <summary>
    /// Frees an <c>FT_MM_Var</c> previously returned by <see cref="FT_Get_MM_Var"/>.
    /// </summary>
    [DllImport("freetype")]
    internal static extern FT_Error FT_Done_MM_Var(nint library, nint aMaster);

    /// <summary>
    /// Selects a named instance (by 1-based index; 0 resets to the default) instead of raw
    /// coordinates. Not used on the hot path, but kept for completeness/future use.
    /// </summary>
    [DllImport("freetype")]
    internal static extern FT_Error FT_Set_Named_Instance(nint face, uint instanceIndex);

    /// <summary>
    /// Packs a 4-character OpenType axis tag the FreeType/HarfBuzz way
    /// (<c>'w'&lt;&lt;24 | 'g'&lt;&lt;16 | 'h'&lt;&lt;8 | 't'</c>).
    /// </summary>
    internal static uint Tag(string tag)
    {
        if (string.IsNullOrEmpty(tag) || tag.Length != 4)
            throw new ArgumentException("OpenType axis tags must be exactly 4 characters.", nameof(tag));

        return ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
    }
}

/// <summary>
/// Managed mirror of FreeType's <c>FT_MM_Var</c> header. It contains only <c>uint</c> and pointer
/// fields (no C <c>long</c>), so <see cref="Marshal.PtrToStructure{T}(nint)"/> lays it out correctly
/// on both LP64 and LLP64. The <c>FT_Var_Axis</c> records it points at are read by hand (see
/// <c>Font.readVariationAxes</c>) because their <c>FT_Fixed</c>/<c>FT_ULong</c> fields are C
/// <c>long</c> and cannot be marshalled portably as struct fields.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal struct FT_MM_Var
{
    public uint num_axis;
    public uint num_designs;
    public uint num_namedstyles;
    public nint axis;        // FT_Var_Axis*
    public nint namedstyle;  // FT_Var_Named_Style*
}
