// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A phone's cutouts the framework can fake on the window (Ctrl+F6), so a layout can be checked against a
/// notch without a phone. The insets scale with the window, keeping a phone's proportions.
/// </summary>
public enum SafeAreaPreview
{
    Off,

    /// <summary>
    /// An iPhone with a Dynamic Island: the island at the top in portrait, both sides inset in landscape,
    /// and the home indicator at the bottom.
    /// </summary>
    Iphone,

    /// <summary>
    /// An Android phone with a punch-hole camera on one short edge and the gesture bar at the bottom.
    /// </summary>
    AndroidPunchHole
}

/// <summary>
/// Applies a <see cref="SafeAreaPreview"/> to the app's safe area and tints the unsafe area, so what would
/// land under the cutout shows.
/// </summary>
internal partial class SafeAreaPreviewOverlay : Container, IRemoveFromDrawVisualiser
{
    private static readonly Color tint = Color.FromArgb(90, 255, 0, 128);

    private readonly SafeAreaDefiningContainer screen;
    private readonly Reactive<SafeAreaPreview> preview;

    private readonly Box top;
    private readonly Box bottom;
    private readonly Box left;
    private readonly Box right;

    public override bool HandlePositionalInput => false;
    public override bool HandleNonPositionalInput => false;

    public SafeAreaPreviewOverlay(SafeAreaDefiningContainer screen, Reactive<SafeAreaPreview> preview)
    {
        this.screen = screen;
        this.preview = preview;

        RelativeSizeAxes = Axes.Both;

        Children = new Drawable[]
        {
            top = new Box { RelativeSizeAxes = Axes.X, Color = tint },
            bottom = new Box { Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, RelativeSizeAxes = Axes.X, Color = tint },
            left = new Box { RelativeSizeAxes = Axes.Y, Color = tint },
            right = new Box { Anchor = Anchor.TopRight, Origin = Anchor.TopRight, RelativeSizeAxes = Axes.Y, Color = tint },
        };
    }

    public override void Update()
    {
        MarginPadding? insets = InsetsFor(preview.Value, DrawSize);

        // Updated before the screen it overrides (later siblings update first), so the screen lays out
        // with the new insets this frame.
        screen.OverrideSafeArea = insets;

        var bands = insets ?? new MarginPadding();
        top.Height = bands.Top;
        bottom.Height = bands.Bottom;
        left.Width = bands.Left;
        right.Width = bands.Right;
        left.Margin = right.Margin = new MarginPadding { Top = bands.Top, Bottom = bands.Bottom };

        base.Update();
    }

    /// <summary>
    /// The insets <paramref name="preview"/> fakes on a window of <paramref name="size"/>, or null when it's off.
    /// </summary>
    internal static MarginPadding? InsetsFor(SafeAreaPreview preview, Vector2 size)
    {
        bool portrait = size.Y > size.X;
        float shortSide = portrait ? size.X : size.Y;

        switch (preview)
        {
            case SafeAreaPreview.Iphone:
            {
                // An iPhone 15 Pro, in points.
                float s = shortSide / 393f;
                return portrait
                    ? new MarginPadding { Top = 59 * s, Bottom = 34 * s }
                    : new MarginPadding { Left = 59 * s, Right = 59 * s, Bottom = 21 * s };
            }

            case SafeAreaPreview.AndroidPunchHole:
            {
                // A Pixel-sized phone, in dp: the cutout is as deep as the status bar it sits in.
                float s = shortSide / 412f;
                return portrait
                    ? new MarginPadding { Top = 32 * s, Bottom = 24 * s }
                    : new MarginPadding { Left = 32 * s, Bottom = 24 * s };
            }

            default:
                return null;
        }
    }
}
