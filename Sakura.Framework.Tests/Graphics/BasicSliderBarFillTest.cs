// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Regression test on the bug that slider bar transformation got stuck when update clock got stutter.
/// </summary>
[TestFixture]
public class BasicSliderBarFillTest
{
    private ManualClock manual = null!;
    private FramedClock rootClock = null!;
    private Container root = null!;
    private BasicSliderBar<float> slider = null!;

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
        rootClock = new FramedClock(manual);
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = rootClock
        };
        slider = new BasicSliderBar<float>
        {
            MinValue = 0f,
            MaxValue = 1f,
            Size = new Vector2(200, 20),
        };
        root.Add(slider);

        root.Load();
        root.LoadComplete();

        // Settle past the initial fill animation (LoadComplete fires one ResizeTo over
        // FillAnimationDuration) so it can't clobber a later instant set.
        for (int i = 0; i < 14; i++)
            frame(null);
    }

    private void frame(Action? atFrameStart, double advance = 16)
    {
        manual.CurrentTime += advance;
        rootClock.ProcessFrame();
        atFrameStart?.Invoke();
        root.UpdateSubTree();
    }

    [Test]
    public void TestDragEveryFrameDoesNotFreezeFill()
    {
        float[] widths = new float[10];
        for (int i = 0; i < 10; i++)
        {
            float value = (i + 1) / 10f;
            frame(() => slider.Current.Value = value);
            widths[i] = slider.CurrentFillWidth;
        }

        Assert.Multiple(() =>
        {
            for (int i = 1; i < widths.Length; i++)
                Assert.That(widths[i], Is.GreaterThanOrEqualTo(widths[i - 1] - 0.001f), $"fill regressed at frame {i}: {string.Join(",", widths)}");

            Assert.That(widths[^1], Is.GreaterThan(0.2f), $"fill froze during drag: {string.Join(",", widths)}");
        });
    }

    [Test]
    public void TestFillSettlesOnValue()
    {
        frame(() => slider.Current.Value = 0.8f);

        // let the animation (150ms default) finish
        for (int i = 0; i < 20; i++) frame(null, 20);

        Assert.That(slider.CurrentFillWidth, Is.EqualTo(0.8f).Within(0.001f), "fill must settle exactly on the value");
    }

    [Test]
    public void TestZeroDurationSnaps()
    {
        slider.FillAnimationDuration = 0;
        frame(() => slider.Current.Value = 0.6f);

        Assert.That(slider.CurrentFillWidth, Is.EqualTo(0.6f).Within(0.001f), "with no animation window the fill snaps immediately");
    }
}
