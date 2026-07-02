// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Transforms;

/// <summary>
/// A physically-based spring easing modelled as the step response of a damped harmonic oscillator.
/// The value starts at 0, is pulled toward its target of 1, and — depending on the damping —
/// may overshoot and oscillate before settling.
/// </summary>
/// <remarks>
/// The spring's behaviour is controlled by two parameters:
/// <list type="bullet">
/// <item><description>
/// <c>dampingRatio</c> (ζ): below 1 the spring is under-damped and bounces past the target;
/// exactly 1 is critically damped (the fastest settle with no overshoot); above 1 is over-damped
/// (a slow approach with no overshoot).
/// </description></item>
/// <item><description>
/// <c>frequency</c>: the natural frequency of the spring expressed as the number of oscillations
/// that a completely undamped spring would perform across the transform's whole duration. Higher
/// values feel stiffer and settle faster.
/// </description></item>
/// </list>
/// The curve is evaluated over normalised progress <c>t ∈ [0, 1]</c>, where <c>t = 1</c> corresponds
/// to the end of the transform. The endpoints are pinned so the transform always comes to rest
/// exactly on its target value.
/// </remarks>
public sealed class SpringEasing : IEasingFunction
{
    /// <summary>
    /// A gently bouncy default that overshoots once before settling.
    /// </summary>
    public const double DEFAULT_DAMPING_RATIO = 0.5;

    /// <summary>
    /// A default natural frequency of 1.5 oscillations across the duration.
    /// </summary>
    public const double DEFAULT_FREQUENCY = 1.5;

    private readonly double dampingRatio;

    /// <summary>
    /// Natural angular frequency (rad over the normalised [0, 1] duration)
    /// </summary>
    private readonly double omega0;

    /// <param name="dampingRatio">
    /// The damping ratio ζ. Clamped to a small positive minimum. Values in (0, 1) bounce,
    /// 1 is critically damped, and values above 1 are over-damped.
    /// </param>
    /// <param name="frequency">
    /// Natural frequency as the number of oscillations across the duration. Clamped to a small
    /// positive minimum.
    /// </param>
    public SpringEasing(double dampingRatio = DEFAULT_DAMPING_RATIO, double frequency = DEFAULT_FREQUENCY)
    {
        this.dampingRatio = Math.Max(dampingRatio, 0.0001);
        omega0 = 2.0 * Math.PI * Math.Max(frequency, 0.0001);
    }

    public double Apply(double progress)
    {
        // Pin the endpoints so transforms always start and finish exactly on their target value.
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;

        double t = progress;
        double envelope = Math.Exp(-dampingRatio * omega0 * t);

        double displacement;

        if (dampingRatio < 1.0)
        {
            // Under-damped: decaying oscillation.
            double omegaD = omega0 * Math.Sqrt(1.0 - dampingRatio * dampingRatio);
            displacement = envelope * (Math.Cos(omegaD * t) + dampingRatio * omega0 / omegaD * Math.Sin(omegaD * t));
        }
        else if (Math.Abs(dampingRatio - 1.0) < 1e-6)
        {
            // Critically damped.
            displacement = envelope * (1.0 + omega0 * t);
        }
        else
        {
            // Over-damped: hyperbolic, non-oscillating approach.
            double omegaD = omega0 * Math.Sqrt(dampingRatio * dampingRatio - 1.0);
            displacement = envelope * (Math.Cosh(omegaD * t) + dampingRatio * omega0 / omegaD * Math.Sinh(omegaD * t));
        }

        // "displacement" is the remaining distance to the target (1 at t=0, ~0 as it settles),
        // so the eased value is 1 minus that.
        return 1.0 - displacement;
    }
}
