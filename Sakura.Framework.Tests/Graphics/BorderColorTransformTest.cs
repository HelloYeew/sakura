// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// <see cref="TransformExtensions.FadeBorderColorTo{T}"/>: a container's border animated by a transform
/// of its own, so it's never a frame behind the rest of the drawable, and it's right on the first frame
/// back from being masked away (where a color copied over in an update can't be: nothing updates
/// while masked away, so the border showed whatever it had last, the default white at first).
/// </summary>
[TestFixture]
public class BorderColorTransformTest
{
    private ManualClock manual = null!;
    private Container root = null!;
    private Container ring = null!;

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
            Size = new Vector2(400, 300),
            Masking = true,
            Clock = new FramedClock(manual)
        };

        root.Add(ring = new Container
        {
            Position = new Vector2(10),
            Size = new Vector2(40),
            Masking = true,
            CornerRadius = 20,
            BorderThickness = 2,
            BorderColor = Color.Black
        });

        root.Load();
        root.CompleteLoad();
        frame();
    }

    private void frame(double elapsed = 16)
    {
        manual.CurrentTime += elapsed;
        root.UpdateSubTree();
    }

    /// <summary>What the draw thread would draw for the ring now.</summary>
    private Color drawnBorder(int buffer = 0)
    {
        var rootNode = (ContainerDrawNode)root.GenerateDrawNodeSubtree(buffer);
        return ((ContainerDrawNode)rootNode.Children[0]).BorderColor;
    }

    [Test]
    public void TestApplyBlendsAndLeavesTheRestAlone()
    {
        var transform = new BorderColorTransform
        {
            StartTime = 0,
            EndTime = 100,
            EndValue = Color.White
        };

        transform.Apply(ring, 50);
        Assert.That(ring.BorderColor.R, Is.InRange(120, 135), "Halfway, halfway between.");

        transform.Apply(ring, 100);

        Assert.Multiple(() =>
        {
            Assert.That(ring.BorderColor, Is.EqualTo(Color.FromArgb(255, 255, 255, 255)));
            Assert.That(ring.BorderThickness, Is.EqualTo(2));
            Assert.That(ring.Color, Is.EqualTo(Color.White), "The container's own color is another property.");
        });
    }

    [Test]
    public void TestFadesOverItsDuration()
    {
        ring.FadeBorderColorTo(Color.Red, 100);

        frame(50);
        Assert.That(ring.BorderColor.R, Is.InRange(1, 254), "On its way.");

        frame(60);
        Assert.That(ring.BorderColor, Is.EqualTo(Color.FromArgb(255, 255, 0, 0)), "There once its time is up.");
    }

    [Test]
    public void TestRetargetKeepsTheTimeline()
    {
        ring.FadeBorderColorTo(Color.Red, 100);
        frame(50);

        // A second fade mid-way redirects the first rather than starting over.
        ring.FadeBorderColorTo(Color.Blue, 100);
        frame(60);

        Assert.That(ring.BorderColor, Is.EqualTo(Color.FromArgb(255, 0, 0, 255)), "The new color, by the first fade's end.");
    }

    [Test]
    public void TestInstantSetIsDrawnOnTheFirstFrame()
    {
        var fresh = new Container
        {
            Size = new Vector2(40),
            Masking = true,
            BorderThickness = 2
        };

        // Given before it's even loaded, as a constructor would.
        fresh.FadeBorderColorTo(Color.Red);
        root.Clear();
        root.Add(fresh);
        frame();

        Assert.That(drawnBorder(), Is.EqualTo(Color.FromArgb(255, 255, 0, 0)), "No frame of the default white.");
    }

    [Test]
    public void TestRecolouredWhileMaskedAwayIsRightOnTheFirstFrameBack()
    {
        ring.Y = 1000;
        frame();
        Assert.That(ring.IsMaskedAway, Is.True);

        ring.FadeBorderColorTo(Color.Red, 250, Easing.OutQuint);
        frame(500);
        frame(500);

        ring.Y = 10;
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(ring.IsMaskedAway, Is.False);
            Assert.That(drawnBorder(), Is.EqualTo(Color.FromArgb(255, 255, 0, 0)), "Drawn in the new color on the first frame back.");
        });
    }
}
