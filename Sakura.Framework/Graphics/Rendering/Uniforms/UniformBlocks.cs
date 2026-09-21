// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.InteropServices;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Rendering.Uniforms;

/// <summary>
/// std140 layout of the framework's built-in uniform blocks.
/// </summary>
/// <remarks>
/// These structs are uploaded verbatim into GL uniform buffer objects (and, later, Metal argument
/// buffers), so their field order, sizes and padding MUST match the <c>std140</c> block declared in
/// the corresponding GLSL 450 shader exactly. std140 rules in play here:
/// <list type="bullet">
/// <item><description><c>float</c>/<c>int</c> align to 4 bytes.</description></item>
/// <item><description><c>vec2</c> aligns to 8 bytes.</description></item>
/// <item><description><c>vec4</c> and <c>mat4</c> align to 16 bytes; a <c>mat4</c> is 64 bytes.</description></item>
/// <item><description>GLSL <c>bool</c> in a std140 block occupies 4 bytes — represented here as <c>int</c> (0/1).</description></item>
/// <item><description>The whole block is rounded up to a multiple of 16 bytes.</description></item>
/// </list>
/// <see cref="StructLayout"/> with explicit <see cref="FieldOffsetAttribute"/> is used so the layout
/// is pinned and self-documenting rather than left to the C# compiler.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct ProjectionBlock
{
    /// <summary>
    /// The orthographic projection matrix. Maps to <c>mat4 u_Projection</c>.
    /// </summary>
    [FieldOffset(0)] public Matrix4x4 Projection;
}

/// <summary>
/// Border + edge-effect state for the main shader. Matches <c>MaskBlock</c> in shader.frag.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 80)]
public struct MaskBlock
{
    /// <summary>
    /// Border color (premultiplied as elsewhere). Maps to <c>vec4 u_BorderColor</c>.
    /// Also reused as the edge-effect color during the edge-effect pass.
    /// </summary>
    [FieldOffset(0)]
    public Vector4 BorderColor;

    /// <summary>
    /// True element centre in screen space. Maps to <c>vec2 u_MaskCenter</c>.
    /// </summary>
    [FieldOffset(16)]
    public Vector2 MaskCenter;

    /// <summary>
    /// Un-sheared half size. Maps to <c>vec2 u_MaskHalfSize</c>.
    /// </summary>
    [FieldOffset(24)]
    public Vector2 MaskHalfSize;

    /// <summary>
    /// Horizontal shear. Maps to <c>float u_ShearX</c>.
    /// </summary>
    [FieldOffset(32)]
    public float ShearX;

    /// <summary>
    /// Corner radius. Maps to <c>float u_CornerRadius</c>.
    /// </summary>
    [FieldOffset(36)]
    public float CornerRadius;

    /// <summary>
    /// Border thickness. Maps to <c>float u_BorderThickness</c>.
    /// </summary>
    [FieldOffset(40)]
    public float BorderThickness;

    /// <summary>
    /// Border pass enabled (0/1). Maps to <c>int u_IsBorder</c> (GLSL bool).
    /// </summary>
    [FieldOffset(44)]
    public int IsBorder;

    /// <summary>
    /// Edge-effect pass enabled (0/1). Maps to <c>int u_IsEdgeEffect</c> (GLSL bool).
    /// </summary>
    [FieldOffset(48)]
    public int IsEdgeEffect;

    /// <summary>
    /// Edge-effect soft falloff radius in screen pixels. Maps to <c>float u_EdgeRadius</c>.
    /// </summary>
    [FieldOffset(52)]
    public float EdgeRadius;

    /// <summary>
    /// Edge-effect offset in screen space (added to the shape centre). Maps to <c>vec2 u_EdgeOffset</c>.
    /// Aligned to 8 bytes per std140; offset 56 satisfies that and keeps it inside one 16-byte block
    /// (48..63) rather than straddling a boundary.
    /// </summary>
    [FieldOffset(56)]
    public Vector2 EdgeOffset;

    /// <summary>
    /// Hollow edge effect (cut out the interior) (0/1). Maps to <c>int u_EdgeHollow</c> (GLSL bool).
    /// </summary>
    [FieldOffset(64)]
    public int EdgeHollow;

    /// <summary>
    /// Glow (1) vs shadow (0) edge effect. Affects falloff shaping. Maps to <c>int u_EdgeGlow</c> (GLSL bool).
    /// </summary>
    [FieldOffset(68)]
    public int EdgeGlow;
}

/// <summary>
/// Grayscale-pass strength for <c>BufferedContainer</c>. Matches <c>GrayscaleBlock</c> in grayscale.frag.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GrayscaleBlock
{
    /// <summary>
    /// 0 = original colors, 1 = fully grayscale. Maps to <c>float u_Strength</c>.
    /// </summary>
    [FieldOffset(0)]
    public float Strength;
}

/// <summary>
/// One separable Gaussian blur direction for <c>BufferedContainer</c>. Matches <c>BlurBlock</c> in blur.frag.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct BlurBlock
{
    /// <summary>
    /// 1.0 / texture size in pixels. Maps to <c>vec2 u_TexelSize</c>.
    /// </summary>
    [FieldOffset(0)]
    public Vector2 TexelSize;

    /// <summary>
    /// (1,0) horizontal pass, (0,1) vertical pass. Maps to <c>vec2 u_Direction</c>.
    /// </summary>
    [FieldOffset(8)]
    public Vector2 Direction;

    /// <summary>
    /// Gaussian sigma in texels. Maps to <c>float u_Sigma</c>.
    /// </summary>
    [FieldOffset(16)]
    public float Sigma;

    /// <summary>
    /// Sampling radius (taps each side, max 64). Maps to <c>int u_Radius</c>.
    /// </summary>
    [FieldOffset(20)]
    public int Radius;
}

/// <summary>
/// YUV→RGB conversion coefficients for the video shader. Matches <c>VideoBlock</c> in video.frag.
/// </summary>
/// <remarks>
/// The shader declares the coefficients as a <c>mat4</c> because a std140 <c>mat3</c> pads each column
/// to 16 bytes anyway; a <c>mat4</c> is the same 64 bytes with a far simpler, less error-prone layout.
/// The fourth column, once dead padding, now carries the black/chroma offset as a translation, so the
/// shader applies offset and conversion in one multiply and the colour range stops being a constant
/// baked into the shader. Use <see cref="FromAffine"/>.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct VideoBlock
{
    /// <summary>
    /// Affine YUV→RGB transform. Maps to <c>mat4 u_YuvCoeff</c>, applied as
    /// <c>u_YuvCoeff * vec4(y, cb, cr, 1.0)</c>.
    /// </summary>
    [FieldOffset(0)]
    public Matrix4x4 YuvCoeff;

    /// <summary>
    /// Builds a <see cref="VideoBlock"/> from a column-major 4×4 supplied as a float[16] — the layout
    /// GLSL reads a <c>mat4</c> in, and what
    /// <see cref="Sakura.Framework.Graphics.Video.VideoDecoder.GetConversionMatrix"/> produces.
    /// </summary>
    public static VideoBlock FromAffine(float[] m)
    {
        // Matrix4x4 is row-major in memory (M11 M12 M13 M14, then M21...), so the first four floats of
        // the struct are what GLSL reads as column 0. Writing m's column c into row c lands each
        // column where the shader expects it.
        var sm = new Matrix4x4(
            m[0], m[1], m[2], m[3],
            m[4], m[5], m[6], m[7],
            m[8], m[9], m[10], m[11],
            m[12], m[13], m[14], m[15]);

        return new VideoBlock
        {
            YuvCoeff = sm
        };
    }
}
