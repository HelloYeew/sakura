// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Text;

namespace Sakura.Framework.Audio;

/// <summary>
/// A metronome to play as a track: clicks on a steady beat, made as a WAV in memory. Every click starts exactly on its
/// beat's sample, and the first on the very first one, so a track of it looping keeps the beat across the loop.
/// </summary>
public static class ClickTrack
{
    /// <summary>
    /// The rate the clicks are made at.
    /// </summary>
    public const int SAMPLE_RATE = 48000;

    private const double click_ms = 30;
    private const double decay_ms = 6;
    private const double click_hz = 1500;
    private const double accent_hz = 2200;
    private const double amplitude = 0.7;

    /// <summary>
    /// The beat interval a track made for <paramref name="beatInterval"/> keeps: the nearest whole number of samples.
    /// </summary>
    public static double IntervalFor(double beatInterval) => framesPerBeat(beatInterval) * 1000.0 / SAMPLE_RATE;

    /// <summary>
    /// Makes the WAV: <paramref name="beats"/> beats <paramref name="beatInterval"/> apart, every
    /// <paramref name="accentEvery"/>th one higher, to hear where a bar starts. A click is a short, quickly fading tone
    /// at full strength from its first sample, so it's heard where it starts.
    /// </summary>
    /// <param name="beatInterval">Milliseconds between beats, kept to the nearest sample (<see cref="IntervalFor"/>).</param>
    /// <param name="beats">How many beats long it is.</param>
    /// <param name="accentEvery">Beats between accented ones, or 0 for none.</param>
    public static Stream Create(double beatInterval, int beats, int accentEvery = 4)
    {
        int beatFrames = framesPerBeat(beatInterval);

        if (beatFrames <= 0 || beats <= 0)
            throw new ArgumentOutOfRangeException(nameof(beats), "A click track needs beats and time between them.");

        int clickFrames = Math.Min(beatFrames, (int)(click_ms * SAMPLE_RATE / 1000));
        short[] click = makeClick(clickFrames, click_hz);
        short[] accent = makeClick(clickFrames, accent_hz);

        int dataBytes = beatFrames * beats * sizeof(short);
        var stream = new MemoryStream(44 + dataBytes);

        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataBytes);
            writer.Write("WAVE"u8);

            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(SAMPLE_RATE);
            writer.Write(SAMPLE_RATE * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);

            writer.Write("data"u8);
            writer.Write(dataBytes);

            for (int beat = 0; beat < beats; beat++)
            {
                var sound = accentEvery > 0 && beat % accentEvery == 0 ? accent : click;

                for (int frame = 0; frame < beatFrames; frame++)
                    writer.Write(frame < sound.Length ? sound[frame] : (short)0);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static int framesPerBeat(double beatInterval) => (int)Math.Round(beatInterval * SAMPLE_RATE / 1000);

    private static short[] makeClick(int frames, double frequency)
    {
        short[] samples = new short[frames];

        for (int i = 0; i < frames; i++)
        {
            double seconds = i / (double)SAMPLE_RATE;

            // Cosine, so the first sample is the loudest: the click is heard from where it starts.
            double value = Math.Cos(2 * Math.PI * frequency * seconds) * Math.Exp(-seconds * 1000 / decay_ms) * amplitude;
            samples[i] = (short)Math.Round(value * short.MaxValue);
        }

        return samples;
    }
}
