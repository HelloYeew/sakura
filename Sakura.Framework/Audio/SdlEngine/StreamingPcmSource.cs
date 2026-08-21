// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Threading;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// An <see cref="IPcmSource"/> that decodes on demand, keeping a buffer of audio ready ahead of the
/// mixer. This is the track path.
/// </summary>
/// <remarks>
/// <para>
/// Three threads meet here, and which one does what is the whole design:
/// </para>
/// <list type="bullet">
/// <item>the <b>decode thread</b> calls <see cref="PumpDecode"/>, the only place FFmpeg is touched;</item>
/// <item>the <b>mix thread</b> calls <see cref="ReadFrames"/>, which only ever drains the ring buffer;</item>
/// <item>the <b>update thread</b> calls <see cref="Seek"/>, which never blocks on decoding.</item>
/// </list>
/// <para>
/// A seek therefore cannot be applied where it is requested. It is recorded as a request and the
/// decode thread picks it up, so audio decoded for the old position may still be in flight when it
/// lands. A generation counter stamps each request; the decode thread discards anything it produced
/// under a superseded generation rather than letting a moment of pre-seek audio through.
/// </para>
/// </remarks>
internal sealed class StreamingPcmSource : IPcmSource, IDecodeSource
{
    /// <summary>
    /// How far ahead to decode. Large enough that a GC pause or a slow read cannot starve the mixer,
    /// and irrelevant to output latency, which is set by the device buffer alone.
    /// </summary>
    private const double target_buffer_ms = 500;

    /// <summary>
    /// Don't wake the decoder for scraps; wait until this much of the buffer has drained.
    /// </summary>
    private const double refill_threshold_ms = 100;

    private readonly Lock sync = new Lock();

    /// <summary>
    /// Held across all decoding, and by <see cref="Dispose"/>, so the decoder is never freed while
    /// the decode thread is inside it.
    /// </summary>
    private readonly Lock decodeSync = new Lock();

    private readonly AudioRingBuffer ring;
    private readonly int deviceSampleRate;
    private readonly int deviceChannels;

    private readonly float[] decodeScratch = new float[8192];
    private readonly float[] convertScratch = new float[8192];

    private FFmpegAudioDecoder? decoder;
    private SDLAudioConverter? converter;

    private double? pendingSeekMs;
    private int seekGeneration;

    private double basePositionMs;
    private long framesSinceBase;

    /// <summary>
    /// Set once the decoder has no more audio. Distinct from <see cref="Ended"/>, which also waits
    /// for the ring to drain.
    /// </summary>
    private bool decoderDrained;

    private bool isDisposed;

    public double LengthMs { get; }

    /// <summary>
    /// How many times the mixer asked for frames the decoder had not produced yet. Non-zero means
    /// decoding is not keeping up. For benchmark.
    /// </summary>
    public long Underruns => Interlocked.Read(ref underruns);

    private long underruns;

    public double PositionMs
    {
        get
        {
            lock (sync)
                return basePositionMs + framesSinceBase / (double)deviceSampleRate * 1000.0;
        }
    }

    public bool Ended
    {
        get
        {
            lock (sync)
                return decoderDrained && ring.Available == 0;
        }
    }

    /// <summary>
    /// Whether the decode thread should spend time on this source right now.
    /// </summary>
    public bool WantsDecode
    {
        get
        {
            lock (sync)
            {
                if (isDisposed)
                    return false;

                if (pendingSeekMs.HasValue)
                    return true;

                if (decoderDrained)
                    return false;

                return ring.Available <= millisecondsToFloats(target_buffer_ms - refill_threshold_ms);
            }
        }
    }

    /// <param name="stream">The encoded source. Ownership passes to this instance.</param>
    /// <param name="deviceSampleRate">Sample rate to decode to, in Hz.</param>
    /// <param name="deviceChannels">Channel count to decode to.</param>
    /// <exception cref="InvalidDataException">The source could not be decoded.</exception>
    public StreamingPcmSource(Stream stream, int deviceSampleRate, int deviceChannels)
    {
        this.deviceSampleRate = deviceSampleRate;
        this.deviceChannels = deviceChannels;

        decoder = new FFmpegAudioDecoder(stream);

        try
        {
            converter = new SDLAudioConverter(decoder.SampleRate, decoder.Channels, deviceSampleRate, deviceChannels);
        }
        catch
        {
            decoder.Dispose();
            decoder = null;
            throw;
        }

        LengthMs = decoder.Duration;
        ring = new AudioRingBuffer(millisecondsToFloats(target_buffer_ms));
    }

    private int millisecondsToFloats(double milliseconds) =>
        Math.Max(deviceChannels, (int)(milliseconds / 1000.0 * deviceSampleRate) * deviceChannels);

    public int ReadFrames(Span<float> destination, int frameCount)
    {
        if (frameCount <= 0)
            return 0;

        int wanted = Math.Min(frameCount * deviceChannels, destination.Length - destination.Length % deviceChannels);

        if (wanted <= 0)
            return 0;

        int got = ring.Read(destination.Slice(0, wanted));
        int frames = got / deviceChannels;

        lock (sync)
        {
            if (isDisposed)
                return 0;

            framesSinceBase += frames;

            // Short of what was asked for, with the decoder still holding audio, means decoding
            // fell behind. At the true end of the source this is not an underrun.
            if (got < wanted && !decoderDrained)
                Interlocked.Increment(ref underruns);
        }

        return frames;
    }

    public void Seek(double milliseconds)
    {
        lock (sync)
        {
            if (isDisposed)
                return;

            double target = Math.Max(0, milliseconds);

            pendingSeekMs = target;
            seekGeneration++;

            // Everything buffered belongs to where we just left, so drop it. Reporting the new
            // position immediately — rather than when the decode thread catches up — is what makes
            // a read straight after a write see the value that was written.
            ring.Clear();
            basePositionMs = target;
            framesSinceBase = 0;
            decoderDrained = false;
        }
    }

    /// <summary>
    /// Does one unit of decoding work. Called only from the decode thread.
    /// </summary>
    /// <returns>True if there is more to do for this source right now.</returns>
    public bool PumpDecode()
    {
        lock (decodeSync)
        {
            if (isDisposed || decoder == null || converter == null)
                return false;

            double? seek;
            int generation;

            lock (sync)
            {
                seek = pendingSeekMs;
                pendingSeekMs = null;
                generation = seekGeneration;

                if (seek == null && (decoderDrained || ring.FreeSpace < convertScratch.Length))
                    return false;
            }

            if (seek.HasValue)
            {
                decoder.Seek(seek.Value);
                converter.Clear();
                decoderDrained = false;
            }

            int read = decoder.Read(decodeScratch);

            if (read == 0)
            {
                // Flush or the converter withholds its tail — the last few milliseconds of every
                // track would silently go missing.
                converter.Flush();
            }
            else
            {
                converter.Put(decodeScratch.AsSpan(0, read));
            }

            int converted = converter.Get(convertScratch);

            lock (sync)
            {
                // A seek landed while this block was being decoded, so it is audio from a position
                // we have already left. Dropping it is the point of the generation counter.
                if (generation != seekGeneration || isDisposed)
                    return true;

                if (converted > 0)
                    ring.Write(convertScratch.AsSpan(0, converted));

                if (read == 0 && converted == 0)
                {
                    decoderDrained = true;
                    return false;
                }
            }

            return true;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (isDisposed)
                return;

            isDisposed = true;
        }

        // Outside the state lock but inside the decode lock: waits for any in-flight decode to
        // finish rather than freeing FFmpeg state underneath it.
        lock (decodeSync)
        {
            decoder?.Dispose();
            decoder = null;
            converter?.Dispose();
            converter = null;
        }

        ring.Clear();
    }
}
