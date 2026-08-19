// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// A whole audio file decoded to interleaved float PCM in the device's format, held in memory and
/// shared by every channel playing it.
/// </summary>
internal sealed class PcmBuffer
{
    /// <summary>
    /// Interleaved samples at <see cref="SampleRate"/> with <see cref="Channels"/> channels.
    /// </summary>
    public float[] Samples { get; }

    public int Channels { get; }

    public int SampleRate { get; }

    /// <summary>
    /// Length in frames.
    /// </summary>
    public int FrameCount => Samples.Length / Channels;

    public double LengthMs => FrameCount / (double)SampleRate * 1000.0;

    private PcmBuffer(float[] samples, int channels, int sampleRate)
    {
        Samples = samples;
        Channels = channels;
        SampleRate = sampleRate;
    }

    /// <summary>
    /// Decodes <paramref name="stream"/> in full and converts it to the given device format.
    /// </summary>
    /// <remarks>Takes ownership of the stream.</remarks>
    /// <exception cref="InvalidDataException">The source could not be decoded.</exception>
    public static PcmBuffer Decode(Stream stream, int deviceSampleRate, int deviceChannels)
    {
        using var decoder = new FFmpegAudioDecoder(stream);
        using var converter = new SdlAudioConverter(decoder.SampleRate, decoder.Channels, deviceSampleRate, deviceChannels);

        // Sized from the source duration where the container reports one, so the common case is a
        // single allocation rather than a doubling walk.
        int estimatedFrames = decoder.Duration > 0
            ? (int)(decoder.Duration / 1000.0 * deviceSampleRate) + deviceSampleRate
            : deviceSampleRate;

        float[] output = new float[Math.Max(estimatedFrames, 1) * deviceChannels];
        int written = 0;

        float[] decodeScratch = new float[8192];
        float[] convertScratch = new float[8192];

        while (true)
        {
            int read = decoder.Read(decodeScratch);

            if (read == 0)
            {
                // Without this the converter holds back its tail waiting for input that never comes.
                converter.Flush();
                written += drain(converter, convertScratch, ref output, written);
                break;
            }

            converter.Put(decodeScratch.AsSpan(0, read));
            written += drain(converter, convertScratch, ref output, written);
        }

        if (written < output.Length)
            Array.Resize(ref output, written);

        return new PcmBuffer(output, deviceChannels, deviceSampleRate);
    }

    private static int drain(SdlAudioConverter converter, float[] scratch, ref float[] output, int written)
    {
        int total = 0;

        while (true)
        {
            int got = converter.Get(scratch);

            if (got == 0)
                break;

            int required = written + total + got;

            if (required > output.Length)
                Array.Resize(ref output, Math.Max(required, output.Length * 2));

            scratch.AsSpan(0, got).CopyTo(output.AsSpan(written + total));
            total += got;
        }

        return total;
    }
}
