// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.TransformSequenceExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.Drawables;

[TestFixture]
public partial class TestTransforms : TestScene
{
    private Box subject = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Create subject box", () =>
        {
            Clear();
            subject = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(100),
                Color = Color.CornflowerBlue
            };
            Add(subject);
        });
    }

    [Test]
    public void TestFadeInOut()
    {
        AddStep("FadeOut 500ms", () => subject.FadeOut(500));
        AddUntilStep("Wait until invisible", () => subject.Alpha == 0);
        AddStep("FadeIn 500ms", () => subject.FadeIn(500));
        AddUntilStep("Wait until visible", () => subject.Alpha == 1);
        AddAssert("Alpha is 1", () => subject.Alpha == 1);
    }

    [Test]
    public void TestFadeInFromZero()
    {
        float alphaAtStart = 1f;

        AddStep("FadeInFromZero 800ms", () =>
        {
            subject.FadeInFromZero(800);
            alphaAtStart = subject.Alpha; // captured immediately after the instant set
        });
        AddAssert("Alpha was set to 0 at start", () => Precision.AlmostEquals(alphaAtStart, 0f));
        AddUntilStep("Wait until fully visible", () => subject.Alpha >= 0.99f);
    }

    [Test]
    public void TestMoveTo()
    {
        AddStep("Move to (-150, -100) over 600ms", () => subject.MoveTo(new Vector2(-150, -100), 600, Easing.OutCubic));
        AddWaitStep("Wait", 600);
        AddStep("Move back to centre over 600ms", () => subject.MoveTo(Vector2.Zero, 600, Easing.OutCubic));
        AddWaitStep("Wait", 600);
    }

    [Test]
    public void TestMoveToOffset()
    {
        AddStep("MoveToOffset (200, 0) over 400ms", () => subject.MoveToOffset(new Vector2(200, 0), 400, Easing.InOutSine));
        AddWaitStep("Wait", 400);
        AddStep("MoveToOffset back (-200, 0) over 400ms", () => subject.MoveToOffset(new Vector2(-200, 0), 400, Easing.InOutSine));
        AddWaitStep("Wait", 400);
    }

    [Test]
    public void TestScaleTo()
    {
        AddStep("Scale to 2x over 500ms", () => subject.ScaleTo(2f, 500, Easing.OutBack));
        AddWaitStep("Wait", 500);
        AddStep("Scale back to 1x over 500ms", () => subject.ScaleTo(1f, 500, Easing.InBack));
        AddWaitStep("Wait", 500);
    }

    [Test]
    public void TestResizeTo()
    {
        AddStep("Resize to 200x50 over 400ms", () => subject.ResizeTo(new Vector2(200, 50), 400, Easing.OutCubic));
        AddWaitStep("Wait", 400);
        AddStep("Resize back to 100x100 over 400ms", () => subject.ResizeTo(new Vector2(100), 400, Easing.OutCubic));
        AddWaitStep("Wait", 400);
    }

    [Test]
    public void TestRotateTo()
    {
        AddStep("Rotate to 180° over 600ms", () => subject.RotateTo(180, 600, Easing.OutBack));
        AddWaitStep("Wait", 600);
        AddStep("Rotate to 360° over 600ms", () => subject.RotateTo(360, 600, Easing.InOutCubic));
        AddWaitStep("Wait", 600);
        AddStep("Reset rotation", () => subject.RotateTo(0));
    }

    [Test]
    public void TestSpin()
    {
        AddStep("Spin clockwise (600ms/rev)", () => subject.Spin(600, RotationDirection.Clockwise));
        AddWaitStep("Spin for 2 seconds", 2000);
        AddStep("Stop spin", () => subject.ClearTransforms());
    }

    [Test]
    public void TestSpinCounterClockwise()
    {
        AddStep("Spin counter-clockwise (800ms/rev)", () => subject.Spin(800, RotationDirection.CounterClockwise));
        AddWaitStep("Spin for 2 seconds", 2000);
        AddStep("Stop spin", () => subject.ClearTransforms());
    }

    [Test]
    public void TestFadeToColor()
    {
        AddStep("Fade to red 500ms", () => subject.FadeToColor(Color.Red, 500));
        AddWaitStep("Wait", 500);
        AddStep("Fade to green 500ms", () => subject.FadeToColor(Color.Green, 500));
        AddWaitStep("Wait", 500);
        AddStep("Fade to original 500ms", () => subject.FadeToColor(Color.CornflowerBlue, 500));
        AddWaitStep("Wait", 500);
    }

    [Test]
    public void TestFlashColor()
    {
        AddStep("Flash white 400ms", () => subject.FlashColor(Color.White, 400));
        AddWaitStep("Wait", 500);
        AddStep("Flash red 400ms", () => subject.FlashColor(Color.Red, 400));
        AddWaitStep("Wait", 500);
    }

    [Test]
    public void TestLoopInfinite()
    {
        AddStep("Fade loop (infinite)", () =>
            subject.FadeTo(0.1f, 600).Then().FadeTo(1f, 600).Loop());
        AddWaitStep("Watch loop for 3s", 3000);
        AddStep("Stop", () => subject.ClearTransforms());
    }

    [Test]
    public void TestLoopWithPause()
    {
        AddStep("Fade loop with 300ms pause", () =>
            subject.FadeTo(0.1f, 500).Then().FadeTo(1f, 500).Loop(pause: 300));
        AddWaitStep("Watch for 4s", 4000);
        AddStep("Stop", () => subject.ClearTransforms());
    }

    [Test]
    public void TestLoopFiniteCount()
    {
        AddStep("Scale pulse × 3", () =>
            subject.Loop(b => b.ScaleTo(1.4f, 300, Easing.OutCubic).Then().ScaleTo(1f, 300), 600, count: 3));
        AddWaitStep("Wait past completion (2000ms)", 2000);
        AddAssert("Back to original scale", () => Precision.AlmostEquals(subject.Scale, Vector2.One));
    }

    [Test]
    public void TestLoopBuilder()
    {
        AddStep("Loop builder: fade 0.25↔1 over 1250ms", () => subject.Loop(b => b.FadeTo(0.25f).Then().FadeTo(1f, 1000), 1250));
        AddWaitStep("Watch for 4s", 4000);
        AddStep("Stop", () => subject.ClearTransforms());
    }

    [Test]
    public void TestLoopBuilderWithCount()
    {
        AddStep("Loop builder ×4: scale bounce", () =>
            subject.Loop(b => b.ScaleTo(1.3f, 250, Easing.OutBack).Then().ScaleTo(1f, 250), 600, count: 4));
        AddWaitStep("Wait past completion (2600ms)", 2600);
        AddAssert("Scale restored", () => Precision.AlmostEquals(subject.Scale, Vector2.One));
    }

    [Test]
    public void TestSequenceChain()
    {
        AddStep("Sequence: fade out → move → fade in", () =>
            subject.TransformSequence()
                   .FadeOut(200)
                   .Then()
                   .MoveTo(new Vector2(150, -80), 400, Easing.OutCubic)
                   .Then(100)
                   .FadeIn(200));
        AddWaitStep("Watch sequence", 1000);
    }

    [Test]
    public void TestSequenceLoop()
    {
        AddStep("Sequence loop: fade 0.25↔1", () =>
            subject.TransformSequence()
                   .FadeTo(0.25f)
                   .Then()
                   .FadeTo(1f, 1000)
                   .Loop(1250));
        AddWaitStep("Watch for 4s", 4000);
        AddStep("Stop", () => subject.ClearTransforms());
    }

    [Test]
    public void TestSequenceOnComplete()
    {
        bool completed = false;

        AddStep("Sequence with OnComplete callback", () =>
        {
            completed = false;
            subject.TransformSequence()
                   .ScaleTo(2f, 400, Easing.OutBack)
                   .Then()
                   .FadeOut(300)
                   .OnComplete(_ => completed = true);
        });
        AddUntilStep("Wait for completion", () => completed);
        AddAssert("Callback fired", () => completed);
    }

    [Test]
    public void TestEasingComparison()
    {
        Box boxA = null!, boxB = null!;

        AddStep("Setup two boxes (OutCubic vs OutBack)", () =>
        {
            Clear();
            Add(boxA = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(80),
                Color = Color.CornflowerBlue,
                Position = new Vector2(-100, 0)
            });
            Add(boxB = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(80),
                Color = Color.LightPink,
                Position = new Vector2(100, 0)
            });
        });

        AddSliderStep("Scale amount", 0.5f, 3f, 1.5f, v =>
        {
            boxA?.ClearTransforms();
            boxB?.ClearTransforms();
            boxA?.ScaleTo(v, 600, Easing.OutCubic);
            boxB?.ScaleTo(v, 600, Easing.OutBack);
        });
    }

    [Test]
    public void TestCubicBezierMove()
    {
        // CSS "ease-in-out" curve.
        AddStep("Move with cubic-bezier(0.42,0,0.58,1)", () =>
            subject.MoveTo(new Vector2(180, 0), 700, EasingFunction.CubicBezier(0.42, 0, 0.58, 1)));
        AddWaitStep("Wait", 700);
        AddStep("Move back with cubic-bezier ease-in-out", () =>
            subject.MoveTo(Vector2.Zero, 700, EasingFunction.CubicBezier(0.42, 0, 0.58, 1)));
        AddWaitStep("Wait", 700);
        AddAssert("Back at centre", () => Precision.AlmostEquals(subject.Position, Vector2.Zero));
    }

    [Test]
    public void TestSpringScale()
    {
        AddStep("Scale to 2x with spring", () =>
            subject.ScaleTo(2f, 800, EasingFunction.Spring()));
        AddWaitStep("Wait", 800);
        AddAssert("Rests exactly at 2x", () => Precision.AlmostEquals(subject.Scale, new Vector2(2f)));
        AddStep("Scale back to 1x with bouncy spring", () =>
            subject.ScaleTo(1f, 800, EasingFunction.Spring(dampingRatio: 0.35, frequency: 2.5)));
        AddWaitStep("Wait", 800);
        AddAssert("Rests exactly at 1x", () => Precision.AlmostEquals(subject.Scale, Vector2.One));
    }

    [Test]
    public void TestSpringMoveSequence()
    {
        AddStep("Sequence: spring right → spring back", () =>
            subject.TransformSequence()
                   .MoveTo(new Vector2(200, 0), 700, EasingFunction.Spring(0.4, 2f))
                   .Then()
                   .MoveTo(Vector2.Zero, 700, EasingFunction.Spring(0.4, 2f)));
        AddWaitStep("Watch sequence", 1600);
        AddAssert("Back at centre", () => Precision.AlmostEquals(subject.Position, Vector2.Zero));
    }

    [Test]
    public void TestEasingComparisonCubicVsSpring()
    {
        Box boxA = null!, boxB = null!;

        AddStep("Setup two boxes (CubicBezier vs Spring)", () =>
        {
            Clear();
            Add(boxA = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(80),
                Color = Color.CornflowerBlue,
                Position = new Vector2(-100, 0)
            });
            Add(boxB = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(80),
                Color = Color.LightPink,
                Position = new Vector2(100, 0)
            });
        });

        AddSliderStep("Scale amount", 0.5f, 3f, 1.5f, v =>
        {
            boxA?.ClearTransforms();
            boxB?.ClearTransforms();
            boxA?.ScaleTo(v, 700, EasingFunction.CubicBezier(0.42, 0, 0.58, 1));
            boxB?.ScaleTo(v, 700, EasingFunction.Spring());
        });
    }

    [Test]
    public void TestSpringDampingComparison()
    {
        // Several boxes stacked vertically, each with a different damping ratio, all launched
        // together so the effect of damping (bouncy → critical → over-damped) is visible side by side.
        (double damping, Color color)[] rows =
        {
            (0.2, Color.Tomato),
            (0.4, Color.Orange),
            (0.6, Color.MediumSeaGreen),
            (1.0, Color.CornflowerBlue),
            (1.6, Color.MediumPurple),
        };

        var boxes = new Box[rows.Length];

        AddStep("Setup boxes (damping 0.2 → 1.6)", () =>
        {
            Clear();
            for (int i = 0; i < rows.Length; i++)
            {
                float y = (i - (rows.Length - 1) / 2f) * 70f;
                Add(boxes[i] = new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(50),
                    Color = rows[i].color,
                    Position = new Vector2(-220, y)
                });
            }
        });

        AddStep("Spring all to the right", () =>
        {
            for (int i = 0; i < rows.Length; i++)
                boxes[i].MoveToX(220, 900, EasingFunction.Spring(dampingRatio: rows[i].damping, frequency: 2f));
        });
        AddWaitStep("Watch settle", 1000);
        AddAssert("All rested at target X", () =>
        {
            for (int i = 0; i < boxes.Length; i++)
                if (!Precision.AlmostEquals(boxes[i].Position.X, 220f))
                    return false;
            return true;
        });

        AddStep("Spring all back", () =>
        {
            for (int i = 0; i < rows.Length; i++)
                boxes[i].MoveToX(-220, 900, EasingFunction.Spring(dampingRatio: rows[i].damping, frequency: 2f));
        });
        AddWaitStep("Watch settle", 1000);
    }

    [Test]
    public void TestSpringInteractive()
    {
        double damping = SpringEasing.DEFAULT_DAMPING_RATIO;
        double frequency = SpringEasing.DEFAULT_FREQUENCY;
        bool toRight = true;

        AddStep("Reset subject to left", () => subject.MoveToX(-200));

        AddSliderStep("Damping ratio", 0.1f, 2f, (float)SpringEasing.DEFAULT_DAMPING_RATIO, v => damping = v);
        AddSliderStep("Frequency", 0.5f, 5f, (float)SpringEasing.DEFAULT_FREQUENCY, v => frequency = v);

        AddStep("Spring to other side", () =>
        {
            float targetX = toRight ? 200 : -200;
            toRight = !toRight;
            subject.MoveToX(targetX, 900, EasingFunction.Spring(damping, frequency));
        });
    }

    [Test]
    public void TestCubicBezierPresets()
    {
        // Compares common CSS cubic-bezier presets driven off the same trigger.
        Box ease = null!, easeIn = null!, easeOut = null!, easeInOut = null!;

        AddStep("Setup preset boxes", () =>
        {
            Clear();
            Add(ease = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(45),
                Color = Color.CornflowerBlue,
                Position = new Vector2(-220, -90)
            });
            Add(easeIn = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(45),
                Color = Color.MediumSeaGreen,
                Position = new Vector2(-220, -30)
            });
            Add(easeOut = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(45),
                Color = Color.Orange,
                Position = new Vector2(-220, 30)
            });
            Add(easeInOut = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(45),
                Color = Color.MediumPurple,
                Position = new Vector2(-220, 90)
            });
        });

        AddStep("Run all presets", () =>
        {
            ease.MoveToX(220, 900, EasingFunction.CubicBezier(0.25, 0.1, 0.25, 1.0));   // ease
            easeIn.MoveToX(220, 900, EasingFunction.CubicBezier(0.42, 0, 1, 1));         // ease-in
            easeOut.MoveToX(220, 900, EasingFunction.CubicBezier(0, 0, 0.58, 1));        // ease-out
            easeInOut.MoveToX(220, 900, EasingFunction.CubicBezier(0.42, 0, 0.58, 1));   // ease-in-out
        });
        AddWaitStep("Watch", 1000);
        AddAssert("All reached target", () =>
            Precision.AlmostEquals(ease.Position.X, 220f)
            && Precision.AlmostEquals(easeIn.Position.X, 220f)
            && Precision.AlmostEquals(easeOut.Position.X, 220f)
            && Precision.AlmostEquals(easeInOut.Position.X, 220f));
    }

    [TearDown]
    public void TearDown()
    {
        AddStep("Clear transforms and children", () =>
        {
            subject?.ClearTransforms();
            Clear();
        });
    }
}
