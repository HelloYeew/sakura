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

    private FocusBox addFocusable(bool acceptsFocus = true)
    {
        var box = new FocusBox { AcceptsFocusValue = acceptsFocus, Size = new Vector2(50) };
        root.Add(box);
        settle();
        return box;
    }

    /// <summary>
    /// A drawable that actively requests focus (a modal / focused overlay). Only these are suspended
    /// onto the focus stack for later restore -- see <see cref="InputManager.ChangeFocus"/>.
    /// </summary>
    private FocusBox addRequester()
    {
        var box = new FocusBox { AcceptsFocusValue = true, RequestsFocusValue = true, Size = new Vector2(50) };
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
    public void TestFocusTransferPushesPreviousRequesterOntoStack()
    {
        var first = addRequester();
        var second = addFocusable();

        manager.ChangeFocus(first);
        manager.ChangeFocus(second);

        Assert.Multiple(() =>
        {
            Assert.That(manager.FocusedDrawable, Is.SameAs(second));
            Assert.That(first.HasFocus, Is.False);
            Assert.That(second.HasFocus, Is.True);
            Assert.That(manager.FocusStack, Does.Contain(first), "A focus REQUESTER is suspended onto the stack when covered.");
        });
    }

    [Test]
    public void TestFocusTransferDoesNotStackPlainFocusable()
    {
        var first = addFocusable();
        var second = addFocusable();

        manager.ChangeFocus(first);
        manager.ChangeFocus(second);

        Assert.Multiple(() =>
        {
            Assert.That(manager.FocusedDrawable, Is.SameAs(second));
            Assert.That(first.HasFocus, Is.False);
            Assert.That(manager.FocusStack, Is.Empty, "Moving between two plain focusables leaves no suspended focus behind.");
        });
    }

    [Test]
    public void TestReleaseRestoresSuspendedRequesterFromStack()
    {
        var overlay = addRequester();
        var second = addFocusable();

        manager.ChangeFocus(overlay);
        manager.ChangeFocus(second);

        // Releasing the current focus restores the suspended requester (e.g. a dropdown closing
        // hands focus back to the still-open overlay underneath it).
        manager.ChangeFocus(null);

        Assert.Multiple(() =>
        {
            Assert.That(manager.FocusedDrawable, Is.SameAs(overlay), "Focus is restored to the suspended requester on release.");
            Assert.That(overlay.HasFocus, Is.True);
            Assert.That(manager.FocusStack, Does.Not.Contain(overlay));
        });
    }

    [Test]
    public void TestReleaseAfterLateralMoveClearsFocusEntirely()
    {
        var first = addFocusable();
        var second = addFocusable();

        // Click text box A, then text box B, then empty space. Focus must end up on NOTHING --
        // it must not bounce back to A.
        manager.ChangeFocus(first);
        manager.ChangeFocus(second);
        manager.ChangeFocus(null);

        Assert.Multiple(() =>
        {
            Assert.That(manager.FocusedDrawable, Is.Null, "Releasing focus after a lateral move leaves nothing focused.");
            Assert.That(first.HasFocus, Is.False);
            Assert.That(second.HasFocus, Is.False);
            Assert.That(first.FocusCount, Is.EqualTo(1), "The first drawable is never re-focused.");
            Assert.That(manager.FocusStack, Is.Empty);
        });
    }

    [Test]
    public void TestStackSkipsDeadDrawableOnRestore()
    {
        var first = addRequester();
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
