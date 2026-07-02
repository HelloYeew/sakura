// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.IconUsageExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Text;

public partial class TestVariableFont : TestScene
{
    [Resolved]
    private IFontStore fontStore { get; set; } = null!;

    [Test]
    public void TestAllWeights()
    {
        AddStep("Render NotoSans at all 9 weights", () =>
        {
            var flow = new FlowContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 6),
                Padding = new MarginPadding(20),
            };

            foreach (FontWeights weight in Enum.GetValues<FontWeights>())
            {
                flow.Add(new SpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Color = Color.White,
                    Font = new FontUsage("NotoSans", size: 32, weight: weight),
                    Text = $"{weight} — The quick brown fox jumps 0123456789",
                });
            }

            Clear();
            Add(flow);
        });
    }

    [Test]
    public void TestItalicWeights()
    {
        AddStep("Render NotoSans italic at all 9 weights", () =>
        {
            var flow = new FlowContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 6),
                Padding = new MarginPadding(20),
            };

            foreach (FontWeights weight in Enum.GetValues<FontWeights>())
            {
                flow.Add(new SpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Color = Color.White,
                    Font = new FontUsage("NotoSans", size: 32, weight: weight, italics: true),
                    Text = $"{weight} Italic — The quick brown fox jumps 0123456789",
                });
            }

            Clear();
            Add(flow);
        });
    }

    [Test]
    public void TestUprightVsItalic()
    {
        // Upright and italic of the same weight, stacked so the slant is easy to eyeball.
        AddStep("Render upright above italic (Bold)", () =>
        {
            var flow = new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Padding = new MarginPadding(20),
            };

            flow.Add(new SpriteText
            {
                Color = Color.White,
                Font = new FontUsage("NotoSans", size: 40, weight: FontWeights.Bold, italics: false),
                Text = "The quick brown fox (Bold, upright)",
            });
            flow.Add(new SpriteText
            {
                Color = Color.White,
                Font = new FontUsage("NotoSans", size: 40, weight: FontWeights.Bold, italics: true),
                Text = "The quick brown fox (Bold, italic)",
            });

            Clear();
            Add(flow);
        });
    }

    [Test]
    public void TestItalicResolvesToVariableFont()
    {
        AddAssert("Italic resolves to a (variable) font", () =>
        {
            var italic = fontStore.Get(new FontUsage("NotoSans", weight: FontWeights.Bold, italics: true));

            // Headless store returns null; only assert on a real renderer.
            if (italic == null)
                return true;

            return italic.IsVariable;
        });

        AddAssert("Italic and upright are distinct instances", () =>
        {
            var upright = fontStore.Get(new FontUsage("NotoSans", weight: FontWeights.Regular, italics: false));
            var italic = fontStore.Get(new FontUsage("NotoSans", weight: FontWeights.Regular, italics: true));

            if (upright == null || italic == null)
                return true;

            // The italic comes from a separate variable file (Noto ships no ital axis), so the two
            // must be different Font instances.
            return !ReferenceEquals(upright, italic);
        });

        AddAssert("Every italic weight shares one instance", () =>
        {
            var bold = fontStore.Get(new FontUsage("NotoSans", weight: FontWeights.Bold, italics: true));
            var thin = fontStore.Get(new FontUsage("NotoSans", weight: FontWeights.Thin, italics: true));

            if (bold == null || thin == null)
                return true;

            // All italic weights are driven from the single italic variable file.
            return ReferenceEquals(bold, thin);
        });
    }

    [Test]
    public void TestIconFillAndWeight()
    {
        AddStep("Render icons: outlined vs filled, across weights", () =>
        {
            var flow = new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(16, 16),
                Padding = new MarginPadding(20),
            };

            foreach (bool filled in new[] { false, true })
            {
                foreach (FontWeights weight in new[] { FontWeights.Thin, FontWeights.Regular, FontWeights.Bold, FontWeights.Black })
                {
                    flow.Add(new IconSprite
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Color = Color.White,
                        Icon = IconUsage.Favorite,
                        IconSize = 64,
                        IconWeight = weight,
                        Filled = filled,
                    });
                }
            }

            Clear();
            Add(flow);
        });
    }

    [Test]
    public void TestMaterialSymbolsIsVariable()
    {
        // The bundled Material Symbols font is a variable font (wght / FILL / GRAD / opsz).
        AddAssert("Material Symbols detected as variable", () =>
        {
            var font = fontStore.Get("MaterialSymbolsOutlined");

            // Headless store returns null and has no real fonts; only assert on a real renderer.
            if (font == null)
                return true;

            return font.IsVariable;
        });

        AddAssert("Exposes wght and FILL axes", () =>
        {
            var font = fontStore.Get("MaterialSymbolsOutlined");
            if (font == null)
                return true;

            bool hasWeight = font.Axes.Any(a => a.Tag == FontVariation.WEIGHT_AXIS);
            bool hasFill = font.Axes.Any(a => a.Tag == FontVariation.FILL_AXIS);
            return hasWeight && hasFill;
        });

        AddAssert("Outlined and filled both shape to glyphs", () =>
        {
            var font = fontStore.Get("MaterialSymbolsOutlined");
            if (font == null)
                return true;

            string glyph = IconUsage.Favorite.ToGlyph();

            var outlined = font.ProcessText(glyph, 48f, 1f, null, FontVariation.None.With(FontVariation.FILL_AXIS, 0f));
            var filled = font.ProcessText(glyph, 48f, 1f, null, FontVariation.None.With(FontVariation.FILL_AXIS, 1f));

            return outlined.Glyphs.Count > 0 && filled.Glyphs.Count > 0;
        });
    }
}
