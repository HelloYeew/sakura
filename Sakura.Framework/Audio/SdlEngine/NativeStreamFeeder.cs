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
/// <para>
/// <see cref="IAudioChannel.Tempo"/> is applied here too, through a <see cref="TimeStretcher"/>
/// between the decoder and the ring, since the native mixer only resamples. The tempo is latched with
/// each seek and a change of speed arrives as a seek to where the listener is, so the ring never holds
/// audio at two speeds and the channel can turn frames played into a position with one multiply.
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
    private readonly float[] stretchScratch = new float[8192];

    private FFmpegAudioDecoder? decoder;

    /// <summary>
    /// The rate and channel conversion, or null when the file is already in the device's format and
    /// decoded floats go straight into the ring.
    /// </summary>
    private SDLAudioConverter? converter;

    private double? pendingSeekMs;

    /// <summary>
    /// The tempo the next seek starts at. Written by the update thread, latched by the decode thread.
    /// </summary>
    private double requestedTempo = 1;

    /// <summary>
    /// The tempo the ring is being filled at, or 1 when decoded audio goes straight in. Decode thread.
    /// </summary>
    private double sessionTempo = 1;

    /// <summary>
    /// Created the first time a track is played at anything but normal speed, then kept.
    /// </summary>
    private TimeStretcher? stretcher;

    /// <summary>
    /// Set once the decoder has run out and the stretcher has been told so. Decode thread.
    /// </summary>
    private bool stretchFlushed;

    /// <summary>
    /// Floats sitting in <see cref="convertScratch"/> that are decoded but not yet written, or 0.
    /// </summary>
    private int staged;

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
            // An SDL stream as a pass-through would only copy every float in and back out again.
            if (decoder.SampleRate != deviceSampleRate || decoder.Channels != deviceChannels)
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
            converter?.Dispose();
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
    /// the handful of milliseconds before the decode thread gets its first turn — registering the
    /// channel wakes it — so two passes (~170 ms of audio) rather than the eight this used to run on
    /// whichever thread created the channel, which is often the update thread.
    /// </remarks>
    private void primeRing()
    {
        const int max_passes = 2;

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

    /// <summary>
    /// Sets the tempo the ring is filled at from the next <see cref="Seek"/> on. The channel follows
    /// this with a seek to the current position, which is what applies it.
    /// </summary>
    public void SetTempo(double tempo)
    {
        lock (sync)
            requestedTempo = Math.Clamp(tempo, TimeStretcher.MIN_TEMPO, TimeStretcher.MAX_TEMPO);
    }

    public bool PumpDecode()
    {
        lock (decodeSync)
        {
            if (isDisposed || decoder == null)
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
                converter?.Clear();

                // Whatever was held back belongs to the position we are leaving.
                staged = 0;

                double tempo;

                lock (sync)
                {
                    tempo = requestedTempo;
                    decoderDrained = false;
                }

                // A change of speed reaches this side as a seek, so this is where it takes effect.
                startStretch(tempo);

                // Posts the discard. Everything buffered belongs to where we just left, and the audio
                // thread is the only side that may drop it.
                engine.StreamFlushBegin(voice);
            }

            // Nothing may be written while a discard is outstanding: the ring still holds the audio it
            // is replacing, so its free space is not ours yet, and ring_write refuses outright.
            bool flushPending = engine.StreamFlushPending(voice);

            if (sessionTempo != 1)
                return pumpStretched(flushPending);

            // A block held back from an earlier pass goes first, or the stream plays out of order.
            // This is the pass right after a seek: the wait is over and the ring is filled with a copy
            // rather than a decoding.
            if (!flushPending && staged > 0)
            {
                if (engine.StreamSpace(voice) * deviceChannels < staged)
                    return false;

                lock (sync)
                {
                    if (isDisposed)
                        return false;

                    engine.StreamWrite(voice, convertScratch.AsSpan(0, staged));
                    staged = 0;
                }

                return true;
            }

            lock (sync)
            {
                if (decoderDrained)
                    return false;

                // While the discard is outstanding, the ring's free space says nothing, so decode
                // anyway and hold the result — the thread would otherwise spend the wait asleep and
                // then still owe a decoding once it woke, and every millisecond of that is a running
                // voice playing silence. convertScratch is where it is held.
                if (!flushPending && engine.StreamSpace(voice) * deviceChannels < convertScratch.Length)
                    return false;

                if (staged > 0)
                    return false;
            }

            int converted = decodeBlock(out int read);

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
                {
                    if (flushPending)
                        staged = converted;
                    else
                        engine.StreamWrite(voice, convertScratch.AsSpan(0, converted));
                }

                if (read == 0 && converted == 0)
                {
                    decoderDrained = true;
                    engine.StreamSetDrained(voice, true);
                    return false;
                }
            }

            // A pass that only staged has nothing further to do until the audio thread releases the
            // ring and says so rather than being asked seven more times.
            return !flushPending;
        }
    }

    /// <summary>
    /// Decodes the next block into <see cref="convertScratch"/>, in the device's format.
    /// </summary>
    /// <param name="read">Floats the decoder produced before conversion: 0 once it has run out.</param>
    /// <returns>Floats now in <see cref="convertScratch"/>.</returns>
    private int decodeBlock(out int read)
    {
        if (converter == null)
        {
            // Already the device's format: decoded straight into the block that gets written.
            read = decoder!.Read(convertScratch);
            return read;
        }

        read = decoder!.Read(decodeScratch);

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

        return converter.Get(convertScratch);
    }

    private void startStretch(double tempo)
    {
        sessionTempo = tempo;
        stretchFlushed = false;

        if (tempo == 1)
            return;

        stretcher ??= new TimeStretcher(deviceSampleRate, deviceChannels);
        stretcher.Reset(tempo);
    }

    /// <summary>
    /// <see cref="PumpDecode"/> while the tempo is not 1: decoded audio goes through the stretcher, and
    /// the stretcher's output is what reaches the ring.
    /// </summary>
    /// <remarks>
    /// The stretcher's own output buffer takes the place of <see cref="staged"/>. It holds what the ring
    /// had no room for, or what was produced while a discard was outstanding, and it is emptied before
    /// anything more is decoded so that it never holds more than one block's worth.
    /// </remarks>
    private bool pumpStretched(bool flushPending)
    {
        var stretch = stretcher!;

        if (stretch.AvailableFrames > 0)
        {
            if (flushPending)
                return false;

            int space = engine.StreamSpace(voice);

            if (space <= 0)
                return false;

            int frames = stretch.ReceiveOutput(stretchScratch, Math.Min(space, stretchScratch.Length / deviceChannels));

            lock (sync)
            {
                if (isDisposed)
                    return false;

                // The audio just taken belongs to a position being left; the seek resets the stretcher.
                if (pendingSeekMs.HasValue)
                    return true;

                engine.StreamWrite(voice, stretchScratch.AsSpan(0, frames * deviceChannels));
            }

            return true;
        }

        if (stretchFlushed)
        {
            lock (sync)
            {
                if (isDisposed || decoderDrained)
                    return false;

                // Only now, with the stretcher's tail in the ring, is the stream really at its end.
                decoderDrained = true;
                engine.StreamSetDrained(voice, true);
            }

            return false;
        }

        int converted = decodeBlock(out int read);

        lock (sync)
        {
            if (isDisposed)
                return false;

            // Decoded from a position we have already left; the next pass starts again from the new one.
            if (pendingSeekMs.HasValue)
                return true;
        }

        if (converted > 0)
        {
            stretch.PutInput(convertScratch.AsSpan(0, converted));
        }
        else if (read == 0)
        {
            stretch.Flush();
            stretchFlushed = true;
        }

        return true;
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
