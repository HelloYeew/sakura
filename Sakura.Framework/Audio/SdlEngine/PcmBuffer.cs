// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;

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

    /// <summary>
    /// Floats decoded or converted per pass. Under the 85 KB large-object threshold, and rented, so a
    /// decode allocates no scratch of its own.
    /// </summary>
    private const int scratch_floats = 8192;

    /// <summary>
    /// Frames allowed on top of the container's stated duration before the output has to grow. The
    /// duration is exact for most formats and a little out for some (VBR MP3 without a seek table),
    /// so this is slack for the second case, not a second of headroom for every sample.
    /// </summary>
    private const int estimate_slack_frames = 4096;

    private PcmBuffer(float[] samples, int channels, int sampleRate)
    {
        Samples = samples;
        Channels = channels;
        SampleRate = sampleRate;
    }

    /// <summary>
    /// Wraps already-decoded interleaved samples, which are taken as-is and not copied.
    /// </summary>
    /// <remarks>
    /// For callers that produced PCM by some route other than <see cref="Decode"/> synthesized
    /// audio, and the test suite, which needs buffers with exactly known contents.
    /// </remarks>
    public static PcmBuffer FromSamples(float[] samples, int channels, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        return samples.Length % channels != 0 ? throw new ArgumentException("Sample count must be a whole number of frames.", nameof(samples)) : new PcmBuffer(samples, channels, sampleRate);

    }

    /// <summary>
    /// Decodes <paramref name="stream"/> in full and converts it to the given device format.
    /// </summary>
    /// <remarks>Takes ownership of the stream.</remarks>
    /// <exception cref="InvalidDataException">The source could not be decoded.</exception>
    public static PcmBuffer Decode(Stream stream, int deviceSampleRate, int deviceChannels)
    {
        var sink = new ManagedSink();

        decode(stream, deviceSampleRate, deviceChannels, ref sink);

        float[] output = sink.Output;

        // Exactly sized, since FrameCount is read from the array's length. With the estimate now
        // within a few thousand frames of the truth this is a small copy, not the old second of slack.
        if (sink.Written < output.Length)
            Array.Resize(ref output, sink.Written);

        return new PcmBuffer(output, deviceChannels, deviceSampleRate);
    }

    /// <summary>
    /// Decodes <paramref name="stream"/> in full straight into a buffer of <paramref name="engine"/>,
    /// without the decoded audio ever being a managed array.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The native sample path. Decoding into a <c>float[]</c> and handing that over put every
    /// hitsound on the large object heap — sized a whole second past its length, then resized, then
    /// copied into the engine and dropped — which is tens of megabytes of LOH garbage and a gen 2
    /// collection or two per beatmap load. Here the PCM goes into unmanaged memory, the engine copies
    /// it into its own pool, and the staging block is freed on the spot.
    /// </para>
    /// <para>Takes ownership of the stream.</para>
    /// </remarks>
    /// <returns>The engine's buffer handle, or 0 when the engine would not take it.</returns>
    /// <exception cref="InvalidDataException">The source could not be decoded.</exception>
    public static unsafe uint DecodeIntoEngine(Stream stream, SakuraAudioEngine engine, out double lengthMs)
    {
        var sink = new NativeSink();

        try
        {
            decode(stream, engine.SampleRate, engine.Channels, ref sink);

            int frames = sink.Written / engine.Channels;
            lengthMs = frames / (double)engine.SampleRate * 1000.0;

            return frames > 0 ? engine.CreateBuffer(new ReadOnlySpan<float>(sink.Data, frames * engine.Channels)) : 0;
        }
        finally
        {
            sink.Free();
        }
    }

    /// <summary>
    /// Where decoded, converted floats go. A struct with a generic constraint rather than an interface
    /// call per pass, and so the managed and unmanaged destinations share one decode loop.
    /// </summary>
    private interface ISink
    {
        void Reserve(int floats);

        void Append(ReadOnlySpan<float> floats);
    }

    private struct ManagedSink : ISink
    {
        public float[] Output;
        public int Written;

        public void Reserve(int floats) => Output = new float[Math.Max(floats, 1)];

        public void Append(ReadOnlySpan<float> floats)
        {
            int required = Written + floats.Length;

            if (required > Output.Length)
                Array.Resize(ref Output, Math.Max(required, Output.Length * 2));

            floats.CopyTo(Output.AsSpan(Written));
            Written += floats.Length;
        }
    }

    private unsafe struct NativeSink : ISink
    {
        public float* Data;
        public int Written;
        private int capacity;

        public void Reserve(int floats)
        {
            capacity = Math.Max(floats, 1);
            Data = (float*)NativeMemory.Alloc((nuint)capacity, sizeof(float));
        }

        public void Append(ReadOnlySpan<float> floats)
        {
            int required = Written + floats.Length;

            if (required > capacity)
            {
                capacity = Math.Max(required, capacity * 2);
                Data = (float*)NativeMemory.Realloc(Data, (nuint)capacity * sizeof(float));
            }

            floats.CopyTo(new Span<float>(Data + Written, floats.Length));
            Written += floats.Length;
        }

        public void Free()
        {
            NativeMemory.Free(Data);
            Data = null;
        }
    }

    private static void decode<TSink>(Stream stream, int deviceSampleRate, int deviceChannels, ref TSink sink)
        where TSink : struct, ISink
    {
        using var decoder = new FFmpegAudioDecoder(stream);

        // A file already in the device's format needs no conversion at all, and the SDL stream would
        // only have copied every float in and back out again.
        using var converter = decoder.SampleRate == deviceSampleRate && decoder.Channels == deviceChannels
            ? null
            : new SDLAudioConverter(decoder.SampleRate, decoder.Channels, deviceSampleRate, deviceChannels);

        // Sized from the source duration where the container reports one, so the common case is a
        // single allocation rather than a doubling walk.
        int estimatedFrames = decoder.Duration > 0
            ? (int)(decoder.Duration / 1000.0 * deviceSampleRate) + estimate_slack_frames
            : deviceSampleRate;

        sink.Reserve(estimatedFrames * deviceChannels);

        float[] decodeScratch = ArrayPool<float>.Shared.Rent(scratch_floats);
        float[] convertScratch = ArrayPool<float>.Shared.Rent(scratch_floats);

        try
        {
            var decodeSpan = decodeScratch.AsSpan(0, scratch_floats);
            var convertSpan = convertScratch.AsSpan(0, scratch_floats);

            while (true)
            {
                int read = decoder.Read(decodeSpan);

                if (converter == null)
                {
                    if (read == 0)
                        break;

                    sink.Append(decodeSpan[..read]);
                    continue;
                }

                if (read == 0)
                {
                    // Without this the converter holds back its tail waiting for input that never comes.
                    converter.Flush();
                    drain(converter, convertSpan, ref sink);
                    break;
                }

                converter.Put(decodeSpan[..read]);
                drain(converter, convertSpan, ref sink);
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(decodeScratch);
            ArrayPool<float>.Shared.Return(convertScratch);
        }
    }

    private static void drain<TSink>(SDLAudioConverter converter, Span<float> scratch, ref TSink sink)
        where TSink : struct, ISink
    {
        while (true)
        {
            int got = converter.Get(scratch);

            if (got == 0)
                break;

            sink.Append(scratch[..got]);
        }
    }
}
