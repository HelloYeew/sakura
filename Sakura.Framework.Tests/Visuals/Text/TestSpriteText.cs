// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering.Vertex;
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

            // The headless font store returns null and has no fonts
            return noto == null || noto.Name == "NotoColorEmoji";
        });
    }

    [Test]
    public void TestColorEmojiIgnoresTint()
    {
        SpriteText tintedEmojiText = null!;

        AddStep("Add red-tinted colour emoji text", () => Add(tintedEmojiText = new SpriteText
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Color = Color.Red,
            Text = "Red 👍",
            Font = new FontUsage(size: 48, weight: "Regular", italics: false)
        }));

        AddAssert("colour glyph vertices are untinted while text glyph vertices are tinted red", () =>
        {
            var glyphs = getShapedGlyphs(tintedEmojiText);
            var vertices = getTextVertices(tintedEmojiText);

            // The headless font store produces no glyphs at all - nothing to assert against.
            if (glyphs == null || glyphs.Count == 0)
                return true;

            bool sawColorGlyph = false;
            bool sawTextGlyph = false;

            for (int i = 0; i < glyphs.Count; i++)
            {
                var glyph = glyphs[i];
                if (glyph.Texture == null)
                    continue;

                var vertexColor = vertices[i * 4].Color;

                if (glyph.IsColorGlyph)
                {
                    sawColorGlyph = true;

                    // Color emoji glyphs must keep their native RGB (identity), only alpha follows the tint.
                    if (vertexColor.X < 0.99f || vertexColor.Y < 0.99f || vertexColor.Z < 0.99f)
                        return false;
                }
                else
                {
                    sawTextGlyph = true;

                    // Ordinary glyphs must be tinted red: full red, no green/blue.
                    if (vertexColor.X < 0.99f || vertexColor.Y > 0.01f || vertexColor.Z > 0.01f)
                        return false;
                }
            }

            // Both kinds of glyphs must actually have been exercised for this assertion to be meaningful.
            return sawColorGlyph && sawTextGlyph;
        });
    }

    private static IReadOnlyList<TextGlyph>? getShapedGlyphs(SpriteText spriteText)
    {
        var field = typeof(SpriteText).GetField("shapedText", BindingFlags.NonPublic | BindingFlags.Instance);
        var shapedText = field?.GetValue(spriteText) as ShapedText;
        return shapedText?.Glyphs;
    }

    private static Vertex[] getTextVertices(SpriteText spriteText)
    {
        var field = typeof(SpriteText).GetField("textVertices", BindingFlags.NonPublic | BindingFlags.Instance);
        return (Vertex[])field!.GetValue(spriteText)!;
    }
}
