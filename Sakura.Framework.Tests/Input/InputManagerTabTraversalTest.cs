// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Input;

[TestFixture]
public class InputManagerTabTraversalTest
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
        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };
        root.Load();
        root.CompleteLoad();

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

    /// <summary>
    /// Adds tab stops to <paramref name="parent"/> (the root by default) and settles.
    /// </summary>
    private StopBox[] addStops(int count, Container? parent = null)
    {
        var stops = new StopBox[count];

        for (int i = 0; i < count; i++)
        {
            stops[i] = new StopBox { Size = new Vector2(50) };
            (parent ?? root).Add(stops[i]);
        }

        settle();
        return stops;
    }

    private void buildQueues() => manager.BuildQueues(root, Vector2.Zero);

    [Test]
    public void TestTabFromNothingFocusedEntersFirstStop()
    {
        var stops = addStops(3);
        buildQueues();

        Assert.That(manager.MoveFocusToNextTabStop(), Is.True);
        Assert.That(manager.FocusedDrawable, Is.SameAs(stops[0]));
    }

    [Test]
    public void TestShiftTabFromNothingFocusedEntersLastStop()
    {
        var stops = addStops(3);
        buildQueues();

        Assert.That(manager.MoveFocusToNextTabStop(reverse: true), Is.True);
        Assert.That(manager.FocusedDrawable, Is.SameAs(stops[2]));
    }

    [Test]
    public void TestTabAdvancesInDocumentOrder()
    {
        var stops = addStops(3);
        buildQueues();
        manager.ChangeFocus(stops[0]);

        manager.MoveFocusToNextTabStop();
        Assert.That(manager.FocusedDrawable, Is.SameAs(stops[1]));

        manager.MoveFocusToNextTabStop();
        Assert.That(manager.FocusedDrawable, Is.SameAs(stops[2]));
    }

    [Test]
    public void TestTabWrapsAtEnd()
    {
        var stops = addStops(3);
        buildQueues();
        manager.ChangeFocus(stops[2]);

        manager.MoveFocusToNextTabStop();
        Assert.That(manager.FocusedDrawable, Is.SameAs(stops[0]), "Tab wraps from the last stop to the first.");
    }

    [Test]
    public void TestShiftTabWrapsAtStart()
    {
        var stops = addStops(3);
        buildQueues();
        manager.ChangeFocus(stops[0]);

        manager.MoveFocusToNextTabStop(reverse: true);
        Assert.That(manager.FocusedDrawable, Is.SameAs(stops[2]), "Shift+Tab wraps from the first stop to the last.");
    }

    [Test]
    public void TestDisabledStopIsSkipped()
    {
        var stops = addStops(3);
        stops[1].CanBeTabbedToValue = false;
        buildQueues();
        manager.ChangeFocus(stops[0]);

        manager.MoveFocusToNextTabStop();
        Assert.That(manager.FocusedDrawable, Is.SameAs(stops[2]), "A stop that opts out is skipped.");
    }

    [Test]
    public void TestHiddenStopIsSkipped()
    {
        var stops = addStops(3);
        stops[1].Alpha = 0f;
        settle();
        buildQueues();
        manager.ChangeFocus(stops[0]);

        manager.MoveFocusToNextTabStop();
        Assert.That(manager.FocusedDrawable, Is.SameAs(stops[2]), "An invisible stop is skipped.");
    }

    [Test]
    public void TestNonTabStopFocusableIsSkipped()
    {
        var first = addStops(1)[0];

        // Accepts focus (so a click could focus it) but never opts into the tab order.
        var clickOnly = new FocusOnlyBox { Size = new Vector2(50) };
        root.Add(clickOnly);
        var last = new StopBox { Size = new Vector2(50) };
        root.Add(last);
        settle();
        buildQueues();

        manager.ChangeFocus(first);
        manager.MoveFocusToNextTabStop();

        Assert.That(manager.FocusedDrawable, Is.SameAs(last), "AcceptsFocus alone does not put a drawable in the tab order.");
    }

    [Test]
    public void TestTabOrderOverridesDocumentOrder()
    {
        var stops = addStops(3);
        stops[2].TabOrderValue = -1; // pull the last one to the front
        buildQueues();

        manager.MoveFocusToNextTabStop();
        Assert.That(manager.FocusedDrawable, Is.SameAs(stops[2]));

        manager.MoveFocusToNextTabStop();
        Assert.That(manager.FocusedDrawable, Is.SameAs(stops[0]), "Ties fall back to document order.");
    }

    [Test]
    public void TestNestedStopsFollowDocumentOrder()
    {
        var outerFirst = addStops(1)[0];

        var group = new Container { Size = new Vector2(200) };
        root.Add(group);
        var inner = addStops(2, group);

        var outerLast = new StopBox { Size = new Vector2(50) };
        root.Add(outerLast);
        settle();
        buildQueues();

        manager.ChangeFocus(outerFirst);

        manager.MoveFocusToNextTabStop();
        Assert.That(manager.FocusedDrawable, Is.SameAs(inner[0]), "Traversal descends into a nested container.");

        manager.MoveFocusToNextTabStop();
        Assert.That(manager.FocusedDrawable, Is.SameAs(inner[1]));

        manager.MoveFocusToNextTabStop();
        Assert.That(manager.FocusedDrawable, Is.SameAs(outerLast), "And comes back out again.");
    }

    [Test]
    public void TestTrappingScopeConfinesTraversal()
    {
        var outside = addStops(2);

        var modal = new ScopeBox { Size = new Vector2(200), TrapsValue = true };
        root.Add(modal);
        var inside = addStops(2, modal);
        settle();
        buildQueues();

        manager.ChangeFocus(inside[0]);

        manager.MoveFocusToNextTabStop();
        Assert.That(manager.FocusedDrawable, Is.SameAs(inside[1]));

        // Wraps within the modal rather than escaping to the controls behind it.
        manager.MoveFocusToNextTabStop();
        Assert.Multiple(() =>
        {
            Assert.That(manager.FocusedDrawable, Is.SameAs(inside[0]), "Tab wraps inside the trapping scope.");
            Assert.That(manager.FocusedDrawable, Is.Not.SameAs(outside[0]));
        });
    }

    [Test]
    public void TestTabEntersTrappingScopeWhenNothingFocused()
    {
        addStops(2);

        var modal = new ScopeBox { Size = new Vector2(200), TrapsValue = true };
        root.Add(modal);
        var inside = addStops(1, modal);
        settle();
        buildQueues();

        manager.MoveFocusToNextTabStop();
        Assert.That(manager.FocusedDrawable, Is.SameAs(inside[0]), "Tab enters an open modal rather than the controls behind it.");
    }

    [Test]
    public void TestScopeStopsTrappingWhenInactive()
    {
        var outside = addStops(1);

        var modal = new ScopeBox { Size = new Vector2(200), TrapsValue = false };
        root.Add(modal);
        var inside = addStops(1, modal);
        settle();
        buildQueues();

        manager.ChangeFocus(inside[0]);
        manager.MoveFocusToNextTabStop();

        Assert.That(manager.FocusedDrawable, Is.SameAs(outside[0]), "A scope that is not trapping does not confine traversal.");
    }

    [Test]
    public void TestNoStopsLeavesFocusAlone()
    {
        buildQueues();

        Assert.Multiple(() =>
        {
            Assert.That(manager.MoveFocusToNextTabStop(), Is.False);
            Assert.That(manager.FocusedDrawable, Is.Null);
        });
    }

    [Test]
    public void TestSingleStopStaysPut()
    {
        var only = addStops(1)[0];
        buildQueues();
        manager.ChangeFocus(only);

        Assert.Multiple(() =>
        {
            Assert.That(manager.MoveFocusToNextTabStop(), Is.True);
            Assert.That(manager.FocusedDrawable, Is.SameAs(only), "A lone stop wraps back onto itself.");
        });
    }

    [Test]
    public void TestTabKeyDispatchMovesFocus()
    {
        var stops = addStops(2);
        buildQueues();
        manager.ChangeFocus(stops[0]);

        bool handled = manager.DispatchKeyDown(new KeyEvent(Key.Tab, KeyModifiers.None, false));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True, "Tab is consumed as navigation when nothing else claims it.");
            Assert.That(manager.FocusedDrawable, Is.SameAs(stops[1]));
        });
    }

    [Test]
    public void TestShiftTabKeyDispatchMovesBackwards()
    {
        var stops = addStops(2);
        buildQueues();
        manager.ChangeFocus(stops[1]);

        manager.DispatchKeyDown(new KeyEvent(Key.Tab, KeyModifiers.Shift, false));

        Assert.That(manager.FocusedDrawable, Is.SameAs(stops[0]));
    }

    [Test]
    public void TestControlHandlingTabKeepsIt()
    {
        var stops = addStops(2);
        stops[0].SwallowTab = true;
        buildQueues();
        manager.ChangeFocus(stops[0]);

        bool handled = manager.DispatchKeyDown(new KeyEvent(Key.Tab, KeyModifiers.None, false));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(handled, Is.True);
            Assert.That(manager.FocusedDrawable, Is.SameAs(stops[0]), "A control that consumes Tab keeps focus.");
        }
    }

    private partial class StopBox : Box, ITabStop
    {
        public bool CanBeTabbedToValue = true;
        public int TabOrderValue;

        /// <summary>Set to have this box consume Tab itself, as a text editor would.</summary>
        public bool SwallowTab;

        public override bool AcceptsFocus => true;
        public bool CanBeTabbedTo => CanBeTabbedToValue;
        public int TabOrder => TabOrderValue;

        public override bool OnKeyDown(KeyEvent e) => SwallowTab && HasFocus && e.Key == Key.Tab;
    }

    /// <summary>
    /// Focusable by click, but deliberately not an <see cref="ITabStop"/>.
    /// </summary>
    private partial class FocusOnlyBox : Box
    {
        public override bool AcceptsFocus => true;
    }

    private partial class ScopeBox : Container, ITabStopScope
    {
        public bool TrapsValue;
        public bool TrapsTabTraversal => TrapsValue;
    }
}
