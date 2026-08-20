// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests that an invisible flow container does not lay out, and lays out correctly on the frame it
/// becomes visible again.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FlowContainer.PerformLayout"/> ran before <see cref="Drawable.Update"/>'s alpha early-out,
/// and laying out *pulls* every child's <c>Size</c>. <c>SpriteText</c> overrides that getter to shape its
/// text on demand, so a fully hidden flow container re-shaped every label it held, every frame. That is
/// why the hidden performance overlay was the single largest source of glyph-shaping allocation in an
/// idle session, and it would bite any hidden UI.
/// </para>
/// <para>
/// These count child <c>Size</c> reads rather than shaped-text counters: the read is the mechanism —
/// everything expensive hangs off it — and it is observable without a font store, which a headless
/// <c>SpriteText</c> has no working equivalent of.
/// </para>
/// </remarks>
[TestFixture]
public class InvisibleLayoutTest
{
    private ManualClock manual = null!;
    private Container root = null!;

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
    }

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    private void frames(int count)
    {
        for (int i = 0; i < count; i++)
            frame();
    }

    private (FlowContainer flow, SizeProbe probe) buildFlow()
    {
        var flow = new FlowContainer
        {
            Size = new Vector2(400, 300),
            Direction = FlowDirection.Horizontal
        };

        var probe = new SizeProbe { Size = new Vector2(50, 20) };
        flow.Add(probe);
        root.Add(flow);

        return (flow, probe);
    }

    [Test]
    public void AVisibleFlowContainerLaysOut()
    {
        var (_, probe) = buildFlow();

        frames(3);

        Assert.That(probe.SizeReads, Is.GreaterThan(0));
    }

    /// <summary>
    /// The acceptance case from the plan: hide it, pump frames, assert nothing was measured.
    /// </summary>
    [Test]
    public void AHiddenFlowContainerDoesNotLayOut()
    {
        var (flow, probe) = buildFlow();

        frames(3);
        flow.Hide();
        frames(3); // let the hide settle

        probe.SizeReads = 0;
        frames(100);

        Assert.That(probe.SizeReads, Is.Zero, "a hidden flow container must not measure its children");
    }

    /// <summary>
    /// The case the alpha check alone could not catch, and the one that actually happens: the container
    /// is visible but something above it is not. The performance overlay fades the *overlay* out; the
    /// flow containers inside it keep their own alpha at 1.
    /// </summary>
    [Test]
    public void AFlowContainerUnderAHiddenAncestorDoesNotLayOut()
    {
        var panel = new Container { Size = new Vector2(500, 400) };
        var flow = new FlowContainer { Size = new Vector2(400, 300) };
        var probe = new SizeProbe { Size = new Vector2(50, 20) };

        flow.Add(probe);
        panel.Add(flow);
        root.Add(panel);

        frames(3);
        panel.Hide();
        frames(3);

        probe.SizeReads = 0;
        frames(100);

        Assert.Multiple(() =>
        {
            Assert.That(flow.Alpha, Is.EqualTo(1), "the flow container itself is still visible");
            Assert.That(probe.SizeReads, Is.Zero, "but nothing below a hidden ancestor should be measured");
        });
    }

    /// <summary>
    /// Skipping is only correct if the work is deferred rather than dropped — a layout owed from while it
    /// was hidden has to happen on the first frame it is shown, not one frame later.
    /// </summary>
    [Test]
    public void LayoutRunsOnTheFirstVisibleFrame()
    {
        var (flow, probe) = buildFlow();

        frames(3);
        flow.Hide();
        frames(3);

        probe.SizeReads = 0;

        // A change made while hidden: layout is owed, and must not be forgotten.
        probe.Size = new Vector2(120, 40);
        frames(10);

        Assert.That(probe.SizeReads, Is.Zero, "still hidden, still not measured");

        probe.SizeReads = 0;
        flow.Show();
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(probe.SizeReads, Is.GreaterThan(0), "the owed layout must run on the frame it becomes visible");
            Assert.That(flow.Children, Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// <see cref="Drawable.AlwaysPresent"/> means "update me even when I am not on screen", and layout is
    /// part of updating — a screen mid-transition relies on it.
    /// </summary>
    [Test]
    public void AnAlwaysPresentFlowContainerStillLaysOutWhileHidden()
    {
        var (flow, probe) = buildFlow();
        flow.AlwaysPresent = true;

        frames(3);
        flow.Hide();
        frames(3);

        probe.SizeReads = 0;
        probe.Size = new Vector2(120, 40);
        frames(3);

        Assert.That(probe.SizeReads, Is.GreaterThan(0));
    }

    /// <summary>
    /// Counts how many times its size was pulled — which is what a layout pass does, and what makes a
    /// real <c>SpriteText</c> shape.
    /// </summary>
    private partial class SizeProbe : Drawable
    {
        public int SizeReads;

        public override Vector2 Size
        {
            get
            {
                SizeReads++;
                return base.Size;
            }
            set => base.Size = value;
        }
    }
}
