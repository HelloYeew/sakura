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
public class InputManagerFocusTest
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

    private FocusBox addFocusable(bool acceptsFocus = true)
    {
        var box = new FocusBox { AcceptsFocusValue = acceptsFocus, Size = new Vector2(50) };
        root.Add(box);
        settle();
        return box;
    }

    [Test]
    public void TestChangeFocusAcquiresAndNotifies()
    {
        var box = addFocusable();

        bool result = manager.ChangeFocus(box);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(manager.FocusedDrawable, Is.SameAs(box));
            Assert.That(box.HasFocus, Is.True);
            Assert.That(box.FocusCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestChangeFocusToNullReleases()
    {
        var box = addFocusable();
        manager.ChangeFocus(box);

        bool result = manager.ChangeFocus(null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(manager.FocusedDrawable, Is.Null);
            Assert.That(box.HasFocus, Is.False);
            Assert.That(box.FocusLostCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestNonAcceptingDrawableCannotTakeFocus()
    {
        var box = addFocusable(acceptsFocus: false);

        bool result = manager.ChangeFocus(box);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False, "A drawable that does not accept focus is rejected.");
            Assert.That(manager.FocusedDrawable, Is.Null);
            Assert.That(box.HasFocus, Is.False);
        });
    }

    [Test]
    public void TestFocusTransferPushesPreviousOntoStack()
    {
        var first = addFocusable();
        var second = addFocusable();

        manager.ChangeFocus(first);
        manager.ChangeFocus(second);

        Assert.Multiple(() =>
        {
            Assert.That(manager.FocusedDrawable, Is.SameAs(second));
            Assert.That(first.HasFocus, Is.False);
            Assert.That(second.HasFocus, Is.True);
            Assert.That(manager.FocusStack, Does.Contain(first), "The previously focused drawable is pushed onto the stack.");
        });
    }

    [Test]
    public void TestReleaseRestoresPreviousFromStack()
    {
        var first = addFocusable();
        var second = addFocusable();

        manager.ChangeFocus(first);
        manager.ChangeFocus(second);

        // Releasing the current focus restores the previous holder from the stack.
        manager.ChangeFocus(null);

        Assert.Multiple(() =>
        {
            Assert.That(manager.FocusedDrawable, Is.SameAs(first), "Focus is restored to the previous drawable on release.");
            Assert.That(first.HasFocus, Is.True);
            Assert.That(manager.FocusStack, Does.Not.Contain(first));
        });
    }

    [Test]
    public void TestStackSkipsDeadDrawableOnRestore()
    {
        var first = addFocusable();
        var second = addFocusable();

        manager.ChangeFocus(first);
        manager.ChangeFocus(second);

        // The stacked drawable expires before focus is released; restore should skip it.
        first.Expire();
        settle();

        manager.ChangeFocus(null);

        Assert.Multiple(() =>
        {
            Assert.That(manager.FocusedDrawable, Is.Null, "A dead drawable is not restored from the stack.");
            Assert.That(manager.FocusStack, Is.Empty);
        });
    }

    [Test]
    public void TestReFocusingSameDrawableMarksClaimedByClick()
    {
        var box = addFocusable();
        manager.ChangeFocus(box);

        manager.BeginMouseDownFocusTracking();
        Assert.That(manager.WasFocusClaimedByLastClick, Is.False, "Tracking resets on mouse-down.");

        // Re-focusing the already-focused drawable should still count as a claim.
        manager.ChangeFocus(box);
        Assert.That(manager.WasFocusClaimedByLastClick, Is.True);
    }

    [Test]
    public void TestClaimTrackingAcrossClick()
    {
        var box = addFocusable();

        manager.BeginMouseDownFocusTracking();
        Assert.That(manager.WasFocusClaimedByLastClick, Is.False);

        manager.ChangeFocus(box);
        Assert.That(manager.WasFocusClaimedByLastClick, Is.True, "Acquiring focus during a click marks it claimed.");
    }

    [Test]
    public void TestTriggerFocusContentionFocusesRequestingDrawable()
    {
        var requester = new FocusBox { AcceptsFocusValue = true, RequestsFocusValue = true, Size = new Vector2(50) };
        root.Add(requester);
        settle();

        manager.TriggerFocusContention(requester);

        Assert.That(manager.FocusedDrawable, Is.SameAs(requester), "A drawable that requests focus gains it on contention.");
    }

    [Test]
    public void TestTriggerFocusContentionIgnoresNonRequester()
    {
        var box = addFocusable();

        manager.TriggerFocusContention(box);

        Assert.That(manager.FocusedDrawable, Is.Null, "A drawable that does not request focus is not focused on contention.");
    }

    private partial class FocusBox : Box
    {
        public bool AcceptsFocusValue;
        public bool RequestsFocusValue;
        public int FocusCount;
        public int FocusLostCount;

        public override bool AcceptsFocus => AcceptsFocusValue;
        public override bool RequestsFocus => RequestsFocusValue;

        public override void OnFocus(FocusEvent e) => FocusCount++;
        public override void OnFocusLost(FocusLostEvent e) => FocusLostCount++;
    }
}
