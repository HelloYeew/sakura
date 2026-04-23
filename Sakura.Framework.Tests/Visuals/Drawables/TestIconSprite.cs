// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

public class TestIconSprite : TestScene
{
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
}
