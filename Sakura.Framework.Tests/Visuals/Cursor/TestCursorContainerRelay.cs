// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Cursor;

/// <summary>
/// <see cref="CursorContainer"/>'s contract with the cursor drawable it draws, and with the OS
/// cursor it stands in for.
/// </summary>
/// <remarks>
/// A plain <see cref="TestScene"/>, not the manual-input one: that harness installs a
/// <see cref="CursorContainer"/> of its own, which would hide the OS cursor before these tests got
/// the chance to watch the one under test do it.
/// </remarks>
public partial class TestCursorContainerRelay : TestScene
{
    [Resolved]
    private IWindow window { get; set; }

    private ProbeCursorContainer container;

    [SetUp]
    public void SetUp()
    {
        AddStep("Build", () =>
        {
            Clear();
            window.CursorVisible = true;
            window.CursorState.Value = CursorState.Default;

            Add(container = new ProbeCursorContainer());
        });

        AddUntilStep("Loaded", () => container.IsLoaded);
    }

    /// <summary>
    /// The relay used to be gated on the container's own private cursor type, so a cursor supplied
    /// through <c>CreateCursor()</c> was never told anything and silently stayed in one shape.
    /// </summary>
    [Test]
    public void TestACustomCursorIsToldAboutStateChanges()
    {
        AddAssert("Seeded with the current state on load", () => container.Probe.Current == CursorState.Default);

        AddStep("Ask for a pointer", () => window.CursorState.Value = CursorState.Pointer);
        AddAssert("The custom cursor heard it", () => container.Probe.Current == CursorState.Pointer);

        AddStep("And a crosshair", () => window.CursorState.Value = CursorState.Crosshair);
        AddAssert("Heard that too", () => container.Probe.Current == CursorState.Crosshair);
    }

    /// <summary>
    /// A software cursor replaces the hardware one. Nothing used to hide it, so adding a container
    /// drew two cursors: the container's own, and the OS arrow underneath.
    /// </summary>
    [Test]
    public void TestTheOsCursorIsHiddenWhileDrawingOurOwn()
    {
        AddAssert("OS cursor hidden", () => !window.CursorVisible);

        AddStep("Remove the container", () => Clear());
        AddUntilStep("OS cursor restored", () => window.CursorVisible);
    }

    [Test]
    public void TestHidingTheOsCursorCanBeDeclined()
    {
        AddStep("Rebuild opting out", () =>
        {
            Clear();
            window.CursorVisible = true;
            Add(container = new ProbeCursorContainer { HideOsCursor = false });
        });

        AddUntilStep("Loaded", () => container.IsLoaded);
        AddAssert("OS cursor left alone", () => window.CursorVisible);
    }

    /// <summary>
    /// <c>window.CursorState</c> lives as long as the application, so a subscription left behind
    /// accumulates one dead entry per container — and a test scene builds one per scene.
    /// </summary>
    [Test]
    public void TestTheSubscriptionIsReleasedWithTheContainer()
    {
        AddStep("Ask for a pointer", () => window.CursorState.Value = CursorState.Pointer);
        AddAssert("Heard", () => container.Probe.Current == CursorState.Pointer);

        var orphan = default(ProbeCursor);

        AddStep("Remove the container", () =>
        {
            orphan = container.Probe;
            Clear();
        });

        AddUntilStep("It is disposed", () => container.IsDisposed);

        AddStep("Change state with nothing listening", () => window.CursorState.Value = CursorState.Wait);

        AddAssert("The detached cursor was not called again", () => orphan.Current == CursorState.Pointer);
    }

    private partial class ProbeCursorContainer : CursorContainer
    {
        public ProbeCursor Probe { get; private set; } = null!;

        protected override Drawable CreateCursor() => Probe = new ProbeCursor();
    }

    /// <summary>
    /// A cursor supplied by an application, which is what <c>CreateCursor</c> is for.
    /// </summary>
    private partial class ProbeCursor : Container, ICursorDrawable
    {
        public CursorState Current { get; private set; } = CursorState.NotAllowed;

        public ProbeCursor()
        {
            Size = new Vector2(20);
            Add(new Box { RelativeSizeAxes = Axes.Both, Color = Color.Aquamarine });
        }

        public void ChangeCursor(CursorState state) => Current = state;
    }
}
