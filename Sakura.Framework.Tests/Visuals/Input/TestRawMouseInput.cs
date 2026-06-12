// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Configurations;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Visuals.Input;

/// <summary>
/// Visual test for relative (raw input) mouse mode.
/// Shows the live raw-motion feed from the window — virtual cursor position, per-event
/// delta, hardware timestamp age and event rate — and provides steps to toggle relative
/// mode and adjust sensitivity both directly and through the framework config.
/// </summary>
public partial class TestRawMouseInput : TestScene
{
    [Resolved]
    private IWindow window { get; set; } = null!;

    [Resolved]
    private FrameworkConfigManager config { get; set; } = null!;

    private SpriteText modeLabel = null!;
    private SpriteText sensitivityLabel = null!;
    private SpriteText positionLabel = null!;
    private SpriteText deltaLabel = null!;
    private SpriteText timestampLabel = null!;
    private SpriteText rateLabel = null!;
    private Box marker = null!;

    // Written by the window's event feed (main thread), read in Update (update thread).
    // Tearing is harmless for display purposes.
    private Vector2 lastPosition;
    private Vector2 lastDelta;
    private double lastTimestamp = double.NaN;
    private int eventCounter;

    private double rateWindowStart;
    private int rateWindowStartCount;
    private double displayedRate;

    [SetUp]
    public void SetUp()
    {
        AddStep("Initialize", () =>
        {
            Clear();

            Add(new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(12, 12),
                Children = new Drawable[]
                {
                    modeLabel = makeLabel(0),
                    sensitivityLabel = makeLabel(1),
                    positionLabel = makeLabel(2),
                    deltaLabel = makeLabel(3),
                    timestampLabel = makeLabel(4),
                    rateLabel = makeLabel(5),
                }
            });

            Add(marker = new Box
            {
                Size = new Vector2(12, 12),
                Color = Color.Crimson,
            });

            // Re-subscribed on every SetUp; remove first so repeated runs don't stack handlers.
            if (window != null)
            {
                window.OnMouseMove -= onWindowMouseMove;
                window.OnMouseMove += onWindowMouseMove;
            }
        });
    }

    private static SpriteText makeLabel(int line) => new SpriteText
    {
        Text = string.Empty,
        Position = new Vector2(0, line * 24),
        Color = Color.White,
    };

    private void onWindowMouseMove(MouseEvent e)
    {
        lastPosition = e.MouseState.Position;
        lastDelta = e.Delta;
        lastTimestamp = e.Timestamp;
        eventCounter++;
    }

    [Test]
    public void TestRelativeModeToggle()
    {
        AddLabel("Direct window control");
        AddStep("Enable raw input", () => window.RelativeMouseMode = true);
        AddAssert("Relative mode active", () => window.RelativeMouseMode);
        AddStep("Disable raw input", () => window.RelativeMouseMode = false);
        AddAssert("Relative mode off", () => !window.RelativeMouseMode);

        AddLabel("Sensitivity (direct)");
        AddSliderStep("Sensitivity", 0.1, 5.0, 1.0, v =>
        {
            if (window != null)
                window.CursorSensitivity = v;
        });

        AddLabel("Via framework config");
        AddStep("Config: raw input on", () => config.Get(FrameworkSetting.RelativeMouseMode, false).Value = true);
        AddStep("Config: raw input off", () => config.Get(FrameworkSetting.RelativeMouseMode, false).Value = false);
        AddStep("Config: sensitivity 0.5", () => config.Get(FrameworkSetting.CursorSensitivity, 1.0).Value = 0.5);
        AddStep("Config: sensitivity 2.0", () => config.Get(FrameworkSetting.CursorSensitivity, 1.0).Value = 2.0);
        AddStep("Config: sensitivity 1.0", () => config.Get(FrameworkSetting.CursorSensitivity, 1.0).Value = 1.0);
    }

    public override void Update()
    {
        base.Update();

        if (modeLabel == null || window == null)
            return;

        // Input timestamps live on the shared TimeSource timeline. The scene's Clock is NOT
        // on that timeline (the test browser wraps it for rate control), so ages and rates
        // must be computed against TimeSource directly.
        double now = TimeSource.CurrentTime;
        if (now - rateWindowStart >= 1000)
        {
            displayedRate = (eventCounter - rateWindowStartCount) / ((now - rateWindowStart) / 1000.0);
            rateWindowStart = now;
            rateWindowStartCount = eventCounter;
        }

        modeLabel.Text = $"RelativeMouseMode: {window.RelativeMouseMode}";
        sensitivityLabel.Text = $"CursorSensitivity: {window.CursorSensitivity:F2}";
        positionLabel.Text = $"Position (window space): ({lastPosition.X:F1}, {lastPosition.Y:F1})";
        deltaLabel.Text = $"Delta: ({lastDelta.X:F2}, {lastDelta.Y:F2})";
        timestampLabel.Text = double.IsNaN(lastTimestamp)
            ? "Timestamp: (none yet)"
            : $"Timestamp: {lastTimestamp:F2} ms (age: {now - lastTimestamp:F2} ms)";
        rateLabel.Text = $"Motion events/s: {displayedRate:F0} (total: {eventCounter})";

        // Window events carry window-space coordinates, but this scene is offset (and possibly
        // scaled) inside the test browser chrome. Map through the scene's screen-space draw
        // rectangle to get local coordinates for the marker.
        var rect = DrawRectangle;

        if (rect.Width > 0 && rect.Height > 0)
        {
            float localX = (lastPosition.X - rect.X) / rect.Width * DrawSize.X;
            float localY = (lastPosition.Y - rect.Y) / rect.Height * DrawSize.Y;
            marker.Position = new Vector2(localX - 6, localY - 6);
        }
    }
}
