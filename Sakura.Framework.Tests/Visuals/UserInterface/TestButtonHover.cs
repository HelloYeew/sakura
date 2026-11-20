// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.UserInterface;

/// <summary>
/// The hover look of a <see cref="Button"/> turned off and on under the pointer: it goes with the button's enabled state, so
/// it never stays on a disabled button, nor after the pointer has left.
/// </summary>
public partial class TestButtonHover : ManualInputManagerTestScene
{
    private HoverRecordingButton button = null!;
    private Box away = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add a button and somewhere away from it", () =>
        {
            TestContent.Add(away = new Box
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Size = new Vector2(50, 50),
            });

            TestContent.Add(button = new HoverRecordingButton
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(200, 50),
                Action = () => { },
            });
        });
    }

    [Test]
    public void TestHoverLookFollowsThePointer()
    {
        AddStep("Move the mouse onto the button", () => InputManager.MoveMouseTo(button));
        AddAssert("The hover look is on", () => button.ShowsHover);

        AddStep("Move the mouse away", () => InputManager.MoveMouseTo(away));
        AddAssert("The hover look is off", () => !button.ShowsHover);
    }

    [Test]
    public void TestTurnedOffUnderThePointer()
    {
        AddStep("Move the mouse onto the button", () => InputManager.MoveMouseTo(button));
        AddStep("Turn the button off", () => button.Enabled.Value = false);
        AddAssert("The hover look is off at once", () => !button.ShowsHover);

        AddStep("Move the mouse away", () => InputManager.MoveMouseTo(away));
        AddAssert("It stays off", () => !button.ShowsHover);
        AddAssert("The hooks stayed in pairs", () => button.HoverCount == button.HoverLostCount);
    }

    [Test]
    public void TestTurnedBackOnAfterThePointerLeft()
    {
        AddStep("Move the mouse onto the button", () => InputManager.MoveMouseTo(button));
        AddStep("Turn the button off", () => button.Enabled.Value = false);
        AddStep("Move the mouse away", () => InputManager.MoveMouseTo(away));
        AddStep("Turn the button back on", () => button.Enabled.Value = true);
        AddAssert("The hover look is off", () => !button.ShowsHover);

        AddStep("Move the mouse onto the button again", () => InputManager.MoveMouseTo(button));
        AddAssert("The hover look is on", () => button.ShowsHover);
    }

    [Test]
    public void TestTurnedBackOnUnderThePointer()
    {
        AddStep("Move the mouse onto the button", () => InputManager.MoveMouseTo(button));
        AddStep("Turn the button off", () => button.Enabled.Value = false);
        AddStep("Turn the button back on", () => button.Enabled.Value = true);
        AddAssert("The hover look is back", () => button.ShowsHover);

        AddStep("Move the mouse away", () => InputManager.MoveMouseTo(away));
        AddAssert("The hover look is off", () => !button.ShowsHover);
        AddAssert("The hooks stayed in pairs", () => button.HoverCount == button.HoverLostCount);
    }

    /// <summary>
    /// A button that records what its hover hooks were told, as an app's hover look would.
    /// </summary>
    private partial class HoverRecordingButton : Button
    {
        /// <summary>
        /// Whether the hover look is on: <see cref="Button.OnHovered"/> ran last, not <see cref="Button.OnHoverLost()"/>.
        /// </summary>
        public bool ShowsHover { get; private set; }

        /// <summary>
        /// How many times <see cref="Button.OnHovered"/> ran.
        /// </summary>
        public int HoverCount { get; private set; }

        /// <summary>
        /// How many times <see cref="Button.OnHoverLost()"/> ran.
        /// </summary>
        public int HoverLostCount { get; private set; }

        /// <summary>
        /// Puts the hover look on.
        /// </summary>
        protected override void OnHovered()
        {
            ShowsHover = true;
            HoverCount++;
        }

        /// <summary>
        /// Takes the hover look off.
        /// </summary>
        protected override void OnHoverLost()
        {
            ShowsHover = false;
            HoverLostCount++;
        }
    }
}
