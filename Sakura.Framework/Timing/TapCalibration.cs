// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;

namespace Sakura.Framework.Timing;

/// <summary>
/// Measures how late someone's taps land against a steady beat they tap along to: the delay a rhythm game has to allow
/// for, between a beat being played and heard (or drawn and seen), and between a finger landing and the app hearing it.
/// </summary>
/// <remarks>
/// <para>
/// Two runs give a game what it needs. Tapping to a beat that's only heard measures sound and touch together, which is
/// what judging taps against the music has to allow for. Tapping to one that's only seen measures picture and touch, and
/// <see cref="SoundAfterPicture"/> of the two is how much later the music is heard than the screen is seen, which is
/// what drawing in time with the music has to allow for. Touch is in both, so it drops out of that one.
/// </para>
/// <para>
/// Touch can't be told apart from sound by tapping alone. Where the output measures its own latency (Android's, played
/// straight through AAudio), little of it is left for the first run to find, so that one is then mostly touch.
/// </para>
/// <para>
/// A tap counts against the nearest beat, and the beat goes round: on a 600 ms beat, a tap 250 ms late and one 350 ms
/// early are the same tap. Where taps gather is found the same way, so it holds where the delay is close to half a beat
/// (Bluetooth headphones on an output that can't say how late it is). A tap far from the rest, a doubled one or a missed
/// beat, doesn't count.
/// </para>
/// </remarks>
public sealed class TapCalibration
{
    /// <summary>
    /// A tap this far from where the rest land is a stray rather than someone's timing.
    /// </summary>
    private const double stray_ms = 120;

    // Each counted tap, as how far from its nearest beat it landed: within half a beat either way.
    private readonly List<double> phases = new List<double>();

    private readonly List<double> unwrapped = new List<double>();

    private int warmUpTaken;

    /// <summary>
    /// Milliseconds between beats.
    /// </summary>
    public double BeatInterval { get; }

    /// <summary>
    /// When a beat fell, in the same time as the taps; every other beat is a whole number of intervals from it.
    /// </summary>
    public double FirstBeat { get; }

    /// <summary>
    /// Taps at the start that don't count, while the player finds the beat.
    /// </summary>
    public int WarmUpTaps { get; init; } = 4;

    /// <summary>
    /// Taps that count towards <see cref="Offset"/>: after the warm-up, and not strays.
    /// </summary>
    public int Counted { get; private set; }

    /// <summary>
    /// How much later than the beat the taps land, in milliseconds, or null before any count. Negative where they land
    /// early. A game judging taps against the music takes it off a tap's time.
    /// </summary>
    public double? Offset { get; private set; }

    /// <summary>
    /// How far the counted taps scatter around <see cref="Offset"/>, as a standard deviation in milliseconds: how
    /// steadily the player tapped, and so how far to trust the offset. Null before two count.
    /// </summary>
    public double? Spread { get; private set; }

    /// <param name="beatInterval">Milliseconds between beats.</param>
    /// <param name="firstBeat">When a beat fell, in the same time as the taps.</param>
    public TapCalibration(double beatInterval, double firstBeat = 0)
    {
        if (beatInterval <= 0)
            throw new ArgumentOutOfRangeException(nameof(beatInterval), beatInterval, "A beat needs an interval.");

        BeatInterval = beatInterval;
        FirstBeat = firstBeat;
    }

    /// <summary>
    /// Takes a tap made at <paramref name="time"/>, in the same time as the beat.
    /// </summary>
    public void Add(double time)
    {
        if (warmUpTaken < WarmUpTaps)
        {
            warmUpTaken++;
            return;
        }

        phases.Add(phaseOf(time));
        settle();
    }

    /// <summary>
    /// How much later the music is heard than the screen is seen, from a run tapping to a beat that's heard and one
    /// tapping to a beat that's seen. A game draws this much behind the music's time to show what's being heard.
    /// </summary>
    /// <param name="heardOffset">The <see cref="Offset"/> from tapping to a beat that's heard.</param>
    /// <param name="seenOffset">The <see cref="Offset"/> from tapping to a beat that's seen.</param>
    public static double SoundAfterPicture(double heardOffset, double seenOffset) => heardOffset - seenOffset;

    private double phaseOf(double time)
    {
        double phase = (time - FirstBeat) % BeatInterval;

        if (phase < 0)
            phase += BeatInterval;

        if (phase > BeatInterval / 2)
            phase -= BeatInterval;

        return phase;
    }

    private void settle()
    {
        // Where the taps gather on the circle a beat makes, so a group straddling the half-beat isn't split in two.
        double sin = 0;
        double cos = 0;

        foreach (double phase in phases)
        {
            double angle = phase / BeatInterval * 2 * Math.PI;
            sin += Math.Sin(angle);
            cos += Math.Cos(angle);
        }

        double centre = Math.Atan2(sin, cos) / (2 * Math.PI) * BeatInterval;

        // Every tap as its nearest distance from there, so they all sit on one side of the wrap together.
        unwrapped.Clear();

        foreach (double phase in phases)
        {
            double fromCentre = (phase - centre) % BeatInterval;

            if (fromCentre > BeatInterval / 2)
                fromCentre -= BeatInterval;
            else if (fromCentre <= -BeatInterval / 2)
                fromCentre += BeatInterval;

            unwrapped.Add(centre + fromCentre);
        }

        // Strays are judged from the median, which they can't drag about the way they drag a mean.
        unwrapped.Sort();
        double median = unwrapped.Count % 2 == 1
            ? unwrapped[unwrapped.Count / 2]
            : (unwrapped[unwrapped.Count / 2 - 1] + unwrapped[unwrapped.Count / 2]) / 2;

        double sum = 0;
        double sumOfSquares = 0;
        int counted = 0;

        foreach (double tap in unwrapped)
        {
            if (Math.Abs(tap - median) > stray_ms)
                continue;

            sum += tap;
            sumOfSquares += tap * tap;
            counted++;
        }

        Counted = counted;
        Offset = sum / counted;
        Spread = counted > 1 ? Math.Sqrt(Math.Max(0, (sumOfSquares - sum * sum / counted) / (counted - 1))) : null;
    }
}
