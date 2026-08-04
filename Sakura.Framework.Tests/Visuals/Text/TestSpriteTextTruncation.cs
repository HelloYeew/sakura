// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Text;

/// <summary>
/// Truncation as seen through <see cref="SpriteText"/>
/// </summary>
public partial class TestSpriteTextTruncation : TestScene
{
    private const string long_text = "The quick brown fox jumps over the lazy dog";

    private const float budget = 220;

    private SpriteText truncating = null!;
    private Box guide = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add truncating sprite", () =>
        {
            Clear();

            Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    // Marks the budget so overflow is obvious by eye.
                    guide = new Box
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(budget, 40),
                        Color = Color.DarkSlateBlue
                    },
                    truncating = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = long_text,
                        Font = FontUsage.Default.With(size: 24),
                        Truncate = true,
                        MaxWidth = budget
                    }
                }
            });
        });

        AddSliderStep("Max width", 20f, 600f, budget, value =>
        {
            if (truncating.IsNull())
                return;

            truncating.MaxWidth = value;
            guide.Width = value;
        });
    }

    [Test]
    public void TestLongTextIsTruncatedToMaxWidth()
    {
        AddAssert("stays within the budget", () =>
        {
            if (truncating.ContentSize.X == 0)
                return true;

            return truncating.ContentSize.X <= budget;
        });

        AddAssert("reports truncation and shows an ellipsis", () =>
        {
            if (truncating.ContentSize.X == 0)
                return true;

            return truncating.IsTruncated
                   && truncating.DisplayedText.EndsWith(TextTruncation.DEFAULT_ELLIPSIS)
                   && truncating.Text == long_text;
        });
    }

    [Test]
    public void TestShortTextIsLeftAlone()
    {
        AddStep("Set short text", () => truncating.Text = "Short");

        AddAssert("not truncated", () => !truncating.IsTruncated && truncating.DisplayedText == "Short");
    }

    [Test]
    public void TestDisablingTruncationRestoresTheFullText()
    {
        AddStep("Disable truncation", () => truncating.Truncate = false);

        AddAssert("full text is displayed", () => !truncating.IsTruncated && truncating.DisplayedText == long_text);

        AddStep("Re-enable truncation", () => truncating.Truncate = true);

        AddAssert("back within the budget", () => truncating.ContentSize.X == 0 || truncating.ContentSize.X <= budget);
    }

    [Test]
    public void TestWideningTheBudgetKeepsMoreText()
    {
        string narrow = null!;

        AddStep("Narrow budget", () =>
        {
            truncating.MaxWidth = 120;
            guide.Width = 120;
            narrow = truncating.DisplayedText;
        });

        AddStep("Wider budget", () =>
        {
            truncating.MaxWidth = 320;
            guide.Width = 320;
        });

        AddAssert("more text survives", () =>
        {
            if (truncating.ContentSize.X == 0)
                return true;

            return truncating.DisplayedText.Length > narrow.Length && truncating.ContentSize.X <= 320;
        });
    }

    [Test]
    public void TestCustomEllipsis()
    {
        AddStep("Use three dots", () => truncating.Ellipsis = "...");

        AddAssert("three dots are used", () =>
        {
            if (truncating.ContentSize.X == 0)
                return true;

            return truncating.DisplayedText.EndsWith("...") && truncating.ContentSize.X <= budget;
        });
    }

    [Test]
    public void TestUnlimitedMaxWidthDoesNothing()
    {
        AddStep("Clear the budget", () => truncating.MaxWidth = float.PositiveInfinity);

        AddAssert("full text is displayed", () => !truncating.IsTruncated && truncating.DisplayedText == long_text);
    }
}
