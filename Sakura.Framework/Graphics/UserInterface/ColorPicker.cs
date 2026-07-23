// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// Abstract base for a color picker
/// </summary>
public abstract partial class ColorPicker : Container
{
    /// <summary>
    /// The currently selected color. Assigning to it (directly, via binding, or via UI input)
    /// updates the whole picker, the alpha channel is preserved through HSV round-trips.
    /// </summary>
    public Reactive<Color> Current { get; } = new Reactive<Color>(Color.White);

    /// <summary>
    /// Pixel size of the saturation/value square
    /// </summary>
    public Vector2 SaturationValueAreaSize { get; init; } = new Vector2(300, 220);

    /// <summary>
    /// Pixel height of the hue slider bar.
    /// </summary>
    public float HueBarHeight { get; init; } = 24;

    /// <summary>
    /// Vertical spacing between the square, the hue bar, and the hex/preview row.
    /// </summary>
    public float Spacing { get; init; } = 10;

    /// <summary>
    /// The saturation/value square region. Exposed so tests and consumers can target it.
    /// </summary>
    public Drawable SaturationValueArea => svArea;

    /// <summary>
    /// The hue slider region. Exposed so tests and consumers can target it.
    /// </summary>
    public Drawable HueSlider => hueBar;

    /// <summary>
    /// The hex text input, or null if the picker was built without one.
    /// </summary>
    public TextBox? HexInput => hexInput;

    // Authoritative HSV state, each in [0, 1].
    private float hue;
    private float saturation;
    private float brightness = 1f;

    // Guards the Current change handler from re-deriving HSV while we are the ones writing Current.
    private bool applyingHsv;

    private readonly SaturationValueSelector svArea;
    private readonly HueSelector hueBar;
    private readonly Box svHueLayer;
    private readonly Drawable svMarker;
    private readonly Drawable hueMarker;

    private readonly TextBox? hexInput;
    private readonly Drawable? preview;

    protected ColorPicker()
    {
        AutoSizeAxes = Axes.Both;

        float width = SaturationValueAreaSize.X;

        svMarker = CreateSaturationValueMarker();
        svMarker.Anchor = Anchor.TopLeft;
        svMarker.Origin = Anchor.Centre;
        svMarker.RelativePositionAxes = Axes.Both;

        hueMarker = CreateHueMarker();
        hueMarker.Anchor = Anchor.CentreLeft;
        hueMarker.Origin = Anchor.Centre;
        hueMarker.RelativePositionAxes = Axes.X;

        svArea = new SaturationValueSelector
        {
            Size = SaturationValueAreaSize,
            Masking = true,
            PositionChanged = onSaturationValueChanged,
            Children = new Drawable[]
            {
                svHueLayer = new Box { RelativeSizeAxes = Axes.Both },
                // White (full saturation reveal) on the left fading to transparent on the right
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    ColorInfo = ColorInfo.GradientHorizontal(Color.White, Color.White.WithAlpha(0))
                },
                // Transparent at the top fading to black at the bottom (value falls off downward).
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    ColorInfo = ColorInfo.GradientVertical(Color.Black.WithAlpha(0), Color.Black)
                },
                new NonPositionalContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = svMarker
                }
            }
        };

        hueBar = new HueSelector
        {
            Size = new Vector2(width, HueBarHeight),
            Masking = true,
            HueChanged = onHueChanged,
        };

        hueBar.Add(new NonPositionalContainer
        {
            RelativeSizeAxes = Axes.Both,
            Child = hueMarker
        });

        var layout = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            AutoSizeAxes = Axes.Both,
            Spacing = new Vector2(0, Spacing),
        };

        layout.Add(svArea);
        layout.Add(hueBar);

        hexInput = CreateHexInput();
        preview = CreatePreview();

        if (hexInput != null || preview != null)
        {
            var bottomRow = new FlowContainer
            {
                Direction = FlowDirection.Horizontal,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(Spacing, 0),
            };

            if (hexInput != null)
            {
                if (hexInput.Size.X <= 0)
                    hexInput.Size = new Vector2(width - (preview?.Size.X ?? 0) - Spacing, HueBarHeight + 8);

                // "#AARRGGBB" is the longest accepted form.
                hexInput.LengthLimit = 9;
                hexInput.OnCommit += onHexCommitted;
                hexInput.Text.ValueChanged += onHexTextChanged;
                bottomRow.Add(hexInput);
            }

            if (preview != null)
                bottomRow.Add(preview);

            layout.Add(bottomRow);
        }

        var background = CreateBackground();
        if (background != null)
        {
            background.RelativeSizeAxes = Axes.Both;
            AddInternal(background);
        }

        AddInternal(layout);

        Current.ValueChanged += onCurrentChanged;
    }

    public override void LoadComplete()
    {
        base.LoadComplete();

        svArea.Size = SaturationValueAreaSize;
        hueBar.Size = new Vector2(SaturationValueAreaSize.X, HueBarHeight);
        if (hexInput != null)
            hexInput.Size = new Vector2(SaturationValueAreaSize.X - (preview?.Size.X ?? 0) - Spacing, HueBarHeight + 8);

        // Derive the initial HSV from Current and paint everything once.
        deriveHsvFromCurrent(Current.Value);
        updateVisuals();
    }

    /// <summary>
    /// Create the draggable marker shown over the saturation/value square.
    /// </summary>
    protected abstract Drawable CreateSaturationValueMarker();

    /// <summary>
    /// Create the draggable marker shown over the hue slider.
    /// </summary>
    protected abstract Drawable CreateHueMarker();

    /// <summary>
    /// Create the background drawn behind the whole picker. Return null for none.
    /// </summary>
    protected virtual Drawable? CreateBackground() => null;

    /// <summary>
    /// Create the hex text input. Return null to omit the hex row. Its color is not managed by the
    /// base; only its <see cref="TextBox.Text"/> is kept in sync with <see cref="Current"/>.
    /// </summary>
    protected virtual TextBox? CreateHexInput() => null;

    /// <summary>
    /// Create the preview swatch kept in sync with <see cref="Current"/>. Return null to omit it.
    /// The base updates it via <see cref="UpdatePreview"/>, which defaults to setting
    /// <see cref="Drawable.Color"/> — override that if the preview renders its color indirectly
    /// (e.g. through a child).
    /// </summary>
    protected virtual Drawable? CreatePreview() => null;

    /// <summary>
    /// Applies <paramref name="color"/> to the drawable returned by <see cref="CreatePreview"/>.
    /// Defaults to setting <see cref="Drawable.Color"/>.
    /// </summary>
    protected virtual void UpdatePreview(Drawable previewDrawable, Color color) => previewDrawable.Color = color;

    private void onSaturationValueChanged(Vector2 normalised)
    {
        releaseHexFocus();
        saturation = Math.Clamp(normalised.X, 0f, 1f);
        brightness = Math.Clamp(1f - normalised.Y, 0f, 1f);
        applyHsvToCurrent();
    }

    private void onHueChanged(float normalisedHue)
    {
        releaseHexFocus();
        hue = Math.Clamp(normalisedHue, 0f, 1f);
        applyHsvToCurrent();
    }

    /// <summary>
    /// Drops focus off the hex input when the user starts interacting with the square/hue bar, so the
    /// live-updating hex text is no longer suppressed (<see cref="updateVisuals"/> skips the field
    /// while it has focus) and the field reflects the dragged color immediately.
    /// </summary>
    private void releaseHexFocus()
    {
        if (hexInput != null && hexInput.HasFocus)
            GetContainingFocusManager()?.ChangeFocus(null);
    }

    private void onHexTextChanged(ValueChangedEvent<string> e)
    {
        // Only react to the user's own typing; programmatic syncs (from updateVisuals) happen while
        // the field is unfocused, and must not feed back into Current.
        if (hexInput == null || !hexInput.HasFocus)
            return;

        // Update live while typing, but silently ignore partial/invalid input until it parses.
        if (tryParseHex(e.NewValue, out var parsed))
            Current.Value = parsed;
    }

    private void onHexCommitted(string text)
    {
        if (tryParseHex(text, out var parsed))
            Current.Value = parsed;
        else if (hexInput != null)
            // Invalid input on commit: revert the field to the current color.
            hexInput.Text.Value = Current.Value.ToHex();
    }

    private static bool tryParseHex(string text, out Color color)
    {
        try
        {
            color = ColorExtensions.FromHex(text.Trim());
            return true;
        }
        catch (Exception)
        {
            color = Color.Empty;
            return false;
        }
    }

    private void applyHsvToCurrent()
    {
        applyingHsv = true;
        Current.Value = ColorExtensions.FromHSV(hue, saturation, brightness, Current.Value.A);
        applyingHsv = false;

        updateVisuals();
    }

    private void onCurrentChanged(ValueChangedEvent<Color> e)
    {
        // A UI drag already set the HSV state; only re-derive when the change came from elsewhere.
        if (!applyingHsv)
            deriveHsvFromCurrent(e.NewValue);

        updateVisuals();
    }

    private void deriveHsvFromCurrent(Color color)
    {
        ColorExtensions.ToHSV(color, out float h, out float s, out float v);

        // Hue is undefined for greyscale/black; keep the previous hue so the UI does not jump.
        if (s > 0f && v > 0f)
            hue = h;

        saturation = s;
        brightness = v;
    }

    private void updateVisuals()
    {
        svMarker.Position = new Vector2(saturation, 1f - brightness);
        hueMarker.X = hue;

        // The square's base layer is the fully-saturated, full-value form of the current hue.
        svHueLayer.Color = ColorExtensions.FromHSV(hue, 1f, 1f);

        if (preview != null)
            UpdatePreview(preview, Current.Value);

        // Don't stomp on the field while the user is typing in it.
        if (hexInput != null && !hexInput.HasFocus)
            hexInput.Text.Value = Current.Value.ToHex();
    }

    /// <summary>
    /// The saturation/value square. Reports the drag position as a normalised (x, y) in [0, 1],
    /// where x is saturation and y grows downward (so value = 1 - y).
    /// </summary>
    private partial class SaturationValueSelector : Container
    {
        public Action<Vector2>? PositionChanged;

        public override bool OnMouseDown(MouseButtonEvent e)
        {
            if (e.Button != MouseButton.Left)
                return false;

            report(e.ScreenSpaceMousePosition);

            // Let the base set IsDragged / invoke OnDragStart so the input manager captures us as the
            // drag target then claim the event so we become that target.
            base.OnMouseDown(e);
            return true;
        }

        public override bool OnDragStart(MouseButtonEvent e) => e.Button == MouseButton.Left;

        public override bool OnDrag(MouseEvent e)
        {
            report(e.ScreenSpaceMousePosition);
            return true;
        }

        private void report(Vector2 screenSpacePos)
        {
            if (DrawSize.X <= 0 || DrawSize.Y <= 0)
                return;

            var local = ToLocalSpace(screenSpacePos);
            PositionChanged?.Invoke(new Vector2(
                Math.Clamp(local.X / DrawSize.X, 0f, 1f),
                Math.Clamp(local.Y / DrawSize.Y, 0f, 1f)));
        }
    }

    /// <summary>
    /// The hue slider. Built from segments so a full 0..1 rainbow is expressed with the framework's
    /// per-quad two-color gradients. Reports the drag position as a normalised hue in [0, 1].
    /// </summary>
    private partial class HueSelector : Container
    {
        public Action<float>? HueChanged;

        private const int segments = 6;

        public HueSelector()
        {
            for (int i = 0; i < segments; i++)
            {
                float start = (float)i / segments;
                float end = (float)(i + 1) / segments;

                AddInternal(new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.X,
                    Width = 1f / segments,
                    X = start,
                    ColorInfo = ColorInfo.GradientHorizontal(
                        ColorExtensions.FromHSV(start, 1f, 1f),
                        ColorExtensions.FromHSV(end, 1f, 1f))
                });
            }
        }

        public override bool OnMouseDown(MouseButtonEvent e)
        {
            if (e.Button != MouseButton.Left)
                return false;

            report(e.ScreenSpaceMousePosition);

            // Let the base set IsDragged / invoke OnDragStart so the input manager captures us as the
            // drag target then claim the event so we become that target.
            base.OnMouseDown(e);
            return true;
        }

        public override bool OnDragStart(MouseButtonEvent e) => e.Button == MouseButton.Left;

        public override bool OnDrag(MouseEvent e)
        {
            report(e.ScreenSpaceMousePosition);
            return true;
        }

        private void report(Vector2 screenSpacePos)
        {
            if (DrawSize.X <= 0)
                return;

            float localX = ToLocalSpace(screenSpacePos).X;
            HueChanged?.Invoke(Math.Clamp(localX / DrawSize.X, 0f, 1f));
        }
    }

    /// <summary>
    /// A pass-through container that never receives positional input, so the markers it holds never
    /// steal mouse-down/drag from the interactive area beneath them.
    /// </summary>
    private partial class NonPositionalContainer : Container
    {
        public override bool HandlePositionalInput => false;
    }
}
