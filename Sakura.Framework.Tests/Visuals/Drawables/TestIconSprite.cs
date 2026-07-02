// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.IconUsageExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

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

    private bool iconFontIsFallbackFor(FontUsage usage)
    {
        var materialFont = fontStore.Get("MaterialSymbolsOutlined");

        // headless font stores return null and have no fallbacks, this work only on real renderer
        if (materialFont == null)
            return true;

        return fontStore.GetFallbacks(usage).Contains(materialFont);
    }
}
