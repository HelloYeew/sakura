// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Transforms;

/// <summary>
/// Describes how a transform interpolates between its start and end values over time.
/// </summary>
public readonly struct EasingFunction
{
    private readonly Easing standard;
    private readonly IEasingFunction? custom;

    /// <summary>
    /// Creates an <see cref="EasingFunction"/> backed by one of the built-in <see cref="Easing"/> curves.
    /// </summary>
    public EasingFunction(Easing easing)
    {
        standard = easing;
        custom = null;
    }

    /// <summary>
    /// Creates an <see cref="EasingFunction"/> backed by a custom <see cref="IEasingFunction"/>.
    /// </summary>
    public EasingFunction(IEasingFunction easingFunction)
    {
        standard = Easing.None;
        custom = easingFunction;
    }

    /// <summary>
    /// Evaluates the easing at the given normalised <paramref name="progress"/> in [0, 1].
    /// </summary>
    public double Apply(double progress) => custom?.Apply(progress) ?? EasingFunctions.Apply(standard, progress);

    /// <summary>
    /// Implicitly promotes an <see cref="Easing"/> to an <see cref="EasingFunction"/>, preserving
    /// backward compatibility with every API that previously took an <see cref="Easing"/>.
    /// </summary>
    public static implicit operator EasingFunction(Easing easing) => new EasingFunction(easing);

    /// <summary>
    /// Implicitly wraps a custom <see cref="IEasingFunction"/> (e.g. <see cref="CubicBezierEasing"/>
    /// or <see cref="SpringEasing"/>) into an <see cref="EasingFunction"/>.
    /// </summary>
    public static implicit operator EasingFunction(CubicBezierEasing bezier) => new EasingFunction(bezier);

    /// <summary>
    /// Implicitly wraps a <see cref="SpringEasing"/> into an <see cref="EasingFunction"/>.
    /// </summary>
    public static implicit operator EasingFunction(SpringEasing spring) => new EasingFunction(spring);

    /// <summary>
    /// Creates a CSS-style cubic bezier easing through control points (<paramref name="x1"/>, <paramref name="y1"/>)
    /// and (<paramref name="x2"/>, <paramref name="y2"/>), with fixed endpoints (0, 0) and (1, 1).
    /// </summary>
    /// <example><code>
    /// // The CSS "ease-in-out" curve.
    /// drawable.MoveTo(target, 400, EasingFunction.CubicBezier(0.42, 0, 0.58, 1));
    /// </code></example>
    public static EasingFunction CubicBezier(double x1, double y1, double x2, double y2)
        => new EasingFunction(new CubicBezierEasing(x1, y1, x2, y2));

    /// <summary>
    /// Creates a physically-based spring easing that overshoots and settles at its target.
    /// </summary>
    /// <param name="dampingRatio">
    /// How quickly oscillations decay. Less than 1 is under-damped (bouncy overshoot), 1 is critically
    /// damped (no overshoot, fastest settle), greater than 1 is over-damped (slow, no overshoot).
    /// </param>
    /// <param name="frequency">
    /// The natural frequency of the spring, expressed as the number of oscillations that would occur
    /// across the transform's duration. Higher values feel stiffer.
    /// </param>
    /// <example><code>
    /// drawable.ScaleTo(1.5f, 600, EasingFunction.Spring());              // pleasant default bounce
    /// drawable.MoveTo(target, 800, EasingFunction.Spring(0.35, 2.5));    // looser, bouncier
    /// </code></example>
    public static EasingFunction Spring(double dampingRatio = SpringEasing.DEFAULT_DAMPING_RATIO, double frequency = SpringEasing.DEFAULT_FREQUENCY)
        => new EasingFunction(new SpringEasing(dampingRatio, frequency));
}
