// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Threading;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Keeps one native voice's ring buffer fed: decodes, converts to the device format, and writes into
/// libsakura-audio. The track paths decode side, and the native counterpart to
/// <see cref="StreamingPcmSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// Three threads meet here, as they do in the managed source, but the mixing one is now the device
/// callback and is on the other side of a P/Invoke boundary:
/// </para>
/// <list type="bullet">
/// <item>the <b>decode thread</b> calls <see cref="PumpDecode"/>, the only place FFmpeg is touched
/// and the only writer to this voice's ring;</item>
/// <item>the <b>audio thread</b> drains the ring inside the native engine, and never enters managed
/// code to do it which is the entire point of the native mixer;</item>
/// <item>the <b>update thread</b> calls <see cref="Seek"/>, which never blocks on decoding.</item>
/// </list>
/// <para>
/// A seek therefore cannot be applied where it is requested, and the discard cannot be done by this
/// side at all: only the audio thread knows where its read cursor is. So a seek here decodes from the
/// new position, posts the discard, and then waits — by declining to write — until the audio thread
/// acknowledges it. Writing before that would throw the new position's audio away along with the old.
/// The wait costs one decode pass, which against a 500 ms buffer is nothing.
/// </para>
/// </remarks>
internal sealed class NativeStreamFeeder : IDecodeSource, IDisposable
{
    /// <summary>
    /// How far ahead to decode. Large enough that a GC pause or a slow read cannot starve the mixer,
    /// and irrelevant to output latency, which is set by the device buffer alone.
    /// </summary>
    public const double TARGET_BUFFER_MS = 500;

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

    private readonly SakuraAudioEngine engine;
    private readonly uint voice;
    private readonly int deviceSampleRate;
    private readonly int deviceChannels;

    private readonly float[] decodeScratch = new float[8192];
    private readonly float[] convertScratch = new float[8192];

    private FFmpegAudioDecoder? decoder;
    private SDLAudioConverter? converter;

    private double? pendingSeekMs;

    /// <summary>
    /// Set once the decoder has no more audio, and mirrored into the native voice, which is only
    /// ended once it is drained <em>and</em> its ring is empty.
    /// </summary>
    private bool decoderDrained;

    private bool isDisposed;

    public double LengthMs { get; }

    /// <summary>
    /// Total capacity of the native ring, in frames.
    /// </summary>
    public int CapacityFrames { get; }

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

                // Keep coming back while a discard is outstanding: the audio thread applies it on its
                // next callback, and until it has there is nothing this side may write.
                if (engine.StreamFlushPending(voice))
                    return true;

                if (decoderDrained)
                    return false;

                return engine.StreamBuffered(voice) <= framesFor(TARGET_BUFFER_MS - refill_threshold_ms);
            }
        }
    }

    /// <param name="stream">The encoded source. Ownership passes to this instance.</param>
    /// <param name="engine">The native engine holding <paramref name="voice"/>.</param>
    /// <param name="voice">The voice whose ring this instance feeds.</param>
    /// <exception cref="InvalidDataException">The source could not be decoded.</exception>
    /// <exception cref="InvalidOperationException">The voice would not take a ring buffer.</exception>
    public NativeStreamFeeder(Stream stream, SakuraAudioEngine engine, uint voice)
    {
        this.engine = engine;
        this.voice = voice;

        deviceSampleRate = engine.SampleRate;
        deviceChannels = engine.Channels;

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
        CapacityFrames = framesFor(TARGET_BUFFER_MS) / deviceChannels;

        if (!engine.SetVoiceStream(voice, CapacityFrames))
        {
            decoder.Dispose();
            decoder = null;
            converter.Dispose();
            converter = null;
            throw new InvalidOperationException("The native voice would not take a ring buffer.");
        }

        // Prime the ring on the calling thread, before anything can play out of it.
        //
        // Doing it here rather than in Play is what makes it a guarantee rather than a smaller race
        // constructing a track already opens and decodes a file on this thread, so one more decode pass
        // is in keeping, and by the time a caller has a channel to press Play on, the ring is not empty.
        primeRing();
    }

    /// <summary>
    /// Decodes until the ring is reasonably full or the source runs out, whichever comes first.
    /// </summary>
    /// <remarks>
    /// Bounded by passes rather than run to completion since the decode thread is perfectly capable of
    /// filling the remaining 500 ms, and a track whose whole buffer had to be decoded before its
    /// constructor returned would make loading a beatmap slower to no purpose. This only has to cover
    /// the handful of milliseconds before the decode thread gets its first turn.
    /// </remarks>
    private void primeRing()
    {
        const int max_passes = 8;

        for (int i = 0; i < max_passes && WantsDecode; i++)
        {
            if (!PumpDecode())
                break;
        }
    }

    /// <summary>
    /// Milliseconds expressed as a whole number of interleaved floats.
    /// </summary>
    private int framesFor(double milliseconds) =>
        Math.Max(deviceChannels, (int)(milliseconds / 1000.0 * deviceSampleRate) * deviceChannels);

    /// <summary>
    /// Records a seek for the decode thread to apply. Returns immediately; the audio thread's side of
    /// it is the channel's <see cref="SakuraAudioEngine.Seek"/>.
    /// </summary>
    public void Seek(double milliseconds)
    {
        lock (sync)
        {
            if (isDisposed)
                return;

            pendingSeekMs = Math.Max(0, milliseconds);

            // Cleared here as well as by the flush so that WantsDecode does not read a stale end and
            // decline to refill the position we are seeking to.
            decoderDrained = false;
        }
    }

    public bool PumpDecode()
    {
        lock (decodeSync)
        {
            if (isDisposed || decoder == null || converter == null)
                return false;

            double? seek;

            lock (sync)
            {
                seek = pendingSeekMs;
                pendingSeekMs = null;
            }

            if (seek.HasValue)
            {
                decoder.Seek(seek.Value);
                converter.Clear();

                lock (sync)
                    decoderDrained = false;

                // Posts the discard. Everything buffered belongs to where we just left, and the audio
                // thread is the only side that may drop it.
                engine.StreamFlushBegin(voice);
            }

            // Nothing may be written while a discard is outstanding: it would be thrown away with the
            // audio it is replacing. Come back on the next pass, by which point a running device has
            // acknowledged it.
            if (engine.StreamFlushPending(voice))
                return false;

            lock (sync)
            {
                if (decoderDrained || engine.StreamSpace(voice) * deviceChannels < convertScratch.Length)
                    return false;
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
                if (isDisposed)
                    return false;

                // A seek landed while this block was being decoded, so it is audio from a position we
                // have already left. The next pass will discard the ring and start from the new one,
                // so dropping this block is all that is needed — there is no generation counter here
                // because the ring itself is about to be emptied.
                if (pendingSeekMs.HasValue)
                    return true;

                if (converted > 0)
                    engine.StreamWrite(voice, convertScratch.AsSpan(0, converted));

                if (read == 0 && converted == 0)
                {
                    decoderDrained = true;
                    engine.StreamSetDrained(voice, true);
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

        // Outside the state lock but inside the decode lock: waits for any in-flight decode to finish
        // rather than freeing FFmpeg state underneath it.
        lock (decodeSync)
        {
            decoder?.Dispose();
            decoder = null;
            converter?.Dispose();
            converter = null;
        }
    }
}
