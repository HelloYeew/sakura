// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A horizontally auto-scrolling ("marquee") single-line text
/// </summary>
public partial class ScrollingText : Container
{
    private readonly SpriteText text;

    /// <summary>
    /// Scroll speed in logical pixels per second.
    /// </summary>
    public float ScrollSpeed { get; set; } = 60f;

    /// <summary>
    /// Gap (logical pixels) between the end of the text and the start of the looped copy.
    /// </summary>
    public float LoopSpacing { get; set; } = 40f;

    /// <summary>
    /// Seconds to pause at the start before scrolling begins (and after each full loop).
    /// </summary>
    public double StartDelay { get; set; } = 1.0;

    /// <summary>
    /// Current horizontal scroll offset of the inner text (0 when not scrolling).
    /// </summary>
    public float ScrollOffset => text.X;

    public string Text
    {
        get => text.Text;
        set
        {
            if (text.Text == value)
                return;

            text.Text = value;
            resetScroll();
        }
    }

    public FontUsage Font
    {
        get => text.Font;
        set
        {
            text.Font = value;
            resetScroll();
        }
    }

    private double delayRemaining;

    public ScrollingText()
    {
        Masking = true;
        AutoSizeAxes = Axes.Y;

        Add(text = new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
        });

        delayRemaining = StartDelay;
    }

    private void resetScroll()
    {
        text.X = 0;
        delayRemaining = StartDelay;
    }

    public override void Update()
    {
        base.Update();

        float visibleWidth = DrawSize.X;
        float textWidth = text.ContentSize.X;

        if (textWidth <= visibleWidth || visibleWidth <= 0)
        {
            if (text.X != 0)
                text.X = 0;
            delayRemaining = StartDelay;
            return;
        }

        if (delayRemaining > 0)
        {
            delayRemaining -= Clock.ElapsedFrameTime / 1000.0;
            return;
        }

        float dx = ScrollSpeed * (float)(Clock.ElapsedFrameTime / 1000.0);
        float x = text.X - dx;

        float loopExtent = textWidth + LoopSpacing;
        if (-x >= loopExtent)
        {
            x = 0;
            delayRemaining = StartDelay;
        }

        text.X = x;
    }
}
