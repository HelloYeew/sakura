// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Input;

public class TestCursorState : ManualInputManagerTestScene
{
    private CursorZone pointerZone;
    private CursorZone textZone;
    private CursorZone waitZone;

    [Resolved]
    private IWindow window { get; set; }

    [SetUp]
    public void SetUp()
    {
        AddStep("Initialize cursor zones", () =>
        {
            TestContent.Clear();

            TestContent.Add(new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(20, 0),
                Children = new Drawable[]
                {
                    pointerZone = new CursorZone("Pointer (Hand)", CursorState.Pointer)
                    {
                        BoxColor = Color.SeaGreen
                    },
                    textZone = new CursorZone("Text (I-Beam)", CursorState.Text)
                    {
                        BoxColor = Color.SteelBlue
                    },
                    waitZone = new CursorZone("Wait (Spinner)", CursorState.Wait)
                    {
                        BoxColor = Color.IndianRed
                    }
                }
            });
        });
    }

    [Test]
    public void TestAutomatedCursorChanges()
    {
        AddStep("Move to empty space", () => InputManager.MoveMouseTo(Vector2.Zero));
        AddWaitStep("Wait to observe Default", 500);

        AddStep("Move to Pointer zone", () => InputManager.MoveMouseTo(pointerZone));
        AddWaitStep("Wait to observe Pointer", 800);

        AddStep("Move to Text zone", () => InputManager.MoveMouseTo(textZone));
        AddWaitStep("Wait to observe Text", 800);

        AddStep("Move to Wait zone", () => InputManager.MoveMouseTo(waitZone));
        AddWaitStep("Wait to observe Wait", 800);

        AddStep("Return to empty space", () => InputManager.MoveMouseTo(Vector2.Zero));
    }

    /// <summary>
    /// A custom container that changes the cursor state when hovered.
    /// </summary>
    private class CursorZone : Container
    {
        private readonly CursorState targetState;
        private Box background;

        [Resolved]
        private IWindow window { get; set; }

        public Color BoxColor
        {
            get => background.Color;
            set => background.Color = value;
        }

        public CursorZone(string label, CursorState state)
        {
            targetState = state;
            Size = new Vector2(150, 200);
            Masking = true;
            CornerRadius = 10;

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(1),
                    Color = Color.Gray
                },
                new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = label,
                    Color = Color.White
                }
            };
        }

        public override bool OnHover(MouseEvent e)
        {
            window.CursorState = targetState;
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            window.CursorState = CursorState.Default;
            return base.OnHoverLost(e);
        }
    }
}
