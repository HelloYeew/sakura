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
    private const double elastic_const = 2 * Math.PI / .3;
    private const double elastic_const2 = .3 / 4;

    private const double back_const = 1.70158;
    private const double back_const2 = back_const * 1.525;

    private const double bounce_const = 1 / 2.75;

    // constants used to fix expo and elastic curves to always start and end at 0 or 1
    private static readonly double expo_offset = Math.Pow(2, -10);
    private static readonly double elastic_offset_full = Math.Pow(2, -11);
    private static readonly double elastic_offset_half = Math.Pow(2, -10) * Math.Sin((.5 - elastic_const2) * elastic_const);
    private static readonly double elastic_offset_quarter = Math.Pow(2, -10) * Math.Sin((.25 - elastic_const2) * elastic_const);
    private static readonly double in_out_elastic_offset = Math.Pow(2, -10) * Math.Sin((1 - elastic_const2 * 1.5) * elastic_const / 1.5);

    public static double Apply(Easing easing, double time)
    {
        switch (easing)
        {
            default:
            case Easing.None:
                return time;

            case Easing.InSine:
                return 1 - Math.Cos(time * Math.PI / 2);

            case Easing.OutSine:
                return Math.Sin(time * Math.PI / 2);

            case Easing.InOutSine:
                return 0.5 - 0.5 * Math.Cos(Math.PI * time);

            case Easing.InQuad:
                return time * time;

            case Easing.OutQuad:
                return time * (2 - time);

            case Easing.InOutQuad:
                return time < 0.5 ? time * time * 2 : --time * time * -2 + 1;

            case Easing.InCubic:
                return time * time * time;

            case Easing.OutCubic:
                return --time * time * time + 1;

            case Easing.InOutCubic:
                return time < 0.5 ? time * time * time * 4 : --time * time * time * 4 + 1;

            case Easing.InQuart:
                return time * time * time * time;

            case Easing.OutQuart:
                return 1 - --time * time * time * time;

            case Easing.InOutQuart:
                return time < 0.5 ? time * time * time * time * 8 : --time * time * time * time * -8 + 1;

            case Easing.InQuint:
                return time * time * time * time * time;

            case Easing.OutQuint:
                return --time * time * time * time * time + 1;

            case Easing.InOutQuint:
                return time < 0.5 ? time * time * time * time * time * 16 : --time * time * time * time * time * 16 + 1;

            case Easing.InExpo:
                return Math.Pow(2, 10 * (time - 1)) + expo_offset * (time - 1);

            case Easing.OutExpo:
                return -Math.Pow(2, -10 * time) + 1 + expo_offset * time;

            case Easing.InOutExpo:
                if (time < 0.5) return 0.5 * (Math.Pow(2, 20 * time - 10) + expo_offset * (2 * time - 1));
                return 1 - 0.5 * (Math.Pow(2, -20 * time + 10) + expo_offset * (-2 * time + 1));

            case Easing.InCirc:
                return 1 - Math.Sqrt(1 - time * time);

            case Easing.OutCirc:
                return Math.Sqrt(1 - --time * time);

            case Easing.InOutCirc:
                if ((time *= 2) < 1) return 0.5 - 0.5 * Math.Sqrt(1 - time * time);
                return 0.5 * Math.Sqrt(1 - (time -= 2) * time) + 0.5;

            case Easing.InBack:
                return time * time * ((back_const + 1) * time - back_const);

            case Easing.OutBack:
                return --time * time * ((back_const + 1) * time + back_const) + 1;

            case Easing.InOutBack:
                if ((time *= 2) < 1) return 0.5 * time * time * ((back_const2 + 1) * time - back_const2);
                return 0.5 * ((time -= 2) * time * ((back_const2 + 1) * time + back_const2) + 2);

            case Easing.InElastic:
                return -Math.Pow(2, -10 + 10 * time) * Math.Sin((1 - elastic_const2 - time) * elastic_const) + elastic_offset_full * (1 - time);

            case Easing.OutElastic:
                return Math.Pow(2, -10 * time) * Math.Sin((time - elastic_const2) * elastic_const) + 1 - elastic_offset_full * time;

            case Easing.OutElasticHalf:
                return Math.Pow(2, -10 * time) * Math.Sin((time - elastic_const2) * elastic_const) + 1 - elastic_offset_half * time;

            case Easing.OutElasticQuarter:
                return Math.Pow(2, -10 * time) * Math.Sin((time - elastic_const2) * elastic_const) + 1 - elastic_offset_quarter * time;

            case Easing.InOutElastic:
                if ((time *= 2) < 1)
                {
                    return -0.5 * (Math.Pow(2, -10 + 10 * time) * Math.Sin((1 - elastic_const2 * 1.5 - time) * elastic_const / 1.5)
                                  - in_out_elastic_offset * (1 - time));
                }
                return 0.5 * (Math.Pow(2, -10 * --time) * Math.Sin((time - elastic_const2 * 1.5) * elastic_const / 1.5)
                              - in_out_elastic_offset * time) + 1;

            case Easing.InBounce:
                return 1 - Apply(Easing.OutBounce, 1 - time);

            case Easing.OutBounce:
                if (time < bounce_const)
                    return 7.5625 * time * time;
                if (time < 2 * bounce_const)
                    return 7.5625 * (time -= 1.5 * bounce_const) * time + 0.75;
                if (time < 2.5 * bounce_const)
                    return 7.5625 * (time -= 2.25 * bounce_const) * time + 0.9375;
                return 7.5625 * (time -= 2.625 * bounce_const) * time + 0.984375;

            case Easing.InOutBounce:
                if (time < 0.5) return 0.5 - 0.5 * Apply(Easing.OutBounce, 1 - time * 2);
                return Apply(Easing.OutBounce, (time - 0.5) * 2) * 0.5 + 0.5;
        }
    }
}
