// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Transforms;

/// <summary>
/// A cubic bezier easing curve equivalent to CSS's <c>cubic-bezier(x1, y1, x2, y2)</c>.
/// The curve runs from a fixed start point (0, 0) to a fixed end point (1, 1) via the two
/// user-supplied control points (<c>x1</c>, <c>y1</c>) and (<c>x2</c>, <c>y2</c>).
/// </summary>
/// <remarks>
/// Given an input <c>x</c> (normalised time), the curve is solved for the parametric value
/// <c>t</c> such that the bezier's x-component equals <c>x</c>, then the y-component at that
/// <c>t</c> is returned. This mirrors the well-known WebKit <c>UnitBezier</c> implementation:
/// a fast Newton-Raphson search that falls back to bisection when convergence is poor.
/// </remarks>
public sealed class CubicBezierEasing : IEasingFunction
{
    // Polynomial coefficients for the x and y components, expanded from the bezier basis with
    // P0 = (0,0) and P3 = (1,1). x(t) = ((ax*t + bx)*t + cx)*t (and likewise for y).
    private readonly double ax, bx, cx;
    private readonly double ay, by, cy;

    private const int newton_iterations = 8;
    private const double newton_min_slope = 0.001;
    private const double subdivision_precision = 0.0000001;
    private const int subdivision_max_iterations = 12;

    /// <param name="x1">X of the first control point. CSS clamps this to [0, 1]; values outside are allowed but may be non-monotonic.</param>
    /// <param name="y1">Y of the first control point.</param>
    /// <param name="x2">X of the second control point.</param>
    /// <param name="y2">Y of the second control point.</param>
    public CubicBezierEasing(double x1, double y1, double x2, double y2)
    {
        // Control-point X must stay within [0, 1] for the curve to represent a valid time function.
        x1 = Math.Clamp(x1, 0, 1);
        x2 = Math.Clamp(x2, 0, 1);

        cx = 3.0 * x1;
        bx = 3.0 * (x2 - x1) - cx;
        ax = 1.0 - cx - bx;

        cy = 3.0 * y1;
        by = 3.0 * (y2 - y1) - cy;
        ay = 1.0 - cy - by;
    }

    public double Apply(double progress)
    {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;

        return sampleCurveY(solveCurveX(progress));
    }

    private double sampleCurveX(double t) => ((ax * t + bx) * t + cx) * t;

    private double sampleCurveY(double t) => ((ay * t + by) * t + cy) * t;

    private double sampleCurveDerivativeX(double t) => (3.0 * ax * t + 2.0 * bx) * t + cx;

    private double solveCurveX(double x)
    {
        double t2 = x;

        // First try Newton-Raphson — it converges very quickly for well-behaved curves.
        for (int i = 0; i < newton_iterations; i++)
        {
            double x2 = sampleCurveX(t2) - x;
            if (Math.Abs(x2) < subdivision_precision)
                return t2;

            double d2 = sampleCurveDerivativeX(t2);
            if (Math.Abs(d2) < newton_min_slope)
                break;

            t2 -= x2 / d2;
        }

        // Fall back to bisection to guarantee a result within the valid range.
        double tLower = 0.0;
        double tUpper = 1.0;
        t2 = x;

        for (int i = 0; i < subdivision_max_iterations; i++)
        {
            double x2 = sampleCurveX(t2);
            if (Math.Abs(x2 - x) < subdivision_precision)
                return t2;

            if (x > x2)
                tLower = t2;
            else
                tUpper = t2;

            t2 = (tUpper + tLower) * 0.5;
        }

        return t2;
    }
}
