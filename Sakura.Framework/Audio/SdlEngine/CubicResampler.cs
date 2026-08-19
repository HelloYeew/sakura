// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Per-voice playback-rate resampling, using 4-point third-order Hermite interpolation.
/// </summary>
internal sealed class CubicResampler
{
    /// <summary>
    /// Interpolation needs the frame either side of the one being produced, so the window is four
    /// frames wide: <c>w0</c> trails, <c>w1</c>–<c>w2</c> bracket the output, <c>w3</c> leads.
    /// </summary>
    private const int window_frames = 4;

    /// <summary>
    /// Bounds on the rate ratio. The lower bound keeps a near-zero ratio from spinning out enormous
    /// numbers of output frames from one input frame; the upper bound caps how far ahead of the
    /// source a single block can run.
    /// </summary>
    private const double min_ratio = 1.0 / 64.0;

    private const double max_ratio = 64.0;

    private readonly int channels;
    private readonly float[] window;
    private readonly float[] frameScratch;

    /// <summary>
    /// Position between <c>w1</c> and <c>w2</c>, in [0, 1).
    /// </summary>
    private double position;

    private bool primed;

    /// <summary>
    /// How many all-zero frames have been shifted in since the source ran dry. Once the whole window
    /// is zeros there is nothing left to interpolate and the resampler reports exhaustion.
    /// </summary>
    private int drainedFrames;

    public CubicResampler(int channels)
    {
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));

        this.channels = channels;
        window = new float[window_frames * channels];
        frameScratch = new float[channels];
    }

    /// <summary>
    /// Discards the interpolation window. Required after a seek, carrying frames across a
    /// discontinuity smears audio from the old position into the new one.
    /// </summary>
    public void Reset()
    {
        Array.Clear(window);
        position = 0;
        primed = false;
        drainedFrames = 0;
    }

    /// <summary>
    /// Produces up to <paramref name="frameCount"/> frames into <paramref name="destination"/>,
    /// pulling from <paramref name="source"/> at <paramref name="ratio"/> input frames per output
    /// frame.
    /// </summary>
    /// <returns>
    /// Frames produced. Fewer than requested means the source could not supply enough input either
    /// it ended, or it is a streaming source that has not decoded far enough ahead.
    /// </returns>
    public int Read(IPcmSource source, Span<float> destination, int frameCount, double ratio)
    {
        if (frameCount <= 0)
            return 0;

        ratio = Math.Clamp(ratio, min_ratio, max_ratio);

        if (!primed && !prime(source))
            return 0;

        int produced = 0;

        while (produced < frameCount)
        {
            int offset = produced * channels;

            for (int ch = 0; ch < channels; ch++)
            {
                float w0 = window[ch];
                float w1 = window[channels + ch];
                float w2 = window[2 * channels + ch];
                float w3 = window[3 * channels + ch];

                destination[offset + ch] = interpolate(w0, w1, w2, w3, (float)position);
            }

            produced++;
            position += ratio;

            while (position >= 1.0)
            {
                if (!advance(source))
                    return produced;

                position -= 1.0;
            }
        }

        return produced;
    }

    /// <summary>
    /// 4-point, third-order Hermite (Catmull-Rom) interpolation between <paramref name="w1"/> and
    /// <paramref name="w2"/>.
    /// </summary>
    private static float interpolate(float w0, float w1, float w2, float w3, float t)
    {
        float c0 = w1;
        float c1 = 0.5f * (w2 - w0);
        float c2 = w0 - 2.5f * w1 + 2f * w2 - 0.5f * w3;
        float c3 = 0.5f * (w3 - w0) + 1.5f * (w1 - w2);

        return ((c3 * t + c2) * t + c1) * t + c0;
    }

    /// <summary>
    /// Fills the window for the first time. <c>w0</c> is left silent: there is no frame before the
    /// start of the source, and inventing one would be worse than starting from zero.
    /// </summary>
    private bool prime(IPcmSource source)
    {
        for (int i = 1; i < window_frames; i++)
        {
            if (!pullFrame(source, window.AsSpan(i * channels, channels)))
            {
                // Nothing at all to play yet. Stay unprimed so the next call tries again rather than
                // treating a not-yet-decoded streaming source as an empty one.
                if (i == 1)
                {
                    Array.Clear(window);
                    return false;
                }

                break;
            }
        }

        primed = true;
        position = 0;
        return true;
    }

    /// <summary>
    /// Slides the window forward one frame.
    /// </summary>
    private bool advance(IPcmSource source)
    {
        Array.Copy(window, channels, window, 0, (window_frames - 1) * channels);

        var last = window.AsSpan((window_frames - 1) * channels, channels);

        if (pullFrame(source, last))
        {
            drainedFrames = 0;
            return true;
        }

        // Let the tail of the window play out into silence rather than cutting off mid-sample, but
        // stop once the whole window is zeros — past that there is genuinely nothing left.
        last.Clear();

        return ++drainedFrames < window_frames;
    }

    private bool pullFrame(IPcmSource source, Span<float> destination)
    {
        if (source.ReadFrames(frameScratch, 1) != 1)
            return false;

        frameScratch.CopyTo(destination);
        return true;
    }
}
