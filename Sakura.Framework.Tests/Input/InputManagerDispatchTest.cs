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
/// non-positional dispatch (keyboard / text / gamepad) running through the
/// <see cref="InputManager"/>'s non-positional input queue rather than the recursive path
/// </summary>
[TestFixture]
public class InputManagerDispatchTest
{
    private ManualClock manual = null!;
    private Container root = null!;
    private InputManager manager = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

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
        root.LoadComplete();

        manager = new InputManager();
    }

    private void settle()
    {
        for (int i = 0; i < 3; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }
    }

    private static KeyEvent key(Key k = Key.A) => new KeyEvent(k, KeyModifiers.None, false);

    [Test]
    public void TestKeyDownReachesOptedInHandler()
    {
        // A handler far off-screen still receives keys: dispatch is non-positional.
        var handler = new RecordingBox
        {
            Position = new Vector2(700, 500),
            Size = new Vector2(50),
            HandleKeyDown = true
        };
        root.Add(handler);
        settle();

        manager.BuildQueues(root);
        bool handled = manager.DispatchKeyDown(key());

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(handler.KeyDownCount, Is.EqualTo(1), "The opted-in handler received the key via the queue.");
            Assert.That(manager.LastNonPositionalHandler, Is.SameAs(handler), "The consuming entry is recorded.");
        });
    }

    [Test]
    public void TestKeyDownStopsAtFirstConsumer()
    {
        // `front` is added later (deeper in index order is irrelevant for non-positional; both are
        // direct children). The queue is post-order over children in index order, so the first
        // child added is tried first.
        var first = new RecordingBox { Size = new Vector2(10), HandleKeyDown = true };
        var second = new RecordingBox { Size = new Vector2(10), HandleKeyDown = true };
        root.Add(first);
        root.Add(second);
        settle();

        manager.BuildQueues(root);
        manager.DispatchKeyDown(key());

        Assert.Multiple(() =>
        {
            Assert.That(first.KeyDownCount, Is.EqualTo(1));
            Assert.That(second.KeyDownCount, Is.EqualTo(0), "Once a handler consumes the key, dispatch stops.");
        });
    }

    [Test]
    public void TestInertDrawablesDoNotAffectKeyDelivery()
    {
        // narrowing the non-positional queue to actual handlers must not change
        // observable dispatch behavior. Inert drawables (no key/text/gamepad override) are excluded
        // from the queue, but a real handler still receives the key exactly as before whether the
        // inert drawables are added before or after it in the tree.
        var inertBefore = new InertBox { Size = new Vector2(10) };
        var handler = new RecordingBox { Size = new Vector2(10), HandleKeyDown = true };
        var inertAfter = new InertBox { Size = new Vector2(10) };
        root.Add(inertBefore);
        root.Add(handler);
        root.Add(inertAfter);
        settle();

        manager.BuildQueues(root);
        bool handled = manager.DispatchKeyDown(key());

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(handler.KeyDownCount, Is.EqualTo(1), "The handler receives the key regardless of inert siblings.");
            Assert.That(manager.LastNonPositionalHandler, Is.SameAs(handler));
        });
    }

    [Test]
    public void TestKeyDownFallsThroughUnhandled()
    {
        var ignorer = new RecordingBox { Size = new Vector2(10), HandleKeyDown = false };
        var handler = new RecordingBox { Size = new Vector2(10), HandleKeyDown = true };
        root.Add(ignorer);
        root.Add(handler);
        settle();

        manager.BuildQueues(root);
        bool handled = manager.DispatchKeyDown(key());

        Assert.Multiple(() =>
        {
            Assert.That(ignorer.KeyDownCount, Is.EqualTo(1), "The first entry is still offered the key.");
            Assert.That(handler.KeyDownCount, Is.EqualTo(1), "An unhandled key falls through to the next entry.");
            Assert.That(handled, Is.True);
        });
    }

    [Test]
    public void TestChildHandlesBeforeParent()
    {
        // A child entry precedes its (container) parent in the post-order queue, so the child is
        // tried first. The parent is a RecordingContainer with real OnKeyDown self-logic.
        var parent = new RecordingContainer { Size = new Vector2(100), HandleKeyDown = true };
        var child = new RecordingBox { Size = new Vector2(50), HandleKeyDown = true };
        parent.Add(child);
        root.Add(parent);
        settle();

        manager.BuildQueues(root);
        manager.DispatchKeyDown(key());

        Assert.Multiple(() =>
        {
            Assert.That(child.KeyDownCount, Is.EqualTo(1), "The child (deeper in the tree) is tried first.");
            Assert.That(parent.KeyDownCount, Is.EqualTo(0), "The parent does not get the key once the child consumes it.");
        });
    }

    [Test]
    public void TestContainerSelfLogicRunsWithRecursionSuppressed()
    {
        // A container with self-logic still handles the key via the queue (its own OnKeyDown runs),
        // but its recursive children-walk is suppressed so a child is reached only via its own entry.
        var container = new RecordingContainer { Size = new Vector2(100), HandleKeyDown = true };
        var child = new RecordingBox { Size = new Vector2(50), HandleKeyDown = false };
        container.Add(child);
        root.Add(container);
        settle();

        manager.BuildQueues(root);
        bool handled = manager.DispatchKeyDown(key());

        Assert.Multiple(() =>
        {
            Assert.That(child.KeyDownCount, Is.EqualTo(1), "The child gets exactly one key via its own queue entry.");
            Assert.That(container.KeyDownCount, Is.EqualTo(1), "The container's own OnKeyDown self-logic still runs.");
            Assert.That(handled, Is.True);
        });
    }

    [Test]
    public void TestNonPositionalOptOutNeverReceivesKey()
    {
        var optedOut = new OptOutNonPositional { Size = new Vector2(50), HandleKeyDown = true };
        root.Add(optedOut);
        settle();

        manager.BuildQueues(root);
        bool handled = manager.DispatchKeyDown(key());

        Assert.Multiple(() =>
        {
            Assert.That(optedOut.KeyDownCount, Is.EqualTo(0), "An opted-out drawable is absent from the queue and never dispatched to.");
            Assert.That(handled, Is.False);
            Assert.That(manager.LastNonPositionalHandler, Is.Null);
        });
    }

    [Test]
    public void TestKeyUpDispatches()
    {
        var handler = new RecordingBox { Size = new Vector2(10), HandleKeyUp = true };
        root.Add(handler);
        settle();

        manager.BuildQueues(root);
        bool handled = manager.DispatchKeyUp(key());

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(handler.KeyUpCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestTextInputDispatches()
    {
        var handler = new RecordingBox { Size = new Vector2(10), HandleText = true };
        root.Add(handler);
        settle();

        manager.BuildQueues(root);
        bool handled = manager.DispatchTextInput(new TextInputEvent("hi"));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(handler.TextInputCount, Is.EqualTo(1));
            Assert.That(handler.LastText, Is.EqualTo("hi"));
        });
    }

    [Test]
    public void TestTextEditingDispatches()
    {
        var handler = new RecordingBox { Size = new Vector2(10), HandleText = true };
        root.Add(handler);
        settle();

        manager.BuildQueues(root);
        bool handled = manager.DispatchTextEditing(new TextEditingEvent("ab", 0, 2));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(handler.TextEditingCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestGamepadButtonDispatches()
    {
        var handler = new RecordingBox { Size = new Vector2(10), HandleGamepad = true };
        root.Add(handler);
        settle();

        var state = new GamepadState { DeviceId = 0 };
        var down = new GamepadButtonEvent(state, GamepadButton.South, isPressed: true);
        var up = new GamepadButtonEvent(state, GamepadButton.South, isPressed: false);

        manager.BuildQueues(root);

        Assert.Multiple(() =>
        {
            Assert.That(manager.DispatchGamepadButtonDown(down), Is.True);
            Assert.That(manager.DispatchGamepadButtonUp(up), Is.True);
            Assert.That(handler.GamepadDownCount, Is.EqualTo(1));
            Assert.That(handler.GamepadUpCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestGamepadConnectionBroadcasts()
    {
        // Connection events are broadcast (not consumed): every opted-in entry is notified.
        var a = new RecordingBox { Size = new Vector2(10) };
        var b = new RecordingBox { Size = new Vector2(10) };
        root.Add(a);
        root.Add(b);
        settle();

        manager.BuildQueues(root);
        manager.DispatchGamepadConnected(new GamepadConnectedEvent(0, "pad"));
        manager.DispatchGamepadDisconnected(new GamepadDisconnectedEvent(0));

        Assert.Multiple(() =>
        {
            Assert.That(a.GamepadConnectedCount, Is.EqualTo(1));
            Assert.That(b.GamepadConnectedCount, Is.EqualTo(1));
            Assert.That(a.GamepadDisconnectedCount, Is.EqualTo(1));
            Assert.That(b.GamepadDisconnectedCount, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A drawable that overrides no non-positional handler, so it is inert for key/text/gamepad input
    /// and excluded from the non-positional queue. Used to characterize that inert drawables do not
    /// affect dispatch to real handlers.
    /// </summary>
    private partial class InertBox : Box
    {
    }

    private partial class RecordingBox : Box
    {
        public bool HandleKeyDown;
        public bool HandleKeyUp;
        public bool HandleText;
        public bool HandleGamepad;

        public int KeyDownCount;
        public int KeyUpCount;
        public int TextInputCount;
        public int TextEditingCount;
        public int GamepadDownCount;
        public int GamepadUpCount;
        public int GamepadConnectedCount;
        public int GamepadDisconnectedCount;
        public string LastText;

        public override bool OnKeyDown(KeyEvent e)
        {
            KeyDownCount++;
            return HandleKeyDown;
        }

        public override bool OnKeyUp(KeyEvent e)
        {
            KeyUpCount++;
            return HandleKeyUp;
        }

        public override bool OnTextInput(TextInputEvent e)
        {
            TextInputCount++;
            LastText = e.Text;
            return HandleText;
        }

        public override bool OnTextEditing(TextEditingEvent e)
        {
            TextEditingCount++;
            return HandleText;
        }

        public override bool OnGamepadButtonDown(GamepadButtonEvent e)
        {
            GamepadDownCount++;
            return HandleGamepad;
        }

        public override bool OnGamepadButtonUp(GamepadButtonEvent e)
        {
            GamepadUpCount++;
            return HandleGamepad;
        }

        public override void OnGamepadConnected(GamepadConnectedEvent e) => GamepadConnectedCount++;
        public override void OnGamepadDisconnected(GamepadDisconnectedEvent e) => GamepadDisconnectedCount++;
    }

    private partial class OptOutNonPositional : RecordingBox
    {
        public override bool HandleNonPositionalInput => false;
    }

    private partial class RecordingContainer : Container
    {
        public bool HandleKeyDown;
        public int KeyDownCount;

        public override bool OnKeyDown(KeyEvent e)
        {
            if (base.OnKeyDown(e))
                return true;

            KeyDownCount++;
            return HandleKeyDown;
        }
    }
}
