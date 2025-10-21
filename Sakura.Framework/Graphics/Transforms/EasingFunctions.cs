// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Transforms;

/// <summary>
/// A collection of standard easing functions.
/// Based on https://easings.net/
/// </summary>
public static class EasingFunctions
{
    public static double Apply(Easing easing, double t)
    {
        switch (easing)
        {
            case Easing.None:
                return t;
            case Easing.InSine:
                return 1 - Math.Cos(t * Math.PI / 2);
            case Easing.OutSine:
                return Math.Sin(t * Math.PI / 2);
            case Easing.InOutSine:
                return -(Math.Cos(Math.PI * t) - 1) / 2;
            case Easing.InQuad:
                return t * t;
            case Easing.OutQuad:
                return 1 - (1 - t) * (1 - t);
            case Easing.InOutQuad:
                return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            case Easing.InCubic:
                return t * t * t;
            case Easing.OutCubic:
                return 1 - Math.Pow(1 - t, 3);
            case Easing.InOutCubic:
                return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            case Easing.InQuart:
                return t * t * t * t;
            case Easing.OutQuart:
                return 1 - Math.Pow(1 - t, 4);
            case Easing.InOutQuart:
                return t < 0.5 ? 8 * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 4) / 2;
            case Easing.InQuint:
                return t * t * t * t * t;
            case Easing.OutQuint:
                return 1 - Math.Pow(1 - t, 5);
            case Easing.InOutQuint:
                return t < 0.5 ? 16 * t * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 5) / 2;
            case Easing.InExpo:
                return t == 0 ? 0 : Math.Pow(2, 10 * t - 10);
            case Easing.OutExpo:
                return t == 1 ? 1 : 1 - Math.Pow(2, -10 * t);
            case Easing.InOutExpo:
                return t == 0 ? 0 : t == 1 ? 1 : t < 0.5 ? Math.Pow(2, 20 * t - 10) / 2 : (2 - Math.Pow(2, -20 * t + 10)) / 2;
            case Easing.InCirc:
                return 1 - Math.Sqrt(1 - Math.Pow(t, 2));
            case Easing.OutCirc:
                return Math.Sqrt(1 - Math.Pow(t - 1, 2));
            case Easing.InOutCirc:
                return t < 0.5 ? (1 - Math.Sqrt(1 - Math.Pow(2 * t, 2))) / 2 : (Math.Sqrt(1 - Math.Pow(-2 * t + 2, 2)) + 1) / 2;
            case Easing.InBack:
            {
                const double c1 = 1.70158;
                const double c3 = c1 + 1;
                return c3 * t * t * t - c1 * t * t;
            }
            case Easing.OutBack:
            {
                const double c1 = 1.70158;
                const double c3 = c1 + 1;
                return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
            }
            case Easing.InOutBack:
            {
                const double c1 = 1.70158;
                const double c2 = c1 * 1.525;
                return t < 0.5
                    ? (Math.Pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2)) / 2
                    : (Math.Pow(2 * t - 2, 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
            }
            case Easing.InElastic:
            {
                const double c4 = (2 * Math.PI) / 3;
                return t == 0 ? 0 : t == 1 ? 1 : -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * c4);
            }
            case Easing.OutElastic:
            {
                const double c4 = (2 * Math.PI) / 3;
                return t == 0 ? 0 : t == 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1;
            }
            case Easing.InOutElastic:
            {
                const double c5 = (2 * Math.PI) / 4.5;
                return t == 0 ? 0 : t == 1 ? 1 : t < 0.5
                    ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * c5)) / 2
                    : (Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * c5)) / 2 + 1;
            }
            case Easing.InBounce:
                return 1 - Apply(Easing.OutBounce, 1 - t);
            case Easing.OutBounce:
                return t < 1 / 2.75
                    ? 7.5625 * t * t
                    : t < 2 / 2.75
                        ? 7.5625 * (t -= 1.5 / 2.75) * t + 0.75
                        : t < 2.5 / 2.75
                            ? 7.5625 * (t -= 2.25 / 2.75) * t + 0.9375
                            : 7.5625 * (t -= 2.625 / 2.75) * t + 0.984375;
            case Easing.InOutBounce:
                return t < 0.5
                    ? (1 - Apply(Easing.OutBounce, 1 - 2 * t)) / 2
                    : (1 + Apply(Easing.OutBounce, 2 * t - 1)) / 2;
            default:
                throw new ArgumentOutOfRangeException(nameof(easing), easing, null);
        }
    }
}
