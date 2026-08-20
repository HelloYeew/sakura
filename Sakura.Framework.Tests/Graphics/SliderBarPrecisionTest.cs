// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Numerics;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Logging;
using Sakura.Framework.Timing;
using Vector2 = Sakura.Framework.Maths.Vector2;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// A slider whose creator chose no precision still gets one
/// </summary>
[TestFixture]
public class SliderBarPrecisionTest
{
    private ManualClock manual = null!;
    private FramedClock rootClock = null!;
    private Container root = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        rootClock = new FramedClock(manual);
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = rootClock
        };
    }

    /// <summary>
    /// Adds a slider in the shape every existing call site uses: a range, a keyboard step, and no
    /// precision of its own.
    /// </summary>
    private BasicSliderBar<T> addSlider<T>(T min, T max)
        where T : struct, INumber<T>, IMinMaxValue<T>
    {
        var slider = new BasicSliderBar<T>
        {
            MinValue = min,
            MaxValue = max,
            Size = new Vector2(200, 20),
        };

        root.Add(slider);
        root.Load();
        root.CompleteLoad();

        return slider;
    }

    [Test]
    public void ADoubleSliderRoundsByDefault()
    {
        var slider = addSlider(0d, 1d);

        slider.Current.Value = 0.748274837d;

        Assert.That(slider.Current.Value, Is.EqualTo(0.75d).Within(1e-9d), "drift from a drag must not survive the assignment");
    }

    [Test]
    public void AFloatSliderRoundsByDefault()
    {
        var slider = addSlider(0f, 1f);

        slider.Current.Value = 0.748274837f;

        Assert.That(slider.Current.Value, Is.EqualTo(0.75f).Within(1e-6f));
    }

    [Test]
    public void TheDefaultIsTwoPlaces()
    {
        var slider = addSlider(0d, 1d);

        Assert.That(slider.DecimalPlaces, Is.EqualTo(SliderBar<double>.DEFAULT_DECIMAL_PLACES));
    }

    /// <summary>
    /// Rounding is away from zero, so the halfway case does not depend on the current value's parity.
    /// </summary>
    [Test]
    public void TheMidpointRoundsAwayFromZero()
    {
        var slider = addSlider(0d, 1d);

        slider.Current.Value = 0.125d;

        Assert.That(slider.Current.Value, Is.EqualTo(0.13d).Within(1e-9d));
    }

    /// <summary>
    /// The default must not make values outside the range reachable: rounding runs before the clamp.
    /// </summary>
    [Test]
    public void RoundingCannotEscapeTheRange()
    {
        var slider = addSlider(0d, 0.995d);

        slider.Current.Value = 0.994d;

        Assert.That(slider.Current.Value, Is.LessThanOrEqualTo(0.995d), "0.994 rounds up to 1.00, which is out of range");
    }

    /// <summary>
    /// Integer sliders are already whole numbers, so the default has to leave every one of their values
    /// reachable — this is what stops it quantizing an int scroll-speed slider.
    /// </summary>
    [Test]
    public void AnIntegerSliderIsUntouched()
    {
        var slider = addSlider(0, 1000);

        slider.Current.Value = 337;

        Assert.That(slider.Current.Value, Is.EqualTo(337));
    }

    /// <summary>
    /// The opt-out, for a normalized scrubber where a hundred positions over the whole range is too
    /// coarse.
    /// </summary>
    [Test]
    public void NullRestoresAContinuousSlider()
    {
        var slider = addSlider(0d, 1d);
        slider.DecimalPlaces = null;

        slider.Current.Value = 0.748274837d;

        Assert.That(slider.Current.Value, Is.EqualTo(0.748274837d).Within(1e-12d));
    }

    /// <summary>
    /// <see cref="SliderBar{T}.Step"/> stays off by default — it is an absolute grid, and a useful one
    /// depends on a range that is not known until after construction. The two mechanisms are independent.
    /// </summary>
    [Test]
    public void TheGridIsStillOffByDefault()
    {
        var slider = addSlider(0d, 1d);

        Assert.That(slider.Step, Is.EqualTo(double.Epsilon), "Step is ReactiveNumber's DefaultPrecision, i.e. no snapping");
    }
}
