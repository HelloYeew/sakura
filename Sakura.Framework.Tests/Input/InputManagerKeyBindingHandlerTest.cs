// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Input;

[TestFixture]
public class InputManagerKeyBindingHandlerTest
{
    private enum TestAction
    {
        A,
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
    public void TestCollectsHandlersFrontToBack()
    {
        // Higher Depth draws on top (front-most). It must be collected first.
        var back = new HandlerBox { Depth = -10 };
        var front = new HandlerBox { Depth = 10 };
        root.Add(back);
        root.Add(front);
        settle();

        var handlers = manager.CollectKeyBindingHandlers<TestAction>(root);

        Assert.Multiple(() =>
        {
            Assert.That(handlers, Has.Count.EqualTo(2));
            Assert.That(handlers[0], Is.SameAs(front), "Front-most (higher depth) handler comes first.");
            Assert.That(handlers[1], Is.SameAs(back));
        });
    }

    [Test]
    public void TestCollectsNestedHandlers()
    {
        var outer = new HandlerContainer();
        var inner = new HandlerBox();
        outer.Add(inner);
        root.Add(outer);
        settle();

        var handlers = manager.CollectKeyBindingHandlers<TestAction>(root);

        Assert.Multiple(() =>
        {
            Assert.That(handlers, Does.Contain(outer));
            Assert.That(handlers, Does.Contain(inner));
        });
    }

    [Test]
    public void TestNonPositionalOptOutExcludesSubtree()
    {
        var optedOut = new OptOutContainer();
        var child = new HandlerBox();
        optedOut.Add(child);
        root.Add(optedOut);
        settle();

        var handlers = manager.CollectKeyBindingHandlers<TestAction>(root);

        Assert.That(handlers, Is.Empty, "A HandleNonPositionalInput=false subtree contributes no handlers.");
    }

    private partial class HandlerBox : Box, IKeyBindingHandler<TestAction>
    {
        public bool OnPressed(KeyBindingPressEvent<TestAction> e) => true;
        public void OnReleased(KeyBindingReleaseEvent<TestAction> e) { }
    }

    private partial class HandlerContainer : Container, IKeyBindingHandler<TestAction>
    {
        public bool OnPressed(KeyBindingPressEvent<TestAction> e) => true;
        public void OnReleased(KeyBindingReleaseEvent<TestAction> e) { }
    }

    private partial class OptOutContainer : Container
    {
        public override bool HandleNonPositionalInput => false;
    }
}
