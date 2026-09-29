// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// The selection fill must end up at the value it was told to show, including when frames are much
/// longer than the fill animation, which is the norm in an app that does heavy work on each change.
/// </summary>
[TestFixture]
public class SliderBarFillAnimationTest
{
    private ManualClock manual = null!;
    private Container root = null!;
    private BasicSliderBar<double> slider = null!;

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

        root.Add(slider = new BasicSliderBar<double>
        {
            Size = new Vector2(200, 20),
            MinValue = 0,
            MaxValue = 100
        });

        root.Load();
        root.CompleteLoad();
        frame(0);
    }

    private void frame(double elapsed)
    {
        manual.CurrentTime += elapsed;
        root.UpdateSubTree();
    }

    [Test]
    public void TestFillReachesTargetAtNormalFrameRate()
    {
        slider.Current.Value = 50;

        // 60fps-ish, well past the 150ms animation.
        for (int i = 0; i < 20; i++)
            frame(16);

        Assert.That(slider.CurrentFillWidth, Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public void TestFillReachesTargetWhenAFrameOutlastsTheAnimation()
    {
        slider.Current.Value = 50;

        // One long hitch, longer than FillAnimationDuration (150ms).
        frame(600);

        Assert.That(slider.CurrentFillWidth, Is.EqualTo(0.5f).Within(0.01f),
            "A frame longer than the animation must still land on the final value.");
    }

    [Test]
    public void TestFillTracksValueDuringALaggyDrag()
    {
        // Mimics dragging in an app that stalls on every change: each value change is followed by a
        // frame far longer than the fill animation. The fill must follow the value, not freeze.
        double[] dragValues = { 10, 25, 40, 55, 70, 85 };

        foreach (double value in dragValues)
        {
            slider.Current.Value = value;
            frame(500);
        }

        Assert.That(slider.CurrentFillWidth, Is.EqualTo(0.85f).Within(0.01f),
            "The fill must track the value through a stalling drag.");
    }

    /// <summary>
    /// <see cref="SliderBar{T}.Step"/> snaps Current onto a precision grid, and that snapping leaves
    /// float residue (0.7 is stored as 7 * 0.1 = 0.7000000000000001). Rounding to DecimalPlaces then
    /// produces a *different* number, so the slider re-assigns Current -- but the re-assignment snaps
    /// straight back to the stored value, Reactive dedupes it, and no change event fires. The fill
    /// must still end up showing the value.
    /// </summary>
    [Test]
    public void TestFillTracksValueOnAStepGrid([Values(0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0)] double target)
    {
        var stepped = new BasicSliderBar<double>
        {
            Size = new Vector2(200, 20),
            MinValue = 0,
            MaxValue = 1,
            Step = 0.1,
            DecimalPlaces = 1
        };

        root.Add(stepped);
        root.CompleteLoad();
        frame(0);

        stepped.Current.Value = target;

        for (int i = 0; i < 20; i++)
            frame(16);

        Assert.That(stepped.CurrentFillWidth, Is.EqualTo((float)target).Within(0.01f),
            $"The fill must show {target}, not whatever it was before.");
    }

    [Test]
    public void TestFillFollowsEveryStepOfALaggyDrag()
    {
        double[] dragValues = { 20, 40, 60, 80 };

        foreach (double value in dragValues)
        {
            slider.Current.Value = value;
            frame(500);

            Assert.That(slider.CurrentFillWidth, Is.EqualTo((float)(value / 100)).Within(0.01f),
                $"The fill should be showing {value} after the frame that set it.");
        }
    }
}
