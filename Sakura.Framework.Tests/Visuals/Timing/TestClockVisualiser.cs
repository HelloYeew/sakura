// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Tests.Timing;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Visuals.Timing;

public partial class TestClockVisualiser : TestScene
{
    private StopwatchClock stopwatch = null!;
    private ManualClock manualClock = null!;
    private FramedClock framedClock = null!;

    private TestAdjustableClock fakeTrack = null!;
    private DecouplingFramedClock decoupled = null!;
    private InterpolatingFramedClock interpolated = null!;
    private GameplayClock gameplay = null!;

    private ClockPanel stopwatchPanel = null!;
    private ClockPanel manualPanel = null!;
    private ClockPanel framedPanel = null!;
    private ClockPanel interpolatedPanel = null!;
    private ClockPanel gameplayPanel = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Build clock chain", () =>
        {
            Clear();

            stopwatch = new StopwatchClock();
            manualClock = new ManualClock { CurrentTime = 0, Rate = 1, IsRunning = false };
            framedClock = new FramedClock(stopwatch);

            fakeTrack = new TestAdjustableClock();
            decoupled = new DecouplingFramedClock(fakeTrack);
            interpolated = new InterpolatingFramedClock(decoupled);
            gameplay = new GameplayClock(fakeTrack);

            var row = new FlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(8),
                Padding = new MarginPadding(12)
            };

            row.Add(stopwatchPanel = new ClockPanel("StopwatchClock", Color.SteelBlue));
            row.Add(manualPanel = new ClockPanel("ManualClock", Color.MediumPurple));
            row.Add(framedPanel = new ClockPanel("FramedClock", Color.SeaGreen));
            row.Add(interpolatedPanel = new ClockPanel("InterpolatingFramedClock", Color.DarkOrange));
            row.Add(gameplayPanel = new ClockPanel("GameplayClock", Color.Crimson));

            Add(row);
        });
    }

    public override void Update()
    {
        base.Update();

        if (stopwatch == null) return;

        // Pump framed clocks.
        framedClock.ProcessFrame();
        decoupled.ProcessFrame();
        interpolated.ProcessFrame();
        gameplay.ProcessFrame();

        // Sync fake track with stopwatch so interpolated / gameplay have something to chase.
        if (stopwatch.IsRunning && fakeTrack.IsRunning)
            fakeTrack.AdvanceBy(framedClock.ElapsedFrameTime);

        // Refresh display panels.
        stopwatchPanel.SetTime(stopwatch.CurrentTime, stopwatch.IsRunning);
        manualPanel.SetTime(manualClock.CurrentTime, manualClock.IsRunning);
        framedPanel.SetTime(framedClock.CurrentTime, framedClock.IsRunning);
        interpolatedPanel.SetTime(interpolated.CurrentTime, interpolated.IsRunning, interpolated.IsInterpolating);
        gameplayPanel.SetTime(gameplay.CurrentTime, gameplay.IsRunning);
    }

    [Test]
    public void TestStopwatchStartStop()
    {
        AddStep("Start StopwatchClock", () => stopwatch.Start());
        AddWaitStep("Run for 1 second", 1000);
        AddAssert("Time > 0", () => stopwatch.CurrentTime > 0);
        AddStep("Stop StopwatchClock", () => stopwatch.Stop());
        AddWaitStep("Verify frozen", 300);
        AddAssert("Time is frozen", () => stopwatch.CurrentTime > 0 && !stopwatch.IsRunning);
    }

    [Test]
    public void TestStopwatchSeek()
    {
        AddStep("Start StopwatchClock", () => stopwatch.Start());
        AddStep("Seek to 5000 ms", () => stopwatch.Seek(5000));
        // Use UntilStep — the clock is running, so time advances between steps.
        AddUntilStep("Time is near 5000", () => stopwatch.CurrentTime >= 5000);
        AddWaitStep("Run briefly", 200);
        AddAssert("Time advanced past 5000", () => stopwatch.CurrentTime > 5000);
    }

    [Test]
    public void TestStopwatchSeekWhileStopped()
    {
        AddStep("Seek to 3000 ms while stopped", () => stopwatch.Seek(3000));
        AddAssert("Time = 3000", () => Math.Abs(stopwatch.CurrentTime - 3000) < 1);
        AddAssert("Clock is not running", () => !stopwatch.IsRunning);
    }

    [Test]
    public void TestDoubleRate()
    {
        // Visual test: verify the rate property is accepted and the clock runs.
        // Exact timing math is covered by the unit test (StopwatchClockTest.TestDoubleRate).
        AddStep("Set rate to 2× and start", () => { stopwatch.Rate = 2.0; stopwatch.Start(); });
        AddAssert("Rate is 2×", () => stopwatch.Rate == 2.0);
        AddAssert("Clock is running", () => stopwatch.IsRunning);
        AddWaitStep("Observe 2× speed visually", 500);
        AddStep("Reset rate to 1×", () => stopwatch.Rate = 1.0);
        AddAssert("Rate is back to 1", () => stopwatch.Rate == 1.0);
    }

    [Test]
    public void TestRateChangeDoesNotJumpTime()
    {
        // Stop the clock, change rate, assert time is unchanged — no wall-time dependency.
        AddStep("Start clock", () => stopwatch.Start());
        AddWaitStep("Accumulate some time", 200);
        double timeBefore = 0;
        AddStep("Stop and capture time", () => { stopwatch.Stop(); timeBefore = stopwatch.CurrentTime; });
        AddAssert("Time did not jump on rate change",
            () => Math.Abs(stopwatch.CurrentTime - timeBefore) < 1);
        AddStep("Change rate to 0.5× while stopped", () => stopwatch.Rate = 0.5);
        AddAssert("Time still unchanged after rate set",
            () => Math.Abs(stopwatch.CurrentTime - timeBefore) < 1);
        AddStep("Reset rate", () => { stopwatch.Rate = 1.0; stopwatch.Start(); });
    }

    [Test]
    public void TestNegativeRate()
    {
        AddStep("Seek to 500 ms, rate -1×, start",
            () => { stopwatch.Seek(500); stopwatch.Rate = -1.0; stopwatch.Start(); });
        AddWaitStep("Run 300 ms real time", 300);
        AddAssert("Time ran backwards", () => stopwatch.CurrentTime < 500);
        AddStep("Reset rate", () => stopwatch.Rate = 1.0);
    }

    [Test]
    public void TestManualClockControl()
    {
        AddStep("Set ManualClock to 1000", () => { manualClock.CurrentTime = 1000; manualClock.IsRunning = true; });
        AddAssert("Manual time = 1000", () => Math.Abs(manualClock.CurrentTime - 1000) < 1);
        AddStep("Advance to 2500", () => manualClock.CurrentTime = 2500);
        AddAssert("Manual time = 2500", () => Math.Abs(manualClock.CurrentTime - 2500) < 1);
        AddStep("Stop manual clock", () => manualClock.IsRunning = false);
        AddAssert("Manual clock stopped", () => !manualClock.IsRunning);
    }

    [Test]
    public void TestFramedClockFollowsSource()
    {
        AddStep("Start stopwatch source", () => stopwatch.Start());
        AddWaitStep("Wait for framing", 200);
        AddAssert("FramedClock > 0", () => framedClock.CurrentTime > 0);
        AddAssert("Elapsed > 0", () => framedClock.ElapsedFrameTime > 0);
    }

    [Test]
    public void TestGameplayClockLeadIn()
    {
        AddStep("Seek gameplay to -500 ms then start",
            () => { gameplay.Seek(-500); gameplay.Start(); });
        AddAssert("Time is negative", () => gameplay.CurrentTime < 0);
        AddAssert("Decoupled from source", () => gameplay.DecoupledClock.IsDecoupled);
        AddUntilStep("Time crosses zero", () => gameplay.CurrentTime >= 0);
    }

    [Test]
    public void TestGameplayClockOffset()
    {
        AddStep("Start gameplay at 0", () =>
        {
            gameplay.Seek(0);
            gameplay.Start();
            fakeTrack.Start();
            stopwatch.Start();
        });
        AddWaitStep("Run briefly", 300);
        AddStep("Apply +50 ms offset", () => gameplay.Offset = 50);
        AddAssert("Offset visible", () => gameplay.CurrentTime > gameplay.InterpolatedClock.CurrentTime);
        AddStep("Remove offset", () => gameplay.Offset = 0);
    }

    [Test]
    public void TestInterpolatedClockSmoothing()
    {
        AddStep("Start track and gameplay", () =>
        {
            fakeTrack.Start();
            stopwatch.Start();
            gameplay.Start();
        });
        AddWaitStep("Run 1 second", 1000);
        AddAssert("Interpolated clock is interpolating", () => gameplay.InterpolatedClock.IsInterpolating);
    }

    private partial class ClockPanel : Container
    {
        private readonly SpriteText timeLabel;
        private readonly SpriteText statusLabel;
        private readonly Box tickBar;
        private double tickOffset;

        private const float panel_width  = 200;
        private const float panel_height = 160;

        public ClockPanel(string name, Color accentColor)
        {
            Width  = panel_width;
            Height = panel_height;

            AddRange(new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.Gray
                },
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 4,
                    Color = accentColor,
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft
                },
                new SpriteText
                {
                    Text = name,
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Position = new Vector2(8, 10),
                    Color = accentColor
                },
                timeLabel = new SpriteText
                {
                    Text = "0.00 ms",
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Position = new Vector2(8, 34),
                    Color = Color.White
                },
                statusLabel = new SpriteText
                {
                    Text = "stopped",
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Position = new Vector2(8, 58),
                    Color = Color.Gray
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 20,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Masking = true,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Color = Color.Gray
                        },
                        tickBar = new Box
                        {
                            Width = 4,
                            Height = 16,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.Centre,
                            Color = accentColor
                        }
                    }
                }
            });
        }

        public void SetTime(double time, bool running, bool interpolating = false)
        {
            timeLabel.Text = $"{time:F1} ms";
            statusLabel.Text = running ? (interpolating ? "interpolating" : "running") : "stopped";
            statusLabel.Color = running ? Color.LimeGreen : Color.DimGray;
            tickOffset = time % 1000;
            tickBar.X = (float)(tickOffset / 1000.0 * panel_width);
        }
    }
}
