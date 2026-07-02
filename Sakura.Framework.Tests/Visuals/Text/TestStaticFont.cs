// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Text;

public partial class TestStaticFont : TestScene
{
    [Resolved]
    private IFontStore fontStore { get; set; } = null!;

    private const string test_family = "Comfortaa";

    private List<FontWeights> registerStaticFamily()
    {
        var storage = new EmbeddedResourceStorage(GetType().Assembly, $"{GetType().Assembly.GetName().Name}.Resources")
            .GetStorageForDirectory("Fonts");

        var registered = new List<FontWeights>();

        foreach (FontWeights weight in Enum.GetValues<FontWeights>())
        {
            string filename = $"{test_family}-{weight}.ttf";
            if (!storage.Exists(filename))
                continue;

            fontStore.AddFont(storage, filename, alias: $"{test_family}-{weight}");
            registered.Add(weight);
        }

        return registered;
    }

    [Test]
    public void TestRenderStaticFamily()
    {
        AddStep("Register + render the static family", () =>
        {
            Clear();

            var weights = registerStaticFamily();

            if (weights.Count == 0)
            {
                Add(new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Color = Color.White,
                    Font = new FontUsage(size: 24),
                    Text = $"No {test_family}-*.ttf found — drop static .ttf files into Tests/Resources/Fonts (see README).",
                });
                return;
            }

            var flow = new FlowContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 8),
                Padding = new MarginPadding(20),
            };

            foreach (FontWeights weight in weights)
            {
                flow.Add(new SpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Color = Color.White,
                    Font = new FontUsage(test_family, size: 32, weight: weight),
                    Text = $"{test_family} {weight} — The quick brown fox 0123456789 blabla สวัสดีจ้า",
                });
            }

            // A few sizes at Regular, to confirm sizing works on the static path.
            foreach (float size in new[] { 16f, 24f, 48f })
            {
                flow.Add(new SpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Color = Color.White,
                    Font = new FontUsage(test_family, size: size, weight: FontWeights.Regular),
                    Text = $"Size {size}px — Sphinx of black quartz, judge my vow.",
                });
            }

            Add(flow);
        });
    }

    [Test]
    public void TestStaticFontIsNotVariable()
    {
        AddAssert("Static family (if present) is detected as non-variable", () =>
        {
            var weights = registerStaticFamily();
            if (weights.Count == 0)
                return true;

            var font = fontStore.Get(new FontUsage(test_family, weight: FontWeights.Regular));

            // Headless store returns null, or a missing font resolves to the default (different name).
            if (font == null || font.Name != $"{test_family}-Regular")
                return true;

            // The whole point: a plain TTF must NOT be flagged variable, and must expose no axes.
            return !font.IsVariable && font.Axes.Count == 0;
        });

        AddAssert("Distinct weights resolve to distinct font instances", () =>
        {
            var weights = registerStaticFamily();

            // Need at least two real weight files to compare.
            if (weights.Count < 2)
                return true;

            var first = fontStore.Get(new FontUsage(test_family, weight: weights[0]));
            var second = fontStore.Get(new FontUsage(test_family, weight: weights[1]));

            if (first == null || second == null || first.Name != $"{test_family}-{weights[0]}")
                return true;

            // Static per-weight files are separate Font instances (contrast with a variable font,
            // where all weights share one instance).
            return !ReferenceEquals(first, second);
        });
    }

    [Test]
    public void TestVariableAndStaticTogether()
    {
        AddStep("Render variable + static side by side", () =>
        {
            Clear();

            var weights = registerStaticFamily();

            var flow = new FlowContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 8),
                Padding = new MarginPadding(20),
            };

            flow.Add(new SpriteText
            {
                Color = Color.White,
                Font = new FontUsage(size: 24, weight: FontWeights.Bold),
                Text = "— Variable (NotoSans[wght], one file drives every weight) —",
            });

            foreach (FontWeights weight in new[] { FontWeights.Thin, FontWeights.Regular, FontWeights.Bold, FontWeights.Black })
            {
                flow.Add(new SpriteText
                {
                    Color = Color.White,
                    Font = new FontUsage(size: 30, weight: weight),
                    Text = $"NotoSans {weight} — variable font",
                });
            }

            flow.Add(new SpriteText
            {
                Color = Color.White,
                Font = new FontUsage("NotoSans", size: 24, weight: FontWeights.Bold),
                Text = weights.Count > 0
                    ? "— Static (Comfortaa, one file per weight) —"
                    : "— Static family not found: add Comfortaa-*.ttf (see README) —",
            });

            foreach (FontWeights weight in weights)
            {
                flow.Add(new SpriteText
                {
                    Color = Color.White,
                    Font = new FontUsage(test_family, size: 30, weight: weight),
                    Text = $"{test_family} {weight} — static font",
                });
            }

            Add(flow);
        });
    }

    [Test]
    public void TestVariableAndStaticDetectedDistinctly()
    {
        AddAssert("Variable and static families are detected as their respective kinds", () =>
        {
            var weights = registerStaticFamily();

            var variable = fontStore.Get(new FontUsage("NotoSans", weight: FontWeights.Regular));
            var staticFont = weights.Count > 0
                ? fontStore.Get(new FontUsage(test_family, weight: FontWeights.Regular))
                : null;

            // Headless store returns null throughout — skip.
            if (variable == null)
                return true;

            // NotoSans must be variable.
            if (!variable.IsVariable)
                return false;

            // If the static family is present it must be detected as non-variable and be a different
            // instance from the variable font.
            if (staticFont != null && staticFont.Name == $"{test_family}-Regular")
                return !staticFont.IsVariable && !ReferenceEquals(variable, staticFont);

            return true;
        });
    }
}
