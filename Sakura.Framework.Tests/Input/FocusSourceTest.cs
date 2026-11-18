// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Input;

/// <summary>
/// Tests that <see cref="FocusEvent.Source"/> and <see cref="Drawable.IsFocusVisible"/> say what moved focus: a press of a
/// pointer, a key, a gamepad button, or code outside any press.
/// </summary>
[TestFixture]
public class FocusSourceTest
{
    private ManualClock manual = null!;
    private Container root = null!;
    private InputManager manager = null!;
    private RecordingBox first = null!;
    private RecordingBox second = null!;

    /// <summary>
    /// Starts the logger, which focus changes write to.
    /// </summary>
    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    /// <summary>
    /// Stops the logger.
    /// </summary>
    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    /// <summary>
    /// Creates two focusable boxes side by side, and a manager that has seen no press yet.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock
        {
            CurrentTime = 1000
        };
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };
        root.Load();
        root.CompleteLoad();

        manager = new InputManager();

        root.Add(first = new RecordingBox(manager)
        {
            Size = new Vector2(100)
        });
        root.Add(second = new RecordingBox(manager)
        {
            Position = new Vector2(200, 0),
            Size = new Vector2(100)
        });

        for (int i = 0; i < 3; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }

        manager.BuildQueues(root, Vector2.Zero);
    }

    /// <summary>
    /// Focus that code moves before any press is shown, as a browser shows focus set as a page loads.
    /// </summary>
    [Test]
    public void FocusFromCodeBeforeAnyPressIsShown()
    {
        manager.ChangeFocus(first);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.LastFocus?.Source, Is.EqualTo(FocusSource.Code));
            Assert.That(first.LastFocus?.IsFocusVisible, Is.True);
            Assert.That(first.IsFocusVisible, Is.True);
        }
    }

    /// <summary>
    /// A click gives focus without showing it.
    /// </summary>
    [Test]
    public void FocusFromAClickIsNotShown()
    {
        press(new Vector2(50, 50));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.HasFocus, Is.True);
            Assert.That(first.LastFocus?.Source, Is.EqualTo(FocusSource.Pointer));
            Assert.That(first.IsFocusVisible, Is.False);
        }
    }

    /// <summary>
    /// Tab gives focus and shows it.
    /// </summary>
    [Test]
    public void FocusFromTabIsShown()
    {
        tab();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.HasFocus, Is.True);
            Assert.That(first.LastFocus?.Source, Is.EqualTo(FocusSource.Keyboard));
            Assert.That(first.IsFocusVisible, Is.True);
        }
    }

    /// <summary>
    /// A gamepad button gives focus and shows it.
    /// </summary>
    [Test]
    public void FocusFromAGamepadButtonIsShown()
    {
        manager.DispatchGamepadButtonDown(new GamepadButtonEvent(new GamepadState(), GamepadButton.South, true));

        var focused = manager.FocusedDrawable as RecordingBox;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(focused, Is.Not.Null, "a box took the button");
            Assert.That(focused?.LastFocus?.Source, Is.EqualTo(FocusSource.Gamepad));
            Assert.That(focused?.IsFocusVisible, Is.True);
        }
    }

    /// <summary>
    /// Code that moves focus after a click follows the click, so a dialog opened with the mouse draws no ring around what it
    /// focused, and after a key it shows it.
    /// </summary>
    [Test]
    public void FocusFromCodeFollowsTheLastPress()
    {
        press(new Vector2(50, 50));
        manager.ChangeFocus(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.LastFocus?.Source, Is.EqualTo(FocusSource.Code), "the press had been dispatched");
            Assert.That(second.IsFocusVisible, Is.False, "the last press was the mouse's");
        }

        tab();
        manager.ChangeFocus(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.LastFocus?.Source, Is.EqualTo(FocusSource.Code));
            Assert.That(second.IsFocusVisible, Is.True, "the last press was a key");
        }
    }

    /// <summary>
    /// Losing focus stops showing it, and a click after Tab takes the ring off again.
    /// </summary>
    [Test]
    public void FocusMovedByAClickAfterTabIsNotShown()
    {
        tab();
        press(new Vector2(250, 50));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.HasFocus, Is.False);
            Assert.That(first.IsFocusVisible, Is.False, "it lost focus");
            Assert.That(second.LastFocus?.Source, Is.EqualTo(FocusSource.Pointer));
            Assert.That(second.IsFocusVisible, Is.False);
        }
    }

    /// <summary>
    /// Presses the mouse at <paramref name="position"/>.
    /// </summary>
    private void press(Vector2 position)
    {
        var state = new MouseState
        {
            Position = position
        };
        state.SetPressed(MouseButton.Left, true);

        manager.BuildQueues(root, position);
        manager.DispatchMouseDown(new MouseButtonEvent(state, MouseButton.Left, 1));
    }

    /// <summary>
    /// Presses Tab.
    /// </summary>
    private void tab() => manager.DispatchKeyDownWithFocus(new KeyEvent(Key.Tab, KeyModifiers.None, false));

    /// <summary>
    /// A tab stop that focuses itself when pressed or sent a gamepad button, and keeps the last focus event it was sent.
    /// </summary>
    private partial class RecordingBox : Box, ITabStop
    {
        private readonly InputManager manager;

        /// <summary>
        /// The last focus event this box was sent.
        /// </summary>
        public FocusEvent? LastFocus { get; private set; }

        public override bool AcceptsFocus => true;

        public bool CanBeTabbedTo => true;

        /// <summary>
        /// Creates a box that focuses itself through <paramref name="manager"/>, since it has no focus manager above it.
        /// </summary>
        public RecordingBox(InputManager manager)
        {
            this.manager = manager;
        }

        public override void OnFocus(FocusEvent e) => LastFocus = e;

        public override bool OnMouseDown(MouseButtonEvent e) => manager.ChangeFocus(this);

        public override bool OnGamepadButtonDown(GamepadButtonEvent e) => manager.ChangeFocus(this);
    }
}
