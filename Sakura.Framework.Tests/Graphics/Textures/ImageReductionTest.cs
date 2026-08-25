// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Graphics.Textures;

/// <summary>
/// Tests for <see cref="ImageReduction"/>
/// </summary>
[TestFixture]
public class ImageReductionTest
{
    [Test]
    public void ExactHalfIsSnappedToAHalf()
    {
        var fraction = ImageReduction.DecodeFraction(3840, 2160, new Vector2(1920, 1080), false);

        Assert.That(fraction, Is.EqualTo((1920, 1080)));
    }

    /// <remarks>
    /// The measured reason the snap exists: 3/8, 5/8, 6/8 and 7/8 are not native IDCT scales, and
    /// hinting one costs more than not hinting at all (109/114/122 ms against 55 ms for a full decode on
    /// a 3840x2160 source). A reduction of less than 2x therefore has no fraction available and must
    /// come back as "decode at full resolution".
    /// </remarks>
    [Test]
    public void ReductionSmallerThanHalfGetsNoFraction()
    {
        // 0.625 of the source: a 5/8 that must not be offered.
        Assert.That(ImageReduction.DecodeFraction(800, 800, new Vector2(500, 500), false), Is.Null);

        // yuuki's own 2347x1507 background into a 1920x1080 window — 0.72x, likewise no fraction.
        Assert.That(ImageReduction.DecodeFraction(2347, 1507, new Vector2(1920, 1080), false), Is.Null);
    }

    [Test]
    public void QuarterScaleSnapsToAQuarterNotTheDisplaySize()
    {
        // a 1000x894 cover bound for 200x200 wants 0.2x, and 1/4 is the smallest free scale that still
        // covers it. Decoding at 200x200 directly would cost an internal resample the decoder cannot
        // skip.
        var fraction = ImageReduction.DecodeFraction(1000, 894, new Vector2(200, 200), false);

        Assert.That(fraction, Is.EqualTo((250, 224)));
    }

    [Test]
    public void SourceAtOrBelowTargetGetsNoFraction()
    {
        Assert.That(ImageReduction.DecodeFraction(800, 600, new Vector2(1920, 1080), false), Is.Null);
        Assert.That(ImageReduction.DecodeFraction(1920, 1080, new Vector2(1920, 1080), false), Is.Null);
    }

    [Test]
    public void DegenerateSourceGetsNoFraction()
    {
        Assert.That(ImageReduction.DecodeFraction(0, 0, new Vector2(100, 100), false), Is.Null);
    }

    /// <remarks>
    /// A Fill keeps one axis whole and cuts the other, so only the cut axis has to reach the target and
    /// the decode has to stay large enough to serve it — 1920 down to 768 is under 2x, so 1/2 is as far
    /// as it goes. The same box as a Fit shrinks <em>both</em> axes, 1080 all the way to 128, so it can
    /// take 1/8. The Fill therefore decodes more pixels on purpose: they are the ones that survive the
    /// crop.
    /// </remarks>
    [Test]
    public void FillAndFitSizeTheDecodeDifferently()
    {
        var fill = ImageReduction.DecodeFraction(1920, 1080, new Vector2(768, 128), true);
        var fit = ImageReduction.DecodeFraction(1920, 1080, new Vector2(768, 128), false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fill, Is.EqualTo((960, 540)));
            Assert.That(fit, Is.EqualTo((240, 135)));
        }
    }

    [Test]
    public void FillSizeCropsToTargetAspect()
    {
        // 16:9 source into a 6:1 bar: the full width is kept, the height cut to the bar's aspect.
        Assert.That(ImageReduction.FillSize(3840, 2160, 768, 128), Is.EqualTo((768, 128)));
        Assert.That(ImageReduction.FillSize(1920, 1080, 768, 128), Is.EqualTo((768, 128)));
    }

    [Test]
    public void FillSizeNeverUpscales()
    {
        // the source is smaller than the box, and ResizeMode.Crop would happily enlarge it.
        Assert.That(ImageReduction.FillSize(120, 120, 256, 256), Is.EqualTo((120, 120)));
    }

    [Test]
    public void TargetPixelsRoundsOutAndClampsToOne()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ImageReduction.TargetPixels(new Vector2(100.2f, 50.8f)), Is.EqualTo((101, 51)));
            Assert.That(ImageReduction.TargetPixels(new Vector2(0f, -5f)), Is.EqualTo((1, 1)));
        }
    }

    /// <remarks>
    /// The origin exists for stb_image_resize2, which crops by taking a sub-rectangle rather than by
    /// being handed a pre-cropped buffer. A region that ran past the source would be an out-of-bounds
    /// read, so it is pinned rather than assumed.
    /// </remarks>
    [Test]
    public void FillRegionIsCentredAndInsideTheSource()
    {
        // 16:9 into a 6:1 bar: full width, a centred horizontal band.
        var wide = ImageReduction.FillRegion(3840, 2160, 768, 128);

        using (Assert.EnterMultipleScope())
        {
            Assert.That((wide.SourceX, wide.SourceY), Is.EqualTo((0, 760)));
            Assert.That((wide.SourceWidth, wide.SourceHeight), Is.EqualTo((3840, 640)));
            Assert.That((wide.Width, wide.Height), Is.EqualTo((768, 128)));
        }

        // 3:2 into a square: full height, a centred vertical band.
        var tall = ImageReduction.FillRegion(1200, 800, 300, 300);

        using (Assert.EnterMultipleScope())
        {
            Assert.That((tall.SourceX, tall.SourceY), Is.EqualTo((200, 0)));
            Assert.That((tall.SourceWidth, tall.SourceHeight), Is.EqualTo((800, 800)));
            Assert.That((tall.Width, tall.Height), Is.EqualTo((300, 300)));
        }
    }

    [Test]
    public void FillRegionNeverLeavesTheSource()
    {
        // aspect ratios chosen to make the rounding in FillRegion land awkwardly
        foreach ((int sw, int sh) in new[] { (1001, 667), (999, 333), (427, 480), (3, 7), (1, 1) })
        {
            foreach ((int tw, int th) in new[] { (200, 200), (768, 128), (16, 9), (1, 3) })
            {
                var region = ImageReduction.FillRegion(sw, sh, tw, th);

                Assert.That(region.SourceX, Is.GreaterThanOrEqualTo(0));
                Assert.That(region.SourceY, Is.GreaterThanOrEqualTo(0));
                Assert.That(region.SourceX + region.SourceWidth, Is.LessThanOrEqualTo(sw), $"{sw}x{sh} -> {tw}x{th} overran width");
                Assert.That(region.SourceY + region.SourceHeight, Is.LessThanOrEqualTo(sh), $"{sw}x{sh} -> {tw}x{th} overran height");
            }
        }
    }

    [Test]
    public void FitSizePreservesAspectAndNeverUpscales()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ImageReduction.FitSize(2000, 1000, 256, 256), Is.EqualTo((256, 128)));
            Assert.That(ImageReduction.FitSize(4000, 2000, 512, 512), Is.EqualTo((512, 256)));
            // already inside the box, so returned untouched rather than stretched to fill it
            Assert.That(ImageReduction.FitSize(100, 80, 512, 512), Is.EqualTo((100, 80)));
        }
    }
}
