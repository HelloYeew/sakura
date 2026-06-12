// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Visuals.Timing;

/// <summary>
/// Visual verification of the clock-sharing model: drawables inherit the scene clock by
/// reference, while explicitly-assigned clocks (here: rate-scaled <see cref="FramedClock"/>s)
/// are preserved and processed per frame. All three boxes run the same 360° rotation over
/// 2 seconds — the half-rate box should visibly lag and the double-rate box finish first.
/// </summary>
public partial class TestClockSharing : TestScene
{
    private Box inheritedBox = null!;
    private Box halfRateBox = null!;
    private Box doubleRateBox = null!;

    private SpriteText sceneTimeText = null!;
    private SpriteText inheritedTimeText = null!;
    private SpriteText halfTimeText = null!;
    private SpriteText doubleTimeText = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Build panels", () =>
        {
            Clear();

            var halfClock = new FramedClock(Clock)
            {
                Rate = 0.5
            };
            var doubleClock = new FramedClock(Clock)
            {
                Rate = 2.0
            };

            var row = new FlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(12),
                Padding = new MarginPadding(12)
            };

            row.Add(buildPanel("Inherited clock", Color.SeaGreen, out inheritedBox, out inheritedTimeText));
            row.Add(buildPanel("Custom clock (0.5x)", Color.SteelBlue, out halfRateBox, out halfTimeText));
            row.Add(buildPanel("Custom clock (2x)", Color.DarkOrange, out doubleRateBox, out doubleTimeText));

            // Assigning before Add marks these clocks as custom; they must survive Add()
            // and be processed by the framework every frame.
            halfRateBox.Clock = halfClock;
            doubleRateBox.Clock = doubleClock;

            Add(row);
            Add(sceneTimeText = new SpriteText
            {
                Position = new Vector2(12, 320),
                Text = "scene: 0ms",
                Color = Color.White
            });
        });
    }

    private static Container buildPanel(string title, Color boxColor, out Box box, out SpriteText timeText)
    {
        var panel = new Container { Size = new Vector2(220, 280) };

        panel.Add(new SpriteText
        {
            Position = new Vector2(10, 10),
            Text = title,
            Color = Color.White
        });

        panel.Add(box = new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(80),
            Color = boxColor
        });

        panel.Add(timeText = new SpriteText
        {
            Position = new Vector2(10, 240),
            Text = "clock: 0ms",
            Color = Color.Gray
        });

        return panel;
    }

    public override void Update()
    {
        base.Update();

        if (sceneTimeText == null)
            return;

        sceneTimeText.Text = $"scene: {Clock.CurrentTime:F0}ms";
        inheritedTimeText.Text = $"clock: {inheritedBox.Clock.CurrentTime:F0}ms";
        halfTimeText.Text = $"clock: {halfRateBox.Clock.CurrentTime:F0}ms";
        doubleTimeText.Text = $"clock: {doubleRateBox.Clock.CurrentTime:F0}ms";
    }

    [Test]
    public void TestClockOwnership()
    {
        AddAssert("inherited box shares scene clock", () => ReferenceEquals(inheritedBox.Clock, Clock));
        AddAssert("half-rate box keeps its custom clock", () => !ReferenceEquals(halfRateBox.Clock, Clock));
        AddAssert("double-rate box keeps its custom clock", () => !ReferenceEquals(doubleRateBox.Clock, Clock));
    }

    [Test]
    public void TestRateScaledRotation()
    {
        AddStep("Reset rotation", () =>
        {
            foreach (var box in new[] { inheritedBox, halfRateBox, doubleRateBox })
            {
                box.ClearTransforms();
                box.Rotation = 0;
            }
        });

        AddStep("Rotate 360° over 2s", () =>
        {
            inheritedBox.RotateTo(360, 2000);
            halfRateBox.RotateTo(360, 2000);
            doubleRateBox.RotateTo(360, 2000);
        });

        AddUntilStep("Normal-rate box completes", () => inheritedBox.Rotation >= 359.9f);
        AddAssert("Half-rate box is lagging behind", () => halfRateBox.Rotation < 359f);
        AddAssert("Double-rate box already finished", () => doubleRateBox.Rotation >= 359.9f);
        AddUntilStep("Half-rate box completes eventually", () => halfRateBox.Rotation >= 359.9f);
    }

    [Test]
    public void TestTransformScheduledBeforeAdd()
    {
        Box lateBox = null!;

        AddStep("Schedule fade before Add", () =>
        {
            lateBox = new Box
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Size = new Vector2(80),
                Color = Color.MediumPurple,
                Alpha = 0
            };

            // scheduled while the box has no parent must begin at the moment it is added.
            lateBox.FadeIn(500);

            Add(lateBox);
        });

        AddUntilStep("Box fades in", () => lateBox.Alpha >= 1f);
    }
}
