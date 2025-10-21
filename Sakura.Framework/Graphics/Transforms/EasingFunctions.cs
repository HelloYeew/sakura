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
    public static double Apply(EasingType easing, double t)
    {
        switch (easing)
        {
            case EasingType.None:
                return t;
            case EasingType.InSine:
                return 1 - Math.Cos(t * Math.PI / 2);
            case EasingType.OutSine:
                return Math.Sin(t * Math.PI / 2);
            case EasingType.InOutSine:
                return -(Math.Cos(Math.PI * t) - 1) / 2;
            case EasingType.InQuad:
                return t * t;
            case EasingType.OutQuad:
                return 1 - (1 - t) * (1 - t);
            case EasingType.InOutQuad:
                return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            case EasingType.InCubic:
                return t * t * t;
            case EasingType.OutCubic:
                return 1 - Math.Pow(1 - t, 3);
            case EasingType.InOutCubic:
                return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            case EasingType.InQuart:
                return t * t * t * t;
            case EasingType.OutQuart:
                return 1 - Math.Pow(1 - t, 4);
            case EasingType.InOutQuart:
                return t < 0.5 ? 8 * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 4) / 2;
            case EasingType.InQuint:
                return t * t * t * t * t;
            case EasingType.OutQuint:
                return 1 - Math.Pow(1 - t, 5);
            case EasingType.InOutQuint:
                return t < 0.5 ? 16 * t * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 5) / 2;
            case EasingType.InExpo:
                return t == 0 ? 0 : Math.Pow(2, 10 * t - 10);
            case EasingType.OutExpo:
                return t == 1 ? 1 : 1 - Math.Pow(2, -10 * t);
            case EasingType.InOutExpo:
                return t == 0 ? 0 : t == 1 ? 1 : t < 0.5 ? Math.Pow(2, 20 * t - 10) / 2 : (2 - Math.Pow(2, -20 * t + 10)) / 2;
            case EasingType.InCirc:
                return 1 - Math.Sqrt(1 - Math.Pow(t, 2));
            case EasingType.OutCirc:
                return Math.Sqrt(1 - Math.Pow(t - 1, 2));
            case EasingType.InOutCirc:
                return t < 0.5 ? (1 - Math.Sqrt(1 - Math.Pow(2 * t, 2))) / 2 : (Math.Sqrt(1 - Math.Pow(-2 * t + 2, 2)) + 1) / 2;
            case EasingType.InBack:
            {
                const double c1 = 1.70158;
                const double c3 = c1 + 1;
                return c3 * t * t * t - c1 * t * t;
            }
            case EasingType.OutBack:
            {
                const double c1 = 1.70158;
                const double c3 = c1 + 1;
                return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
            }
            case EasingType.InOutBack:
            {
                const double c1 = 1.70158;
                const double c2 = c1 * 1.525;
                return t < 0.5
                    ? (Math.Pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2)) / 2
                    : (Math.Pow(2 * t - 2, 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
            }
            case EasingType.InElastic:
            {
                const double c4 = (2 * Math.PI) / 3;
                return t == 0 ? 0 : t == 1 ? 1 : -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * c4);
            }
            case EasingType.OutElastic:
            {
                const double c4 = (2 * Math.PI) / 3;
                return t == 0 ? 0 : t == 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1;
            }
            case EasingType.InOutElastic:
            {
                const double c5 = (2 * Math.PI) / 4.5;
                return t == 0 ? 0 : t == 1 ? 1 : t < 0.5
                    ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * c5)) / 2
                    : (Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * c5)) / 2 + 1;
            }
            case EasingType.InBounce:
                return 1 - Apply(EasingType.OutBounce, 1 - t);
            case EasingType.OutBounce:
                return t < 1 / 2.75
                    ? 7.5625 * t * t
                    : t < 2 / 2.75
                        ? 7.5625 * (t -= 1.5 / 2.75) * t + 0.75
                        : t < 2.5 / 2.75
                            ? 7.5625 * (t -= 2.25 / 2.75) * t + 0.9375
                            : 7.5625 * (t -= 2.625 / 2.75) * t + 0.984375;
            case EasingType.InOutBounce:
                return t < 0.5
                    ? (1 - Apply(EasingType.OutBounce, 1 - 2 * t)) / 2
                    : (1 + Apply(EasingType.OutBounce, 2 * t - 1)) / 2;
            default:
                throw new ArgumentOutOfRangeException(nameof(easing), easing, null);
        }
    }
}
