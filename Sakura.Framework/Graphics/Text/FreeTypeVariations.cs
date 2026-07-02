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
/// <c>long</c>/<c>unsigned long</c>: 8 bytes on macOS/Linux (LP64) but 4 bytes on 64-bit Windows
/// (LLP64). We marshal them with the runtime types <see cref="CLong"/>/<see cref="CULong"/>, which
/// map to the platform-correct C <c>long</c> width automatically, and let
/// <see cref="Marshal.SizeOf{T}()"/>/<see cref="Marshal.PtrToStructure{T}(nint)"/> compute the axis
/// record stride. This keeps the struct layout correct on both data models without <c>#if</c>
/// branches.
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
    /// Sets the design coordinates for the current variation instance. <paramref name="coords"/> are
    /// 16.16 fixed-point values in the face's axis order.
    /// </summary>
    [DllImport("freetype")]
    internal static extern FT_Error FT_Set_Var_Design_Coordinates(nint face, uint numCoords, [In] CLong[] coords);

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
/// Managed mirror of FreeType's <c>FT_MM_Var</c> header. <see cref="LayoutKind.Sequential"/> with the
/// <see cref="CLong"/>/<see cref="CULong"/> fields in <see cref="FT_Var_Axis"/> keeps it correct on
/// both LP64 and LLP64.
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

/// <summary>
/// Managed mirror of FreeType's <c>FT_Var_Axis</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal struct FT_Var_Axis
{
    public nint name;        // FT_String*
    public CLong minimum;    // FT_Fixed (16.16)
    public CLong def;        // FT_Fixed (16.16)
    public CLong maximum;    // FT_Fixed (16.16)
    public CULong tag;       // FT_ULong — packed 4-char axis tag
    public uint strid;
}
