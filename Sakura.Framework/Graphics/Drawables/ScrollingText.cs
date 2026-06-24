// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Colors;
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
    /// A trailing copy of <see cref="text"/> positioned one loop-extent behind it, so that as the
    /// first copy scrolls off the left edge the second copy fills the gap from the right, giving a
    /// seamless, endlessly-scrolling marquee with no blank state.
    /// </summary>
    private readonly SpriteText textCopy;

    private Color color = Color.White;

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

    /// <summary>
    /// The text to display.
    /// </summary>
    public string Text
    {
        get => text.Text;
        set
        {
            if (text.Text == value)
                return;

            text.Text = value;
            textCopy.Text = value;
            resetScroll();
        }
    }

    /// <summary>
    /// The <see cref="FontUsage"/> to use for the text.
    /// </summary>
    public FontUsage Font
    {
        get => text.Font;
        set
        {
            text.Font = value;
            textCopy.Font = value;
            resetScroll();
        }
    }

    /// <summary>
    /// The color of the text.
    /// </summary>
    public new Color Color
    {
        get => color;
        set
        {
            color = value;
            text.Color = value;
            textCopy.Color = value;
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
            Color = Color
        });

        Add(textCopy = new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Color = Color,
            Alpha = 0f
        });

        delayRemaining = StartDelay;
    }

    private void resetScroll()
    {
        text.X = 0;
        textCopy.X = 0;
        textCopy.Alpha = 0f;
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
            textCopy.Alpha = 0f;
            delayRemaining = StartDelay;
            return;
        }

        float loopExtent = textWidth + LoopSpacing;

        if (delayRemaining > 0)
        {
            delayRemaining -= Clock.ElapsedFrameTime / 1000.0;

            textCopy.X = text.X + loopExtent;
            textCopy.Alpha = 0f;
            return;
        }

        float dx = ScrollSpeed * (float)(Clock.ElapsedFrameTime / 1000.0);
        float x = text.X - dx;

        if (-x >= loopExtent)
            x += loopExtent;

        text.X = x;
        
        textCopy.X = x + loopExtent;
        textCopy.Alpha = 1f;
    }
}
