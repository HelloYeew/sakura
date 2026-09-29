// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Cursor;

/// <summary>
/// Verifies that the software cursor hears every press without intercepting it.
/// </summary>
/// <remarks>
/// The cursor is repositioned a frame after the pointer moves, so its <c>OnClick</c> can miss presses
/// made during the move.
/// </remarks>
public partial class TestCursorPress : ManualInputManagerTestScene
{
    private ProbeCursorContainer container = null!;
    private BasicButton button = null!;
    private int clicks;

    [SetUp]
    public void SetUp()
    {
        AddStep("Build", () =>
        {
            clicks = 0;
            TestContent.Clear();
            TestContent.Add(button = new BasicButton
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(200, 60),
                Text = "Target",
                Action = () => clicks++,
            });
            TestContent.Add(container = new ProbeCursorContainer { HideOsCursor = false, HideWhenOutsideWindow = false });
        });

        AddUntilStep("Loaded", () => container.IsLoaded);
    }

    /// <summary>
    /// The press lands up and to the left of where the cursor was last drawn — outside its box, which
    /// extends down and right from the tip — in the same frame as the move. Only the container can
    /// see that press.
    /// </summary>
    [Test]
    public void TestAPressOnTheMoveReachesTheCursor()
    {
        AddStep("Rest the pointer", () => InputManager.MoveMouseTo(new Vector2(300, 300)));
        AddWaitStep("Let the cursor catch up", 50);

        AddStep("Move up-left and press at once", () =>
        {
            InputManager.MoveMouseTo(new Vector2(200, 200));
            InputManager.PressButton(MouseButton.Left);
            InputManager.ReleaseButton(MouseButton.Left);
        });

        AddAssert("The cursor heard the press", () => container.Probe.Presses == 1);
    }

    [Test]
    public void TestThePressStillReachesWhatIsUnderIt()
    {
        AddStep("Click the button", () =>
        {
            InputManager.MoveMouseTo(button);
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("The button was clicked", () => clicks == 1);
        AddAssert("And the cursor heard it too", () => container.Probe.Presses == 1);
    }

    [Test]
    public void TestEveryButtonIsHeard()
    {
        AddStep("Right-click", () => InputManager.Click(MouseButton.Right));
        AddAssert("Heard as the right button", () => container.Probe.LastButton == MouseButton.Right);
    }

    /// <summary>The cursor is not something the pointer points at, so it is never under it for input.</summary>
    [Test]
    public void TestTheDefaultCursorTakesNoPointerInput()
    {
        CursorContainer scenes = null!;

        AddStep("Find the scene's own cursor", () => scenes = InputManager.Children.OfType<CursorContainer>().Single());
        AddStep("Rest the pointer", () => InputManager.MoveMouseTo(new Vector2(300, 300)));
        AddWaitStep("Let the cursor catch up", 50);
        AddStep("Press in place", () => InputManager.PressButton(MouseButton.Left));

        AddAssert("Its container is under the pointer", () => InputManager.InputManager.PositionalInputQueue.Contains(scenes));
        AddAssert("Its cursor is not", () => !InputManager.InputManager.PositionalInputQueue.Contains(scenes.ActiveCursor));
        AddStep("Release", () => InputManager.ReleaseButton(MouseButton.Left));
    }

    private partial class ProbeCursorContainer : CursorContainer
    {
        public ProbeCursor Probe { get; private set; } = null!;

        protected override Drawable CreateCursor() => Probe = new ProbeCursor();
    }

    private partial class ProbeCursor : Container, ICursorDrawable
    {
        public int Presses { get; private set; }
        public MouseButton? LastButton { get; private set; }

        public ProbeCursor()
        {
            Size = new Vector2(20);
            Add(new Box { RelativeSizeAxes = Axes.Both, Color = Color.Aquamarine });
        }

        public void ChangeCursor(CursorState state)
        {
        }

        public void ButtonPressed(MouseButton button)
        {
            Presses++;
            LastButton = button;
        }
    }
}
