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
/// Masking + border state for the main shader. Matches <c>MaskBlock</c> in shader.frag.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 80)]
public struct MaskBlock
{
    /// <summary>
    /// Border colour (premultiplied as elsewhere). Maps to <c>vec4 u_BorderColor</c>.
    /// Also reused as the edge-effect colour during the edge-effect pass.
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
    /// Masking enabled (0/1). Maps to <c>int u_IsMasking</c> (GLSL bool).
    /// </summary>
    [FieldOffset(44)]
    public int IsMasking;

    /// <summary>
    /// Border pass enabled (0/1). Maps to <c>int u_IsBorder</c> (GLSL bool).
    /// </summary>
    [FieldOffset(48)]
    public int IsBorder;

    /// <summary>
    /// Edge-effect pass enabled (0/1). Maps to <c>int u_IsEdgeEffect</c> (GLSL bool).
    /// </summary>
    [FieldOffset(52)]
    public int IsEdgeEffect;

    /// <summary>
    /// Edge-effect soft falloff radius in screen pixels. Maps to <c>float u_EdgeRadius</c>.
    /// </summary>
    [FieldOffset(56)]
    public float EdgeRadius;

    /// <summary>
    /// Edge-effect offset in screen space (added to the shape centre). Maps to <c>vec2 u_EdgeOffset</c>.
    /// Aligned to 8 bytes per std140; placed at offset 64 so it does not straddle a 16-byte boundary.
    /// </summary>
    [FieldOffset(64)]
    public Vector2 EdgeOffset;

    /// <summary>
    /// Hollow edge effect (cut out the interior) (0/1). Maps to <c>int u_EdgeHollow</c> (GLSL bool).
    /// </summary>
    [FieldOffset(72)]
    public int EdgeHollow;

    /// <summary>
    /// Glow (1) vs shadow (0) edge effect. Affects falloff shaping. Maps to <c>int u_EdgeGlow</c> (GLSL bool).
    /// </summary>
    [FieldOffset(76)]
    public int EdgeGlow;
}

/// <summary>
/// Grayscale-pass strength for <c>BufferedContainer</c>. Matches <c>GrayscaleBlock</c> in grayscale.frag.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GrayscaleBlock
{
    /// <summary>
    /// 0 = original colours, 1 = fully grayscale. Maps to <c>float u_Strength</c>.
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
/// The shader declares the coefficients as a <c>mat4</c> (using only the upper-left 3×3) because a
/// std140 <c>mat3</c> pads each column to 16 bytes anyway; a <c>mat4</c> is the same 64 bytes with a
/// far simpler, less error-prone layout. Use <see cref="FromMat3"/> to build it from a row-major
/// float[9].
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct VideoBlock
{
    /// <summary>
    /// YUV→RGB matrix; upper-left 3×3 is used. Maps to <c>mat4 u_YuvCoeff</c>.
    /// </summary>
    [FieldOffset(0)]
    public Matrix4x4 YuvCoeff;

    /// <summary>
    /// Builds a <see cref="VideoBlock"/> from a 3×3 matrix supplied as a row-major float[9],
    /// placing it in the upper-left of a 4×4 (identity elsewhere). The GLSL side reads it as
    /// <c>mat3(u_YuvCoeff)</c>, which takes the upper-left 3 columns × 3 rows.
    /// </summary>
    public static VideoBlock FromMat3(float[] m)
    {
        var sm = Matrix4x4.Identity;

        // The GLSL source treated u_YuvCoeff as a mat3 multiplied as `coeff * yuv` with the array
        // uploaded column-major (UniformMatrix3 convention). Matrix4x4 is row-major,
        // and mat3(mat4) in GLSL reads columns; place the 9 values so the resulting mat3 columns
        // match the original mat3 columns 1:1.
        sm.M11 = m[0]; sm.M12 = m[1]; sm.M13 = m[2];
        sm.M21 = m[3]; sm.M22 = m[4]; sm.M23 = m[5];
        sm.M31 = m[6]; sm.M32 = m[7]; sm.M33 = m[8];

        return new VideoBlock
        {
            YuvCoeff = sm
        };
    }
}
