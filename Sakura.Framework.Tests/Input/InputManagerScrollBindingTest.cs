// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Input;

[TestFixture]
public class InputManagerScrollBindingTest
{
    private enum TestAction
    {
        VolumeUp,
    }

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
        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container
        {
            Size = new Vector2(400),
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

    [Test]
    public void TestScrollDispatchReachesKeyBindingContainer()
    {
        // A provider root so descendants resolve GetContainingInputManager() to `manager`, exactly as
        // ManualInputManager does for the test subtree.
        var providerRoot = new ProviderContainer(manager) { RelativeSizeAxes = Axes.Both, Size = new Vector2(1) };
        var container = new BindingContainer { RelativeSizeAxes = Axes.Both, Size = new Vector2(1) };
        var handler = new RecordingHandler { RelativeSizeAxes = Axes.Both, Size = new Vector2(1) };
        container.Add(handler);
        providerRoot.Add(container);
        root.Add(providerRoot);
        settle();

        manager.BuildQueues(root, new Vector2(200, 200));
        bool handled = manager.DispatchScroll(new ScrollEvent(new MouseState { Position = new Vector2(200, 200) }, new Vector2(0, 1)));

        Assert.Multiple(() =>
        {
            Assert.That(container.ScrollReceived, Is.True, "The KeyBindingContainer received the scroll via the positional queue.");
            Assert.That(handler.Pressed, Does.Contain(TestAction.VolumeUp), "The bound action fired through the manager-resolved handler.");
            Assert.That(handled, Is.True);
        });
    }

    [Test]
    public void TestScrollDispatchReachesContainerBehindFullScreenSibling()
    {
        // Mirror the visual scene: the provider root holds the binding subtree and a full-screen,
        // front-most sibling (like the test scene's CursorContainer). Build queues + dispatch exactly
        // as ManualInputManager.ScrollBy does, using the container's own DrawRectangle centre.
        var providerRoot = new ProviderContainer(manager) { RelativeSizeAxes = Axes.Both, Size = new Vector2(1) };
        var container = new BindingContainer { RelativeSizeAxes = Axes.Both, Size = new Vector2(1) };
        var handler = new RecordingHandler { RelativeSizeAxes = Axes.Both, Size = new Vector2(1) };
        container.Add(handler);
        providerRoot.Add(container);

        // Front-most full-screen sibling that does not consume scroll.
        providerRoot.Add(new Container { RelativeSizeAxes = Axes.Both, Size = new Vector2(1), Depth = float.MaxValue });

        root.Add(providerRoot);
        settle();

        var rect = container.DrawRectangle;
        var centre = new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);

        manager.HandleScroll(centre);
        manager.BuildQueues(providerRoot, centre);
        manager.DispatchScroll(new ScrollEvent(new MouseState { Position = centre }, new Vector2(0, 1)));

        Assert.Multiple(() =>
        {
            Assert.That(container.ScrollReceived, Is.True, "Scroll reaches the binding container behind a full-screen sibling.");
            Assert.That(handler.Pressed, Does.Contain(TestAction.VolumeUp));
        });
    }

    private partial class ProviderContainer : Container, IInputManagerProvider
    {
        public ProviderContainer(InputManager manager) => InputManager = manager;
        public InputManager InputManager { get; }
    }

    private partial class BindingContainer : KeyBindingContainer<TestAction>
    {
        public bool ScrollReceived;

        public override IEnumerable<KeyBinding> DefaultKeyBindings => new[]
        {
            new KeyBinding(InputKey.MouseWheelUp, TestAction.VolumeUp),
        };

        public override bool OnScroll(ScrollEvent e)
        {
            ScrollReceived = true;
            return base.OnScroll(e);
        }
    }

    private partial class RecordingHandler : Container, IKeyBindingHandler<TestAction>
    {
        public readonly List<TestAction> Pressed = new List<TestAction>();

        public bool OnPressed(KeyBindingPressEvent<TestAction> e)
        {
            Pressed.Add(e.Action);
            return true;
        }

        public void OnReleased(KeyBindingReleaseEvent<TestAction> e) { }
    }
}
