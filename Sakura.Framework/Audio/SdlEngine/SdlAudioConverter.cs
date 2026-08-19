// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using SDL;
using static SDL.SDL3;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Converts float PCM between sample rates and channel counts, wrapping an <c>SDL_AudioStream</c>
/// used purely as a converter, it is never bound to a device.
/// </summary>
/// <remarks>
/// This class stand for <c>libswresample</c>, which the shipped FFmpeg build deliberately omits.
/// Also, this is not thread-safe, an instance belongs to whichever thread is decoding into it.
/// </remarks>
internal sealed unsafe class SdlAudioConverter : IDisposable
{
    private const int bytes_per_float = sizeof(float);

    /// <summary>
    /// The sample format used on both sides of every conversion.
    /// </summary>
    /// <remarks>
    /// Spelled <c>LE</c> explicitly because the binding exposes only the two endian-specific enum
    /// members, not SDL's native-endian <c>SDL_AUDIO_F32</c> alias. Every platform this framework
    /// targets is little-endian, and a big-endian port would have to revisit this.
    /// </remarks>
    internal const SDL_AudioFormat SAMPLE_FORMAT = SDL_AudioFormat.SDL_AUDIO_F32LE;

    private SDL_AudioStream* stream;

    /// <summary>
    /// Number of floats currently converted and waiting to be read.
    /// </summary>
    public int Available
    {
        get
        {
            if (stream == null)
                return 0;

            int bytes = SDL_GetAudioStreamAvailable(stream);
            return bytes <= 0 ? 0 : bytes / bytes_per_float;
        }
    }

    /// <param name="sourceRate">Input sample rate in Hz.</param>
    /// <param name="sourceChannels">Input channel count.</param>
    /// <param name="targetRate">Output sample rate in Hz.</param>
    /// <param name="targetChannels">Output channel count.</param>
    /// <exception cref="InvalidOperationException">SDL could not create the stream.</exception>
    public SdlAudioConverter(int sourceRate, int sourceChannels, int targetRate, int targetChannels)
    {
        var source = new SDL_AudioSpec
        {
            format = SAMPLE_FORMAT,
            channels = sourceChannels,
            freq = sourceRate
        };

        var target = new SDL_AudioSpec
        {
            format = SAMPLE_FORMAT,
            channels = targetChannels,
            freq = targetRate
        };

        stream = SDL_CreateAudioStream(&source, &target);

        if (stream == null)
            throw new InvalidOperationException($"SDL_CreateAudioStream failed: {SDL_GetError()}");
    }

    /// <summary>
    /// Feeds interleaved source-format floats in.
    /// </summary>
    public void Put(ReadOnlySpan<float> source)
    {
        if (stream == null || source.IsEmpty)
            return;

        fixed (float* pointer = source)
        {
            if (!SDL_PutAudioStreamData(stream, (IntPtr)pointer, source.Length * bytes_per_float))
                throw new InvalidOperationException($"SDL_PutAudioStreamData failed: {SDL_GetError()}");
        }
    }

    /// <summary>
    /// Reads converted interleaved target-format floats out.
    /// </summary>
    /// <returns>The number of floats written, which may be fewer than requested.</returns>
    public int Get(Span<float> destination)
    {
        if (stream == null || destination.IsEmpty)
            return 0;

        fixed (float* pointer = destination)
        {
            int bytes = SDL_GetAudioStreamData(stream, (IntPtr)pointer, destination.Length * bytes_per_float);
            return bytes <= 0 ? 0 : bytes / bytes_per_float;
        }
    }

    /// <summary>
    /// Tells the converter no more input is coming, so it emits the tail it would otherwise hold
    /// back waiting for more. Call at the end of the stream, or the last few milliseconds never arrive.
    /// </summary>
    public void Flush()
    {
        if (stream != null)
            SDL_FlushAudioStream(stream);
    }

    /// <summary>
    /// Discards all buffered input and output. Used on seek, where anything still in flight belongs
    /// to the position being left behind.
    /// </summary>
    public void Clear()
    {
        if (stream != null)
            SDL_ClearAudioStream(stream);
    }

    public void Dispose()
    {
        if (stream == null)
            return;

        SDL_DestroyAudioStream(stream);
        stream = null;
    }
}
