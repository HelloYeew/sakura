// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.IconUsageExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.Drawables;

public partial class TestIconSprite : TestScene
{
    [Resolved]
    private IFontStore fontStore { get; set; } = null!;

    private IconSprite icon;

    [SetUp]
    public void SetUp()
    {
        icon = new IconSprite
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(100),
            Color = Color.White,
            Icon = IconUsage.Alarm,
            IconSize = 80
        };
        AddStep("Add icon", () => Add(icon));
    }

    [Test]
    public void TestIconsTransform()
    {
        AddStep("Rotate icon", () => icon.RotateTo(360, 200));
        AddWaitStep("Wait for rotation", 200);
        AddStep("Scale icon", () => icon.ScaleTo(1.5f, 200));
        AddWaitStep("Wait for scaling", 200);
        AddStep("Fade icon", () => icon.FadeTo(0.5f, 200));
        AddWaitStep("Wait for fading", 200);
        AddAssert("Icon is faded", () => icon.Alpha < 1);
        AddStep("Reset transforms", () => icon.Alpha = 1);
        AddStep("Flash icon", () => icon.FlashColour(Color.Red, 200));
    }

    [Test]
    public void TestIconChange()
    {
        AddStep("Change icon", () => icon.Icon = IconUsage.Doorbell);
        AddAssert("Icon is changed", () => icon.Icon == IconUsage.Doorbell);
    }

    [Test]
    public void TestIconStyleChange()
    {
        AddStep("Change icon to better see the style change", () => icon.Icon = IconUsage.Box);
        AddStep("Change icon style to outlined", () => icon.Style = MaterialIconStyle.Outlined);
        AddAssert("Icon style is changed", () => icon.Style == MaterialIconStyle.Outlined);
        AddStep("Change icon style to rounded", () => icon.Style = MaterialIconStyle.Rounded);
        AddAssert("Icon style is changed", () => icon.Style == MaterialIconStyle.Rounded);
        AddStep("Change icon style to sharp", () => icon.Style = MaterialIconStyle.Sharp);
        AddAssert("Icon style is changed", () => icon.Style == MaterialIconStyle.Sharp);
    }

    [Test]
    public void TestIconFilledDrivesFillAxis()
    {
        AddAssert("Defaults to outlined", () => !icon.Filled);
        AddAssert("No FILL override by default", () => icon.Font.ToVariation().Get(FontVariation.FILL_AXIS) is null);

        AddStep("Fill the icon", () => icon.Filled = true);
        AddAssert("Filled getter is true", () => icon.Filled);
        AddAssert("FILL axis is 1", () => axisIs(icon.Font.ToVariation().Get(FontVariation.FILL_AXIS), 1f));

        AddStep("Outline the icon", () => icon.Filled = false);
        AddAssert("Filled getter is false", () => !icon.Filled);
        AddAssert("FILL axis is 0", () => axisIs(icon.Font.ToVariation().Get(FontVariation.FILL_AXIS), 0f));
    }

    [Test]
    public void TestIconWeightDrivesWghtAxis()
    {
        AddAssert("Defaults to Regular", () => icon.IconWeight == FontWeights.Regular);
        AddAssert("wght axis defaults to 400", () => axisIs(icon.Font.ToVariation().Get(FontVariation.WEIGHT_AXIS), 400f));

        AddStep("Set weight to Bold", () => icon.IconWeight = FontWeights.Bold);
        AddAssert("IconWeight getter is Bold", () => icon.IconWeight == FontWeights.Bold);
        AddAssert("wght axis is 700", () => axisIs(icon.Font.ToVariation().Get(FontVariation.WEIGHT_AXIS), 700f));

        AddStep("Set weight to Thin", () => icon.IconWeight = FontWeights.Thin);
        AddAssert("IconWeight getter is Thin", () => icon.IconWeight == FontWeights.Thin);
        AddAssert("wght axis is 100", () => axisIs(icon.Font.ToVariation().Get(FontVariation.WEIGHT_AXIS), 100f));
    }

    [Test]
    public void TestIconGradeDrivesGradAxis()
    {
        AddAssert("No GRAD override by default", () => icon.Grade is null && icon.Font.ToVariation().Get(FontVariation.GRADE_AXIS) is null);

        AddStep("Set a positive grade", () => icon.Grade = 200f);
        AddAssert("Grade getter reflects value", () => axisIs(icon.Grade, 200f));
        AddAssert("GRAD axis is 200", () => axisIs(icon.Font.ToVariation().Get(FontVariation.GRADE_AXIS), 200f));

        AddStep("Set a negative grade", () => icon.Grade = -25f);
        AddAssert("GRAD axis is -25", () => axisIs(icon.Font.ToVariation().Get(FontVariation.GRADE_AXIS), -25f));

        AddStep("Clear the grade", () => icon.Grade = null);
        AddAssert("Grade getter is null", () => icon.Grade is null);
        AddAssert("GRAD override is removed", () => icon.Font.ToVariation().Get(FontVariation.GRADE_AXIS) is null);
    }

    [Test]
    public void TestIconOpticalSizeTracksIconSizeByDefault()
    {
        AddAssert("opsz tracks the initial IconSize", () => axisIs(icon.Font.ToVariation().Get(FontVariation.OPTICAL_SIZE_AXIS), icon.IconSize));

        AddStep("Change IconSize to 40", () => icon.IconSize = 40f);
        AddAssert("opsz follows IconSize to 40", () => axisIs(icon.OpticalSize, 40f) && axisIs(icon.Font.ToVariation().Get(FontVariation.OPTICAL_SIZE_AXIS), 40f));
    }

    [Test]
    public void TestExplicitOpticalSizeDisablesAutoTracking()
    {
        AddStep("Pin optical size to 100", () => icon.OpticalSize = 100f);
        AddAssert("opsz axis is 100", () => axisIs(icon.OpticalSize, 100f) && axisIs(icon.Font.ToVariation().Get(FontVariation.OPTICAL_SIZE_AXIS), 100f));

        AddStep("Change IconSize to 20", () => icon.IconSize = 20f);
        AddAssert("opsz stays pinned at 100", () => axisIs(icon.OpticalSize, 100f) && axisIs(icon.Font.ToVariation().Get(FontVariation.OPTICAL_SIZE_AXIS), 100f));

        AddStep("Clear the explicit optical size", () => icon.OpticalSize = null);
        AddAssert("opsz reverts to tracking IconSize (20)", () => axisIs(icon.OpticalSize, 20f) && axisIs(icon.Font.ToVariation().Get(FontVariation.OPTICAL_SIZE_AXIS), 20f));

        AddStep("Change IconSize to 60", () => icon.IconSize = 60f);
        AddAssert("Auto tracking restored, opsz follows to 60", () => axisIs(icon.OpticalSize, 60f) && axisIs(icon.Font.ToVariation().Get(FontVariation.OPTICAL_SIZE_AXIS), 60f));
    }

    [Test]
    public void TestIconAxisVariants()
    {
        AddStep("Show a row of axis variants", () =>
        {
            var flow = new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Direction = FlowDirection.Horizontal,
                Size = new Vector2(600, 120),
                Spacing = new Vector2(20, 0),
                Children = new Drawable[]
                {
                    new IconSprite { Icon = IconUsage.Box, IconSize = 60, Color = Color.White },
                    new IconSprite { Icon = IconUsage.Box, IconSize = 60, Color = Color.White, Filled = true },
                    new IconSprite { Icon = IconUsage.Box, IconSize = 60, Color = Color.White, IconWeight = FontWeights.Bold },
                    new IconSprite { Icon = IconUsage.Box, IconSize = 60, Color = Color.White, IconWeight = FontWeights.Thin },
                    new IconSprite { Icon = IconUsage.Box, IconSize = 60, Color = Color.White, Filled = true, Grade = 200f }
                }
            };
            Add(flow);
        });
    }

    [Test]
    public void TestIconInSpriteText()
    {
        AddStep("Add a SpriteText with an icon", () =>
        {
            var text = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Color = Color.White,
                Text = "This is alarm " + char.ConvertFromUtf32((int)IconUsage.Alarm)
            };
            Add(text);
        });
    }

    [Test]
    public void TestIconInBoldSpriteText()
    {
        AddStep("Add a Bold SpriteText with an icon", () =>
        {
            var text = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Color = Color.White,
                Font = FontUsage.Default.With(size: 40, weight: nameof(FontWeights.Bold)),
                Text = $"Bold alarm {IconUsage.Alarm.ToGlyph()}"
            };
            Add(text);
        });
    }

    [Test]
    public void TestIconFallbackSurvivesWeightOverride()
    {
        AddAssert("Material font is a fallback for Regular", () => iconFontIsFallbackFor(FontUsage.Default));
        AddAssert("Material font is a fallback for Bold", () => iconFontIsFallbackFor(FontUsage.Default.With(weight: nameof(FontWeights.Bold))));
        AddAssert("Material font is a fallback for Bold Italic", () => iconFontIsFallbackFor(FontUsage.Default.With(weight: nameof(FontWeights.Bold), italics: true)));
    }
    
    private static bool axisIs(float? actual, float expected) => actual is float v && Precision.AlmostEquals(v, expected);

    private bool iconFontIsFallbackFor(FontUsage usage)
    {
        var materialFont = fontStore.Get("MaterialSymbolsOutlined");

        // headless font stores return null and have no fallbacks, this work only on real renderer
        if (materialFont == null)
            return true;

        return fontStore.GetFallbacks(usage).Contains(materialFont);
    }
}
