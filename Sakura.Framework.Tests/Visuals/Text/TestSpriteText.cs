// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Text;

public partial class TestSpriteText : TestScene
{
    [Resolved]
    private IFontStore fontStore { get; set; } = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add SpriteText", () => Add(new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            RelativePositionAxes = Axes.Both,

            Child = new FlowContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Size = new Vector2(1),
                Spacing = new Vector2(5, 10),
                Padding = new MarginPadding(20),

                Children = new Drawable[]
                {
                    // 1. English - Testing Bold Italics
                    new SpriteText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Text = "Hello World! This is Bold Italic.",
                        Font = new FontUsage("NotoSans", size: 24, weight: "Bold", italics: true)
                    },

                    // Thai
                    new SpriteText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Text = "สวัสดีชาวโลก (Hello World - Thai Regular)",
                        Font = new FontUsage("NotoSans", size: 24, weight: "Regular", italics: false)
                    },

                    // Japanese
                    new SpriteText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Text = "こんにちは世界 (Hello World - Japanese Black)",
                        Font = new FontUsage("NotoSans", size: 24, weight: "Black", italics: false)
                    },

                    // Arabic (just test RTL)
                    new SpriteText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Text = "مرحبا بالعالم (Hello World - Arabic Light)",
                        Font = new FontUsage("NotoSans", size: 24, weight: "Light", italics: false)
                    },

                    // Chinese (Simplified)
                    new SpriteText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Text = "你好，世界 (Hello World - Chinese SC Medium)",
                        Font = new FontUsage("NotoSans", size: 24, weight: "Medium", italics: false)
                    },

                    // Korean
                    new SpriteText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Text = "안녕하세요 세상 (Hello World - Korean Thin)",
                        Font = new FontUsage("NotoSans", size: 24, weight: "Thin", italics: false)
                    },

                    // Emojis mix with text
                    new SpriteText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Text = "👍 🦍 🐒 🌸",
                        Font = new FontUsage("NotoSans", size: 24, weight: "Regular", italics: false)
                    },

                    // Mix everything
                    new SpriteText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Text = "English, สวัสดี, こんにちは, مرحبا, 😡!",
                        Font = new FontUsage("NotoSans", size: 24, weight: "Bold", italics: false)
                    }
                }
            }
        }));
    }

    [Test]
    public void TestSpriteTextRendering()
    {

    }

    /// <summary>
    /// Renders emoji using NotoColorEmoji as the primary font directly (by family name), rather than
    /// relying on it being picked from the emoji fallback chain. This exercises the bundled
    /// cross-platform color emoji font even on platforms where a different color emoji font (e.g.
    /// macOS "Apple Color Emoji") would otherwise win the fallback.
    /// Requires <c>NotoColorEmoji.ttf</c> to be present in the framework's Resources/Fonts.
    /// </summary>
    [Test]
    public void TestNotoColorEmojiDirect()
    {
        AddStep("Add emoji text using NotoColorEmoji directly", () => Add(new SpriteText
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Text = "👍 🦍 🐒 🌸 😡 🎉 ❤️",
            Font = new FontUsage("NotoColorEmoji", size: 48, weight: "Regular", italics: false)
        }));

        AddAssert("NotoColorEmoji resolves to its own font (bundled)", () =>
        {
            var noto = fontStore.Get("NotoColorEmoji");

            // The headless font store returns null and has no fonts; only assert on a real renderer.
            // If NotoColorEmoji is missing the store falls back to the default font, whose name will
            // not match, failing this assert to signal the resource is not bundled.
            return noto == null || noto.Name == "NotoColorEmoji";
        });
    }
}
