// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using FFmpeg.AutoGen;
using NUnit.Framework;
using Sakura.Framework.Graphics.Video;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests video color conversion across range and colorspace settings, including regressions that can
/// cause washed-out, crushed, or tinted output.
/// </summary>
[TestFixture]
public class VideoColorConversionTest
{
    private const float limited_black = 16f / 255f;
    private const float limited_white = 235f / 255f;
    private const float chroma_centre = 128f / 255f;

    /// <summary>
    /// Applies the affine transform the way the shader does: <c>u_YuvCoeff * vec4(y, cb, cr, 1)</c>,
    /// with the matrix held column-major.
    /// </summary>
    private static (float R, float G, float B) convert(float[] m, float y, float cb, float cr)
    {
        float component(int row) => m[row] * y + m[4 + row] * cb + m[8 + row] * cr + m[12 + row];

        return (component(0), component(1), component(2));
    }

    private static float[] matrixFor(AVColorSpace space, AVColorRange range) => VideoDecoder.ConversionMatrixFor(space, range, 1920, 1080);

    [Test]
    public void LimitedRangeMapsVideoBlackAndWhiteToFullSwing()
    {
        float[] m = matrixFor(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_MPEG);

        var black = convert(m, limited_black, chroma_centre, chroma_centre);
        var white = convert(m, limited_white, chroma_centre, chroma_centre);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(black.R, Is.EqualTo(0f).Within(0.002f));
            Assert.That(black.G, Is.EqualTo(0f).Within(0.002f));
            Assert.That(black.B, Is.EqualTo(0f).Within(0.002f));

            Assert.That(white.R, Is.EqualTo(1f).Within(0.002f));
            Assert.That(white.G, Is.EqualTo(1f).Within(0.002f));
            Assert.That(white.B, Is.EqualTo(1f).Within(0.002f));
        }
    }

    [Test]
    public void FullRangeMapsZeroAndOneToFullSwing()
    {
        float[] m = matrixFor(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_JPEG);

        var black = convert(m, 0f, chroma_centre, chroma_centre);
        var white = convert(m, 1f, chroma_centre, chroma_centre);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(black.R, Is.EqualTo(0f).Within(0.002f));
            Assert.That(white.R, Is.EqualTo(1f).Within(0.002f));
            Assert.That(white.G, Is.EqualTo(1f).Within(0.002f));
            Assert.That(white.B, Is.EqualTo(1f).Within(0.002f));
        }
    }

    /// <summary>
    /// The regression this whole piece of work exists to avoid: a full-range frame (what VideoToolbox
    /// hands back for <c>420YpCbCr8BiPlanarFullRange</c>) put through the limited-range matrix blows
    /// past white, and a limited-range frame through the full-range matrix never reaches it. Both look
    /// like a picture; neither is the right one.
    /// </summary>
    [Test]
    public void MixingUpTheRangeIsVisiblyWrong()
    {
        float[] limited = matrixFor(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_MPEG);
        float[] full = matrixFor(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_JPEG);

        float fullWhiteThroughLimited = convert(limited, 1f, chroma_centre, chroma_centre).R;
        float limitedWhiteThroughFull = convert(full, limited_white, chroma_centre, chroma_centre).R;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fullWhiteThroughLimited, Is.GreaterThan(1.05f), "full-range white should overshoot under the limited matrix");
            Assert.That(limitedWhiteThroughFull, Is.LessThan(0.95f), "limited-range white should fall short under the full matrix");
        }
    }

    /// <summary>
    /// Unspecified range has always been treated as limited and must keep being — that is the
    /// assumption every frame that reached this code before the range was completely read was converted
    /// under, so changing it would silently restyle existing videos.
    /// </summary>
    [Test]
    public void UnspecifiedRangeIsTreatedAsLimited()
    {
        float[] unspecified = matrixFor(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_UNSPECIFIED);
        float[] limited = matrixFor(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_MPEG);

        Assert.That(unspecified, Is.EqualTo(limited));
    }

    /// <summary>
    /// The size heuristic for an unspecified colorspace predates this work and is deliberately kept:
    /// SD-sized content is BT.601, anything larger BT.709. They differ in the chroma columns only.
    /// </summary>
    [Test]
    public void UnspecifiedColorspaceFallsBackOnSize()
    {
        float[] sd = VideoDecoder.ConversionMatrixFor(AVColorSpace.AVCOL_SPC_UNSPECIFIED, AVColorRange.AVCOL_RANGE_MPEG, 640, 480);
        float[] hd = VideoDecoder.ConversionMatrixFor(AVColorSpace.AVCOL_SPC_UNSPECIFIED, AVColorRange.AVCOL_RANGE_MPEG, 1920, 1080);

        float[] bt601 = matrixFor(AVColorSpace.AVCOL_SPC_SMPTE170M, AVColorRange.AVCOL_RANGE_MPEG);
        float[] bt709 = matrixFor(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_MPEG);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sd, Is.EqualTo(bt601));
            Assert.That(hd, Is.EqualTo(bt709));
        }
    }

    /// <summary>
    /// What the shader did before the offset moved into the matrix: subtract 16/256 and 128/256, then
    /// apply the bare limited-range 3x3. The new path divides by 255, which is what a UNORM8 sampler
    /// actually normalizes by, so the two differ by a bounded amount rather than matching — this pins
    /// that amount at just over one 8-bit code value, which is what makes the change a correction and
    /// not a re-grade.
    /// </summary>
    [Test]
    public void DiffersFromThePreviousShaderMathByBarelyOverOneCodeValue()
    {
        float[] m = matrixFor(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_MPEG);

        float[] legacy =
        {
            1.164f, 1.164f, 1.164f,
            0.000f, -0.213f, 2.112f,
            1.793f, -0.533f, 0.000f
        };

        // The worst case, derived rather than guessed: the chroma centre moved by
        // 128/255 - 128/256 = 0.00196 and the black level by 16/255 - 16/256 = 0.000245, so the largest
        // shift is on blue, whose Cb coefficient is the biggest in the matrix at 2.112 —
        // 2.112 * 0.00196 + 1.164 * 0.000245 = 0.00443, or 1.13 code values out of 255.
        const float max_delta = 1.2f / 255f;

        foreach ((float y, float cb, float cr) in new[]
                 {
                     (0.2f, 0.4f, 0.6f),
                     (0.5f, 0.5f, 0.5f),
                     (0.8f, 0.55f, 0.45f),
                     (limited_white, chroma_centre, chroma_centre),
                 })
        {
            float ly = y - 0.0625f, lcb = cb - 0.5f, lcr = cr - 0.5f;

            float legacyR = legacy[0] * ly + legacy[3] * lcb + legacy[6] * lcr;
            float legacyG = legacy[1] * ly + legacy[4] * lcb + legacy[7] * lcr;
            float legacyB = legacy[2] * ly + legacy[5] * lcb + legacy[8] * lcr;

            var now = convert(m, y, cb, cr);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(now.R, Is.EqualTo(legacyR).Within(max_delta));
                Assert.That(now.G, Is.EqualTo(legacyG).Within(max_delta));
                Assert.That(now.B, Is.EqualTo(legacyB).Within(max_delta));
            }
        }
    }

    /// <summary>
    /// Which direction that sub-code-value gap corrects in. Neutral chroma is the code value 128; with
    /// the old 128/256 offset it came out as a residual +0.00196 on Cb and Cr, so every grey pixel
    /// picked up a slight tint. Subtracting 128/255 — what the sampler actually produces for 128 —
    /// leaves grey exactly grey.
    /// </summary>
    [Test]
    public void NeutralChromaConvertsToExactlyNeutralRgb()
    {
        float[] m = matrixFor(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_MPEG);

        var grey = convert(m, 0.5f, chroma_centre, chroma_centre);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(grey.G, Is.EqualTo(grey.R).Within(1e-6f));
            Assert.That(grey.B, Is.EqualTo(grey.R).Within(1e-6f));
        }
    }
}
