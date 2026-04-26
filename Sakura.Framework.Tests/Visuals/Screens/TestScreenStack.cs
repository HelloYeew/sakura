// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.Screens;

public class TestScreenStack : TestScene
{
    private ScreenStack stack = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Initialize screen stack", () =>
        {
            Clear();
            stack = new ScreenStack
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            };
            Add(stack);
        });
    }

    [Test]
    public void TestScreenTransitions()
    {
        AddStep("Push DummyScreen", () => stack.Push(new DummyScreen()));
        AddAssert("Current screen is DummyScreen", () => stack.CurrentScreen is DummyScreen);

        AddStep("Push SwipingScreen", () => stack.Push(new SwipingScreen()));
        AddAssert("Current screen is SwipingScreen", () => stack.CurrentScreen is SwipingScreen);

        AddStep("Exit SwipingScreen", () => stack.Exit());
        AddAssert("Current screen is DummyScreen", () => stack.CurrentScreen is DummyScreen);

        AddStep("Push another DummyScreen", () => stack.Push(new DummyScreen()));
        AddAssert("Current screen is DummyScreen", () => stack.CurrentScreen is DummyScreen);

        AddStep("Exit top DummyScreen", () => stack.Exit());
        AddAssert("Current screen is DummyScreen", () => stack.CurrentScreen is DummyScreen);

        AddStep("Exit remaining DummyScreen", () => stack.Exit());
        AddAssert("Stack is empty", () => stack.CurrentScreen == null);
    }

    [Test]
    public void TestCustomTransitions()
    {
        AddStep("Push SlideScreen (From Left)", () => stack.Push(new SlideScreen(fromLeft: true)));
        AddAssert("Current screen is SlideScreen", () => stack.CurrentScreen is SlideScreen);
        addWaitForTransition();

        AddStep("Push SlideScreen (From Right)", () => stack.Push(new SlideScreen(fromLeft: false)));
        AddAssert("Current screen is SlideScreen", () => stack.CurrentScreen is SlideScreen);
        addWaitForTransition();

        AddStep("Push ZoomScreen", () => stack.Push(new ZoomScreen()));
        AddAssert("Current screen is ZoomScreen", () => stack.CurrentScreen is ZoomScreen);
        addWaitForTransition();

        AddStep("Exit ZoomScreen", () => stack.Exit());
        AddAssert("Current screen is SlideScreen (Right)", () => stack.CurrentScreen is SlideScreen);
        addWaitForTransition();

        AddStep("Exit SlideScreen (Right)", () => stack.Exit());
        AddAssert("Current screen is SlideScreen (Left)", () => stack.CurrentScreen is SlideScreen);
        addWaitForTransition();

        AddStep("Exit SlideScreen (Left)", () => stack.Exit());
        addWaitForTransition();
        AddAssert("Stack is empty", () => stack.CurrentScreen == null);
    }

    [Test]
    public void TestScreenDepth()
    {
        DummyScreen screen1 = null!;
        DummyScreen screen2 = null!;
        DummyScreen screen3 = null!;
        DummyScreen screen4 = null!;

        AddStep("Push first screen", () => stack.Push(screen1 = new DummyScreen()));
        AddAssert("First screen depth is 0", () => screen1.Depth == 0);
        AddWaitStep("Wait for transition", 500);

        AddStep("Push second screen", () => stack.Push(screen2 = new DummyScreen()));
        AddAssert("Second screen depth is 1", () => Precision.AlmostEquals(screen2.Depth, 1));
        AddWaitStep("Wait for transition", 500);

        AddStep("Push third screen", () => stack.Push(screen3 = new DummyScreen()));
        AddAssert("Third screen depth is 2", () => Precision.AlmostEquals(screen3.Depth, 2));
        AddWaitStep("Wait for transition", 500);

        // When we exit the third screen, the stack's CurrentScreen becomes screen2 again.
        AddStep("Exit third screen", () => stack.Exit());
        AddAssert("Current screen is second screen", () => stack.CurrentScreen == screen2);

        // Wait for the exit transition to finish so screen3 is actually removed from the container
        AddWaitStep("Wait for exit transition", 500);

        // Pushing a new screen now should base its depth on screen2 (which has a depth of 1).
        // Therefore, the new screen should be assigned a depth of 2.
        AddStep("Push fourth screen", () => stack.Push(screen4 = new DummyScreen()));
        AddAssert("Fourth screen depth is 2", () => Precision.AlmostEquals(screen4.Depth, 2));
    }

    private void addWaitForTransition()
    {
        AddWaitStep("Wait for transition", 700);
    }

    private class DummyScreen : Screen
    {
        private const int transition_time = 500;

        public override void Load()
        {
            base.Load();
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;
            Size = new Vector2(1);

            Alpha = 0f;

            Add(new Box()
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Color = ColorExtensions.GetRandomColor()
            });

            Add(new SpriteText()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = Name,
                Font = FontUsage.Default.With(size: 32),
                Color = Color.White
            });
        }

        public override void OnEntering(Screen? last)
        {
            Position = new Vector2(0, 1);
            Alpha = 0;
            this.MoveTo(Vector2.Zero, transition_time, Easing.OutQuint);
            this.FadeIn(transition_time, Easing.OutQuint);
        }

        public override void OnExiting(Screen? next)
        {
            this.MoveTo(new Vector2(0, 1), transition_time, Easing.InQuint);
            this.FadeOut(transition_time, Easing.InQuint);
        }

        public override void OnResuming(Screen? last)
        {
            this.FadeIn(transition_time / 2);
        }

        public override void OnSuspending(Screen next)
        {
            this.FadeOut(transition_time / 2);
        }
    }

    private class SwipingScreen : Screen
    {
        public override void Load()
        {
            base.Load();
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;
            Size = new Vector2(1);
            Scale = new Vector2(0);
            Alpha = 0f;

            Add(new Box()
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Color = ColorExtensions.GetRandomColor()
            });

            Add(new SpriteText()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = Name,
                Font = FontUsage.Default.With(size: 32),
                Color = Color.White
            });
        }

        public override void OnEntering(Screen? last)
        {
            this.ScaleTo(1f, 300, Easing.OutQuint)
                .FadeIn(300);
        }

        public override void OnExiting(Screen? next)
        {
            this.ScaleTo(0f, 300, Easing.InQuint)
                .FadeOut(300);
        }
    }

    private class SlideScreen : Screen
    {
        private readonly int slideDirection;

        public SlideScreen(bool fromLeft)
        {
            slideDirection = fromLeft ? -1 : 1;
        }

        public override void Load()
        {
            base.Load();
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;
            RelativePositionAxes = Axes.X;
            Size = new Vector2(1);
            Alpha = 0f;

            Add(new Box()
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Color = ColorExtensions.GetRandomColor()
            });
        }

        public override void OnEntering(Screen? last)
        {
            Position = new Vector2(slideDirection, 0);

            this.FadeIn(500, Easing.OutQuint)
                .MoveTo(Vector2.Zero, 500, Easing.OutQuint);
        }

        public override void OnExiting(Screen? next)
        {
            this.MoveTo(new Vector2(slideDirection, 0), 500, Easing.InQuint);
            this.FadeOut(500, Easing.InQuint);
        }

        public override void OnSuspending(Screen next)
        {
            this.MoveTo(new Vector2(-slideDirection * 0.2f, 0), 500, Easing.OutQuint);
            this.FadeOut(500, Easing.OutQuint);
        }

        public override void OnResuming(Screen? last)
        {
            this.MoveTo(Vector2.Zero, 500, Easing.OutQuint);
            this.FadeIn(500, Easing.OutQuint);
        }
    }

    private class ZoomScreen : Screen
    {
        public override void Load()
        {
            base.Load();
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;
            Size = new Vector2(1);

            Scale = new Vector2(0);
            Alpha = 0f;

            Add(new Box()
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Color = ColorExtensions.GetRandomColor()
            });

            Add(new SpriteText()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = Name,
                Font = FontUsage.Default.With(size: 32),
                Color = Color.White
            });
        }

        public override void OnEntering(Screen? last)
        {
            this.ScaleTo(1f, 600, Easing.OutElastic)
                .FadeIn(400);
        }

        public override void OnExiting(Screen? next)
        {
            this.ScaleTo(0f, 400, Easing.InQuint)
                .FadeOut(400);
        }

        public override void OnSuspending(Screen next)
        {
            this.ScaleTo(0.8f, 400, Easing.OutQuint)
                .FadeOut(400);
        }

        public override void OnResuming(Screen? last)
        {
            this.ScaleTo(1f, 500, Easing.OutElastic)
                .FadeIn(300);
        }
    }
}
