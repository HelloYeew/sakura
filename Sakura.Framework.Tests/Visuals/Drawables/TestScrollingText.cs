// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

public partial class TestScrollingText : TestScene
{
    private ScrollingText scrolling = null!;
    private Box background = null!;

    private const string long_text = "This is a very long line of text that does not fit and should scroll horizontally like a marquee.";
    private const string short_text = "Short text (fits).";

    [SetUp]
    public void SetUp()
    {
        AddStep("Create scrolling text", () =>
        {
            Clear();

            Add(background = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 300,
                Height = 30,
                Color = Color.FromArgb(255, 40, 40, 40)
            });

            Add(scrolling = new ScrollingText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 300,
                Font = FontUsage.Default.With(size: 18),
                Text = long_text
            });
        });
    }

    [Test]
    public void TestScrolls()
    {
        AddWaitStep("Watch it scroll", 6000);
    }

    [Test]
    public void TestScrollsContinuously()
    {
        if (!IsVisualRunner)
            Assert.Ignore("Requires glyph measurement (visual runner); headless has no font metrics.");

        AddStep("Use fast scroll", () =>
        {
            scrolling.StartDelay = 0;
            scrolling.ScrollSpeed = 240f;
        });

        float firstOffset = 0f;
        AddWaitStep("Let it scroll a while", 1500);
        AddStep("Sample offset", () => firstOffset = scrolling.ScrollOffset);

        AddAssert("Has started scrolling (offset negative)", () => scrolling.ScrollOffset < 0);

        AddWaitStep("Scroll past at least one loop", 4000);
        AddAssert("Offset stayed bounded (wrapped, not stalled)", () =>
            scrolling.ScrollOffset <= 0 && scrolling.ScrollOffset > -10000);
        AddAssert("Still actively scrolling", () => scrolling.ScrollOffset != firstOffset);
    }

    [Test]
    public void TestShortTextDoesNotScroll()
    {
        AddStep("Set short text", () => scrolling.Text = short_text);
        AddWaitStep("Should stay still", 2000);
        AddAssert("Text pinned to left (offset == 0)", () => scrolling.ScrollOffset == 0);
    }

    [Test]
    public void TestAdjustable()
    {
        AddSliderStep("Width", 80f, 600f, 300f, w =>
        {
            if (scrolling == null) return;
            scrolling.Width = w;
            background.Width = w;
        });

        AddSliderStep("Scroll speed", 10f, 240f, 60f, s =>
        {
            if (scrolling != null) scrolling.ScrollSpeed = s;
        });

        AddSliderStep("Loop spacing", 0f, 200f, 40f, g =>
        {
            if (scrolling != null) scrolling.LoopSpacing = g;
        });

        AddSliderStep("Start delay (s)", 0f, 3f, 1f, d =>
        {
            if (scrolling != null) scrolling.StartDelay = d;
        });

        AddStep("Use long text", () => scrolling.Text = long_text);
        AddStep("Use short text", () => scrolling.Text = short_text);
    }

    [Test]
    public void TestFontSize()
    {
        AddSliderStep("Font size", 10f, 40f, 18f, size =>
        {
            if (scrolling != null) scrolling.Font = FontUsage.Default.With(size: size);
        });

        AddWaitStep("Watch", 4000);
    }

    [Test]
    public void TestChangeColor()
    {
        AddStep("Change color", () => scrolling.Color = Color.Red);
        AddWaitStep("Watch", 2000);
        AddStep("Change color", () => scrolling.Color = Color.Green);
        AddWaitStep("Watch", 2000);
        AddStep("Change color", () => scrolling.Color = Color.Blue);
        AddWaitStep("Watch", 2000);
    }
}
