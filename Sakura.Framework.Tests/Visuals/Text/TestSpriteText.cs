// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Text;

public class TestSpriteText : TestScene
{
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
}
