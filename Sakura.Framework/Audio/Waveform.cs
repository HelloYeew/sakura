// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sakura.Framework.Audio.SdlEngine;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Audio;

/// <summary>
/// The shape of a piece of audio over time, for drawing (see
/// <see cref="Graphics.Audio.WaveformGraph"/>): how loud each channel is, and how much of that is
/// bass, mids and treble, one <see cref="Point"/> per <see cref="RESOLUTION_MS"/> of audio.
/// </summary>
/// <remarks>
/// <para>
/// Decoded once, in the background, as soon as this is constructed; every accessor waits for that
/// to finish. The decoding goes through FFmpeg directly rather than through whichever audio backend is
/// playing, so it works the same on all of them and never takes a voice.
/// </para>
/// </remarks>
public class Waveform : IDisposable
{
    /// <summary>
    /// How much audio each point covers, in milliseconds.
    /// </summary>
    public const double RESOLUTION_MS = 1;

    private const float low_min = 20;
    private const float mid_min = 100;
    private const float high_min = 2000;
    private const float high_max = 12000;

    private Point[] points = Array.Empty<Point>();
    private int channels;

    private readonly CancellationTokenSource cancelSource = new CancellationTokenSource();
    private readonly Task readTask;

    /// <param name="data">The encoded audio. Ownership passes to this instance. Null makes an empty waveform.</param>
    public Waveform(Stream? data)
    {
        if (data == null)
        {
            readTask = Task.CompletedTask;
            return;
        }

        var token = cancelSource.Token;

        readTask = Task.Run(() =>
        {
            try
            {
                read(data, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Logger.Error("Could not read a waveform.", e);
            }
            finally
            {
                data.Dispose();
            }
        });
    }

    private Waveform(Point[] points, int channels)
    {
        this.points = points;
        this.channels = channels;
        readTask = Task.CompletedTask;
    }

    private void read(Stream data, CancellationToken token)
    {
        using var decoder = new FFmpegAudioDecoder(data);

        int sourceChannels = Math.Max(1, decoder.Channels);

        // Fractional: at 44.1 kHz a point is 44.1 frames, and rounding that to 44 would put the
        // waveform 0.4 s ahead of the song by the end of a three-minute track.
        double framesPerPoint = decoder.SampleRate * RESOLUTION_MS / 1000;

        var result = new List<Point>(Math.Max(0, (int)(decoder.Duration / RESOLUTION_MS) + 1));
        var fft = new AudioFft();

        float[] decoded = new float[8192 * sourceChannels];
        float[] mono = new float[AudioFft.FFT_SIZE];
        float[] bins = new float[AudioFft.BIN_COUNT];

        int monoCount = 0;

        // Points whose band intensities are still waiting on the spectrum that covers them.
        int firstWithoutBands = 0;

        var current = new Point();
        long frameIndex = 0;
        bool currentHasAudio = false;

        int read;

        while ((read = decoder.Read(decoded)) > 0)
        {
            token.ThrowIfCancellationRequested();

            int frames = read / sourceChannels;

            for (int frame = 0; frame < frames; frame++)
            {
                int index = frame * sourceChannels;
                float left = decoded[index];
                float right = sourceChannels > 1 ? decoded[index + 1] : left;

                current.AmplitudeLeft = Math.Max(current.AmplitudeLeft, Math.Abs(left));
                current.AmplitudeRight = Math.Max(current.AmplitudeRight, Math.Abs(right));
                currentHasAudio = true;

                // The point ends on the first frame at or past its boundary.
                if (++frameIndex >= (result.Count + 1) * framesPerPoint)
                {
                    current.AmplitudeLeft = Math.Min(1, current.AmplitudeLeft);
                    current.AmplitudeRight = Math.Min(1, current.AmplitudeRight);
                    result.Add(current);

                    current = new Point();
                    currentHasAudio = false;
                }

                float sum = 0;

                for (int c = 0; c < sourceChannels; c++)
                    sum += decoded[index + c];

                mono[monoCount++] = sum / sourceChannels;

                if (monoCount == mono.Length)
                {
                    fft.Compute(mono, bins);
                    applyBands(result, ref firstWithoutBands, result.Count, bins, decoder.SampleRate);
                    monoCount = 0;
                }
            }
        }

        if (currentHasAudio)
            result.Add(current);

        if (monoCount > 0)
        {
            fft.Compute(mono.AsSpan(0, monoCount), bins);
            applyBands(result, ref firstWithoutBands, result.Count, bins, decoder.SampleRate);
        }

        channels = sourceChannels;
        points = result.ToArray();
    }

    /// <summary>
    /// Gives every point from <paramref name="first"/> up to <paramref name="end"/> the band
    /// intensities of the spectrum that just covered them.
    /// </summary>
    private static void applyBands(List<Point> result, ref int first, int end, float[] bins, int sampleRate)
    {
        float low = intensity(bins, sampleRate, low_min, mid_min);
        float mid = intensity(bins, sampleRate, mid_min, high_min);
        float high = intensity(bins, sampleRate, high_min, high_max);

        for (; first < end; first++)
        {
            var point = result[first];
            point.LowIntensity = low;
            point.MidIntensity = mid;
            point.HighIntensity = high;
            result[first] = point;
        }
    }

    private static float intensity(float[] bins, int sampleRate, float startFrequency, float endFrequency)
    {
        int startBin = Math.Clamp((int)(AudioFft.FFT_SIZE * startFrequency / sampleRate), 0, bins.Length);
        int endBin = Math.Clamp((int)(AudioFft.FFT_SIZE * endFrequency / sampleRate), 0, bins.Length);

        float value = 0;

        for (int i = startBin; i < endBin; i++)
            value += bins[i];

        return value;
    }

    /// <summary>
    /// Every point, one per <see cref="RESOLUTION_MS"/> from the start of the audio. Blocks until the
    /// audio has been read.
    /// </summary>
    public Point[] GetPoints() => GetPointsAsync().GetAwaiter().GetResult();

    public async Task<Point[]> GetPointsAsync()
    {
        await readTask.ConfigureAwait(false);
        return points;
    }

    /// <summary>
    /// How many channels the source had. Blocks until the audio has been read.
    /// </summary>
    public int GetChannels() => GetChannelsAsync().GetAwaiter().GetResult();

    public async Task<int> GetChannelsAsync()
    {
        await readTask.ConfigureAwait(false);
        return channels;
    }

    /// <summary>
    /// A copy of this waveform smoothed down (or up) to <paramref name="pointCount"/> points, for
    /// drawing at a given width. A Gaussian over the neighbouring points rather than a plain pick, so
    /// short peaks don't flicker in and out as the width changes.
    /// </summary>
    public async Task<Waveform> GenerateResampledAsync(int pointCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pointCount);

        if (pointCount == 0)
            return new Waveform(Array.Empty<Point>(), channels);

        await readTask.ConfigureAwait(false);

        return await Task.Run(() =>
        {
            var source = points;
            var generated = new Point[pointCount];

            if (source.Length == 0)
                return new Waveform(generated, channels);

            double sourcePerGenerated = (double)source.Length / pointCount;
            double sigma = Math.Max(sourcePerGenerated, 0.5);

            // Three sigma either way holds all but a sliver of the weight.
            int kernelWidth = (int)Math.Ceiling(sigma * 3);
            float[] kernel = new float[kernelWidth + 1];

            for (int i = 0; i <= kernelWidth; i++)
                kernel[i] = (float)Math.Exp(-i * i / (2 * sigma * sigma));

            for (int g = 0; g < pointCount; g++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int centre = (int)(g * sourcePerGenerated);
                int from = Math.Max(0, centre - kernelWidth);
                int to = Math.Min(source.Length - 1, centre + kernelWidth);

                var point = new Point();
                float total = 0;

                for (int j = from; j <= to; j++)
                {
                    float weight = kernel[Math.Abs(j - centre)];
                    total += weight;

                    point.AmplitudeLeft += weight * source[j].AmplitudeLeft;
                    point.AmplitudeRight += weight * source[j].AmplitudeRight;
                    point.LowIntensity += weight * source[j].LowIntensity;
                    point.MidIntensity += weight * source[j].MidIntensity;
                    point.HighIntensity += weight * source[j].HighIntensity;
                }

                if (total > 0)
                {
                    point.AmplitudeLeft /= total;
                    point.AmplitudeRight /= total;
                    point.LowIntensity /= total;
                    point.MidIntensity /= total;
                    point.HighIntensity /= total;
                }

                generated[g] = point;
            }

            return new Waveform(generated, channels);
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        cancelSource.Cancel();
        cancelSource.Dispose();
    }

    /// <summary>
    /// One <see cref="RESOLUTION_MS"/> of audio.
    /// </summary>
    public struct Point
    {
        /// <summary>
        /// The left channel's peak, 0 to 1. The only channel's, for mono audio.
        /// </summary>
        public float AmplitudeLeft;

        /// <summary>
        /// The right channel's peak, 0 to 1. Equal to <see cref="AmplitudeLeft"/> for mono audio.
        /// </summary>
        public float AmplitudeRight;

        /// <summary>
        /// Spectral energy from 20 to 100 Hz: kicks and bass.
        /// </summary>
        public float LowIntensity;

        /// <summary>
        /// Spectral energy from 100 to 2000 Hz: most instruments and vocals.
        /// </summary>
        public float MidIntensity;

        /// <summary>
        /// Spectral energy from 2 to 12 kHz: hats, cymbals and sibilance.
        /// </summary>
        public float HighIntensity;
    }
}
