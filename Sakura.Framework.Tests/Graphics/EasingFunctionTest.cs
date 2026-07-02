// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Transforms;

namespace Sakura.Framework.Tests.Graphics;

[TestFixture]
public class EasingFunctionTest
{
    private const double tolerance = 1e-6;

    [Test]
    public void TestDefaultIsLinear()
    {
        EasingFunction easing = default;
        Assert.That(easing.Apply(0), Is.EqualTo(0).Within(tolerance));
        Assert.That(easing.Apply(0.5), Is.EqualTo(0.5).Within(tolerance));
        Assert.That(easing.Apply(1), Is.EqualTo(1).Within(tolerance));
    }

    [Test]
    public void TestImplicitConversionFromEasing()
    {
        EasingFunction easing = Easing.OutQuad;
        // OutQuad: t * (2 - t)
        Assert.That(easing.Apply(0.5), Is.EqualTo(0.75).Within(tolerance));
        Assert.That(easing.Apply(0), Is.EqualTo(0).Within(tolerance));
        Assert.That(easing.Apply(1), Is.EqualTo(1).Within(tolerance));
    }

    [Test]
    public void TestCubicBezierEndpoints()
    {
        var easing = EasingFunction.CubicBezier(0.42, 0, 0.58, 1);
        Assert.That(easing.Apply(0), Is.EqualTo(0).Within(tolerance));
        Assert.That(easing.Apply(1), Is.EqualTo(1).Within(tolerance));
    }

    [Test]
    public void TestCubicBezierLinearWhenControlPointsOnDiagonal()
    {
        // Control points on the y=x diagonal reproduce the linear curve.
        var easing = EasingFunction.CubicBezier(0.25, 0.25, 0.75, 0.75);

        for (double t = 0; t <= 1.0; t += 0.1)
            Assert.That(easing.Apply(t), Is.EqualTo(t).Within(1e-4), $"at t={t}");
    }

    [Test]
    public void TestCubicBezierIsMonotonicForStandardEase()
    {
        var easing = EasingFunction.CubicBezier(0.25, 0.1, 0.25, 1.0); // CSS "ease"
        double previous = double.NegativeInfinity;

        for (double t = 0; t <= 1.0; t += 0.05)
        {
            double value = easing.Apply(t);
            Assert.That(value, Is.GreaterThanOrEqualTo(previous - 1e-9), $"non-monotonic at t={t}");
            previous = value;
        }
    }

    [Test]
    public void TestSpringEndpointsPinned()
    {
        var easing = EasingFunction.Spring();
        Assert.That(easing.Apply(0), Is.EqualTo(0).Within(tolerance));
        Assert.That(easing.Apply(1), Is.EqualTo(1).Within(tolerance));
    }

    [Test]
    public void TestSpringUnderdampedOvershoots()
    {
        var easing = EasingFunction.Spring(dampingRatio: 0.3, frequency: 1.5);
        bool overshot = false;

        for (double t = 0; t < 1.0; t += 0.01)
        {
            if (easing.Apply(t) > 1.0 + 1e-3)
            {
                overshot = true;
                break;
            }
        }

        Assert.That(overshot, Is.True, "under-damped spring should overshoot its target");
    }

    [Test]
    public void TestSpringCriticallyDampedDoesNotOvershoot()
    {
        var easing = EasingFunction.Spring(dampingRatio: 1.0, frequency: 1.5);

        for (double t = 0; t <= 1.0; t += 0.01)
            Assert.That(easing.Apply(t), Is.LessThanOrEqualTo(1.0 + 1e-6), $"overshoot at t={t}");
    }

    [Test]
    public void TestSpringSettlesNearTargetBeforeEnd()
    {
        var easing = EasingFunction.Spring(dampingRatio: 0.5, frequency: 1.5);
        // With this decay the value should be within a couple percent of the target well before the end.
        Assert.That(easing.Apply(0.95), Is.EqualTo(1.0).Within(0.05));
    }
}
