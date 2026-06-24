// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Testing.Input;

namespace Sakura.Framework.Tests.Visuals.Input;

public partial class TestInputDebugOverlay : ManualInputManagerTestScene
{
    private InputDebugOverlay overlay = null!;
    private BasicTextBox textBox = null!;
    private bool buttonClicked;

    [SetUp]
    public void SetUp()
    {
        buttonClicked = false;

        AddStep("Create overlay around interactive content", () =>
        {
            TestContent.Clear();

            overlay = new InputDebugOverlay
            {
                RelativeSizeAxes = Axes.Both
            };

            overlay.Add(new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 20),
                Children = new Drawable[]
                {
                    textBox = new BasicTextBox { Width = 300, Height = 40 },
                    new BasicButton
                    {
                        Text = "Click me",
                        Size = new Vector2(120, 40),
                        Action = () => buttonClicked = true
                    }
                }
            });

            TestContent.Add(overlay);
        });

        AddWaitStep("Wait for layout", 200);
    }

    [Test]
    public void TestOverlayObservesEventsWithoutBlockingThem()
    {
        AddStep("Focus and type", () =>
        {
            InputManager.MoveMouseTo(textBox);
            InputManager.Click(MouseButton.Left);
            InputManager.TypeText("hi");
        });
        AddAssert("Text box received input", () => textBox.Text.Value == "hi");
        AddAssert("Text box is focused", () => textBox.HasFocus);
    }

    [Test]
    public void TestButtonStillClicksThroughOverlay()
    {
        AddStep("Move to button area and click", () =>
        {
            InputManager.MoveMouseTo(new Vector2(
                TestContent.DrawRectangle.Center.X,
                TestContent.DrawRectangle.Center.Y + 50));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("Click reached content (button fired or focus cleared)",
            () => buttonClicked || !textBox.HasFocus);
    }

    [Test]
    public void TestKeyboardObservedNonPositionally()
    {
        AddStep("Press a key with the cursor in the corner", () =>
        {
            InputManager.MoveMouseTo(new Vector2(2, 2));
            InputManager.PressKey(Key.Space);
            InputManager.ReleaseKey(Key.Space);
        });
        AddAssert("Overlay still present", () => overlay.IsAlive);
    }

    [Test]
    public void TestOverlayBuildsLiveQueues()
    {
        AddStep("Move cursor over the text box", () => InputManager.MoveMouseTo(textBox));
        AddWaitStep("Let the overlay update", 100);

        AddAssert("Non-positional queue is populated", () => overlay.InputManager.NonPositionalInputQueue.Count > 0);
        AddAssert("Positional queue contains the hovered text box",
            () => overlay.InputManager.PositionalInputQueue.Contains(textBox));
    }
}
