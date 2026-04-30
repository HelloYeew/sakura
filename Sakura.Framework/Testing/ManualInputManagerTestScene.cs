// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing.Input;

namespace Sakura.Framework.Testing;

public abstract class ManualInputManagerTestScene : TestScene
{
    /// <summary>
    /// Tests should add their visual components to this container instead of directly to the scene.
    /// </summary>
    protected Container TestContent { get; }

    protected Vector2 InitialMousePosition => Vector2.Zero;

    protected ManualInputManager InputManager { get; }

    private readonly BasicButton buttonTest;
    private readonly BasicButton buttonLocal;

    protected ManualInputManagerTestScene()
    {
        TestContent = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1)
        };

        InputManager = new ManualInputManager
        {
            UseParentInput = false,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1)
        };

        InputManager.Add(TestContent);

        InputManager.Add(new CursorContainer
        {
            Depth = float.MaxValue
        });

        var inputToggleOverlay = new Container
        {
            Depth = float.MaxValue,
            AutoSizeAxes = Axes.Both,
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Margin = new MarginPadding(5),
            CornerRadius = 5,
            Masking = true,
            Children = new Drawable[]
            {
                new Box
                {
                    Color = Color.White,
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0.1f,
                    Size = new Vector2(1)
                },
                new FlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Size = new Vector2(1),
                    Direction = FlowDirection.Vertical,
                    Margin = new MarginPadding(5),
                    Spacing = new Vector2(0, 5),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "Input Priority",
                            Color = Color.White
                        },
                        new FlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FlowDirection.Horizontal,
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Spacing = new Vector2(5, 0),
                            Children = new Drawable[]
                            {
                                buttonLocal = new BasicButton
                                {
                                    Text = "Local",
                                    Size = new Vector2(60, 30),
                                    Action = returnUserInput
                                },
                                buttonTest = new BasicButton
                                {
                                    Text = "Test",
                                    Size = new Vector2(60, 30),
                                    Action = returnTestInput
                                }
                            }
                        }
                    }
                }
            }
        };

        base.Add(InputManager);
        base.Add(inputToggleOverlay);
    }

    public override void Update()
    {
        base.Update();

        buttonTest.Enabled.Value = InputManager.UseParentInput;
        buttonLocal.Enabled.Value = !InputManager.UseParentInput;
    }

    [SetUp]
    public void SetUpInputManager()
    {
        ResetInput();
    }

    protected void ResetInput()
    {
        InputManager.MoveMouseTo(InitialMousePosition);

        InputManager.ReleaseButton(MouseButton.Left);
        InputManager.ReleaseButton(MouseButton.Right);
        InputManager.ReleaseButton(MouseButton.Middle);

        Scheduler.Add(returnTestInput);
    }

    private void returnUserInput() => InputManager.UseParentInput = true;
    private void returnTestInput() => InputManager.UseParentInput = false;

    /// <summary>
    /// Synthesizes a rapid key press and release.
    /// </summary>
    protected void PressKeyOnce(Key key)
    {
        InputManager.PressKey(key);
        InputManager.ReleaseKey(key);
    }

    public override void Clear()
    {
        // Only clear the test-specific content added by the test classes.
        // Fallback to base.Clear() just in case this is called before TestContent is initialized.
        if (TestContent != null)
        {
            TestContent.Clear();
        }
        else
        {
            base.Clear();
        }
    }
}
