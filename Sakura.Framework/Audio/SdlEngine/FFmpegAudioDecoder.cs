// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Decodes an audio file to interleaved 32-bit float PCM using FFmpeg, reading through a custom AVIO
/// context so any <see cref="Stream"/> can be a source.
/// </summary>
/// <remarks>
/// Not thread-safe. Each instance belongs to a single decode thread.
/// </remarks>
internal sealed unsafe class FFmpegAudioDecoder : IDisposable
{
    private const int io_buffer_size = 4096;

    /// <summary>
    /// The sample rate of the decoded output in Hz. This is the file's own rate.
    /// </summary>
    public int SampleRate { get; private set; }

    /// <summary>
    /// The channel count of the decoded output. This is the file's own channel count so mono files
    /// decode as mono.
    /// </summary>
    public int Channels { get; private set; }

    /// <summary>
    /// The total duration in milliseconds, or 0 if the container does not report one.
    /// </summary>
    public double Duration { get; private set; }

    /// <summary>
    /// Whether <see cref="Seek"/> can be honored. False for non-seekable sources.
    /// </summary>
    public bool CanSeek { get; private set; }

    /// <summary>
    /// True once the decoder has been fully drained. Cleared by a successful <see cref="Seek"/>.
    /// </summary>
    public bool EndOfStream { get; private set; }

    private Stream? source;
    private GCHandle selfHandle;

    private AVFormatContext* formatContext;
    private AVIOContext* ioContext;
    private AVCodecContext* codecContext;
    private AVPacket* packet;
    private AVFrame* frame;

    private avio_alloc_context_read_packet? readCallback;
    private avio_alloc_context_seek? seekCallback;

    private int audioStreamIndex = -1;
    private double timeBaseInSeconds;
    private bool inputOpened;

    /// <summary>
    /// Samples decoded from the current frame that did not fit in the caller's buffer, carried over
    /// to the next <see cref="Read"/>. Grown to the largest frame seen and then reused.
    /// </summary>
    private float[] pending = Array.Empty<float>();

    private int pendingOffset;
    private int pendingCount;

    /// <summary>
    /// True, once a null packet has been pushed to flush the decoder's internal buffer.
    /// </summary>
    private bool flushed;

    /// <summary>
    /// Opens <paramref name="stream"/> and reads enough of it to determine the stream format.
    /// The stream will be disposed of with this decoder.
    /// </summary>
    /// <exception cref="InvalidDataException">The source has no decodable audio stream.</exception>
    public FFmpegAudioDecoder(Stream stream)
    {
        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));

        FFmpegLibrary.EnsureInitialized();

        source = stream;
        selfHandle = GCHandle.Alloc(this);

        try
        {
            open();
        }
        catch
        {
            // The caller never receives the instance, so nothing else will dispose it.
            Dispose();
            throw;
        }
    }

    private void open()
    {
        readCallback = readPacket;
        seekCallback = source!.CanSeek ? seekStream : null;
        CanSeek = source.CanSeek;

        byte* ioBuffer = (byte*)ffmpeg.av_malloc(io_buffer_size);
        ioContext = ffmpeg.avio_alloc_context(ioBuffer, io_buffer_size, 0,
            (void*)GCHandle.ToIntPtr(selfHandle), readCallback, null, seekCallback);

        if (ioContext == null)
            throw new InvalidDataException("Could not allocate an AVIO context for audio decoding.");

        var context = ffmpeg.avformat_alloc_context();
        context->pb = ioContext;

        int openResult = ffmpeg.avformat_open_input(&context, "pipe:", null, null);

        if (openResult < 0)
            throw new InvalidDataException($"avformat_open_input failed for audio source: {errorString(openResult)}");

        inputOpened = true;
        formatContext = context;

        if (ffmpeg.avformat_find_stream_info(formatContext, null) < 0)
            throw new InvalidDataException("Could not read stream info from the audio source.");

        AVCodec* codec = null;
        audioStreamIndex = ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &codec, 0);

        if (audioStreamIndex < 0 || codec == null)
            throw new InvalidDataException("The source contains no decodable audio stream.");

        var avStream = formatContext->streams[audioStreamIndex];
        timeBaseInSeconds = avStream->time_base.num / (double)avStream->time_base.den;

        codecContext = ffmpeg.avcodec_alloc_context3(codec);

        if (codecContext == null)
            throw new InvalidDataException("Could not allocate an audio codec context.");

        if (ffmpeg.avcodec_parameters_to_context(codecContext, avStream->codecpar) < 0)
            throw new InvalidDataException("Could not copy audio codec parameters.");

        int codecOpenResult = ffmpeg.avcodec_open2(codecContext, codec, null);

        if (codecOpenResult < 0)
        {
            // The most likely cause by far is a decoder compiled out of the shipped FFmpeg build,
            // so name the codec rather than leaving a bare error code.
            string name = Marshal.PtrToStringAnsi((IntPtr)codec->name) ?? "unknown";
            throw new InvalidDataException($"Could not open the '{name}' audio decoder: {errorString(codecOpenResult)}. " +
                                           "The shipped FFmpeg build may not include it.");
        }

        SampleRate = codecContext->sample_rate;
        Channels = codecContext->ch_layout.nb_channels;

        if (SampleRate <= 0 || Channels <= 0)
            throw new InvalidDataException($"Audio stream reports an unusable format ({SampleRate}Hz, {Channels}ch).");

        Duration = avStream->duration > 0
            ? avStream->duration * timeBaseInSeconds * 1000.0
            : formatContext->duration > 0
                ? formatContext->duration / (double)ffmpeg.AV_TIME_BASE * 1000.0
                : 0;

        packet = ffmpeg.av_packet_alloc();
        frame = ffmpeg.av_frame_alloc();

        if (packet == null || frame == null)
            throw new InvalidDataException("Could not allocate FFmpeg audio packet/frame.");
    }

    /// <summary>
    /// Decodes into <paramref name="destination"/> as interleaved floats at <see cref="SampleRate"/>
    /// and <see cref="Channels"/>.
    /// </summary>
    /// <returns>
    /// The number of floats written, which is a whole number of frames. 0 means the stream is
    /// exhausted; <see cref="EndOfStream"/> is set at that point.
    /// </returns>
    public int Read(Span<float> destination)
    {
        if (destination.IsEmpty || codecContext == null)
            return 0;

        int written = 0;

        while (written < destination.Length)
        {
            if (pendingCount > 0)
            {
                int take = Math.Min(pendingCount, destination.Length - written);
                pending.AsSpan(pendingOffset, take).CopyTo(destination.Slice(written));
                pendingOffset += take;
                pendingCount -= take;
                written += take;
                continue;
            }

            if (!decodeNextFrame())
                break;
        }

        // Never hand back a partial frame, every consumer downstream counts in frames, and a split
        // frame would silently rotate the channel order of everything after it.
        int remainder = written % Channels;
        if (remainder != 0)
            written -= remainder;

        return written;
    }

    /// <summary>
    /// Pulls packets until one produces a decoded frame, which is converted into
    /// <see cref="pending"/>.
    /// </summary>
    /// <returns>False once the decoder is drained and no further frames will arrive.</returns>
    private bool decodeNextFrame()
    {
        while (true)
        {
            int receiveResult = ffmpeg.avcodec_receive_frame(codecContext, frame);

            if (receiveResult == 0)
            {
                convertFrame();
                ffmpeg.av_frame_unref(frame);

                // A frame carrying no samples is legal, keep pulling rather than reporting silence.
                if (pendingCount > 0)
                    return true;

                continue;
            }

            if (receiveResult == ffmpeg.AVERROR_EOF)
            {
                EndOfStream = true;
                return false;
            }

            if (receiveResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                Logger.Error($"[FFmpegAudioDecoder] avcodec_receive_frame failed: {errorString(receiveResult)}");
                EndOfStream = true;
                return false;
            }

            // EAGAIN: the decoder wants another packet.
            if (flushed)
            {
                EndOfStream = true;
                return false;
            }

            if (!sendNextPacket())
                return false;
        }
    }

    /// <summary>
    /// Reads packets until one belonging to the audio stream has been handed to the decoder, or the
    /// container runs out and the decoder is put into flush mode.
    /// </summary>
    private bool sendNextPacket()
    {
        while (true)
        {
            int readResult = ffmpeg.av_read_frame(formatContext, packet);

            if (readResult < 0)
            {
                // Push a null packet so the decoder emits whatever it is still holding.
                ffmpeg.avcodec_send_packet(codecContext, null);
                flushed = true;
                return true;
            }

            if (packet->stream_index != audioStreamIndex)
            {
                ffmpeg.av_packet_unref(packet);
                continue;
            }

            int sendResult = ffmpeg.avcodec_send_packet(codecContext, packet);
            ffmpeg.av_packet_unref(packet);

            if (sendResult < 0 && sendResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                Logger.Error($"[FFmpegAudioDecoder] avcodec_send_packet failed: {errorString(sendResult)}");
                EndOfStream = true;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Converts <see cref="frame"/> into interleaved float in <see cref="pending"/>.
    /// </summary>
    /// <remarks>
    /// This is the job <c>libswresample</c> would normally do. It is not built into the shipped
    /// FFmpeg, and pulling it in for what amounts to a scale-and-transpose is not worth the binary
    /// size; the formats the enabled decoders actually emit are all covered here.
    /// </remarks>
    private void convertFrame()
    {
        int samples = frame->nb_samples;
        int channels = frame->ch_layout.nb_channels;

        if (samples <= 0 || channels <= 0)
        {
            pendingOffset = 0;
            pendingCount = 0;
            return;
        }

        // Channel count can in principle change mid-stream; downstream is built around a fixed
        // format, so fold to what was advertised rather than emitting a differently shaped block.
        if (channels != Channels)
            channels = Math.Min(channels, Channels);

        int total = samples * Channels;

        if (pending.Length < total)
            pending = new float[total];

        pendingOffset = 0;
        pendingCount = total;

        var format = (AVSampleFormat)frame->format;
        bool planar = ffmpeg.av_sample_fmt_is_planar(format) != 0;
        var output = pending.AsSpan(0, total);

        // Any channel the frame does not carry stays silent rather than repeating another channel.
        if (channels < Channels)
            output.Clear();

        for (int ch = 0; ch < channels; ch++)
        {
            // extended_data rather than data[]: the latter only covers the first 8 planes.
            byte* plane = planar ? frame->extended_data[ch] : frame->extended_data[0];
            int stride = planar ? 1 : channels;
            int start = planar ? 0 : ch;

            switch (format)
            {
                case AVSampleFormat.AV_SAMPLE_FMT_U8:
                case AVSampleFormat.AV_SAMPLE_FMT_U8P:
                    for (int i = 0; i < samples; i++)
                        output[i * Channels + ch] = (plane[start + i * stride] - 128) / 128f;
                    break;

                case AVSampleFormat.AV_SAMPLE_FMT_S16:
                case AVSampleFormat.AV_SAMPLE_FMT_S16P:
                {
                    short* data = (short*)plane;
                    for (int i = 0; i < samples; i++)
                        output[i * Channels + ch] = data[start + i * stride] / 32768f;
                    break;
                }

                case AVSampleFormat.AV_SAMPLE_FMT_S32:
                case AVSampleFormat.AV_SAMPLE_FMT_S32P:
                {
                    int* data = (int*)plane;
                    for (int i = 0; i < samples; i++)
                        output[i * Channels + ch] = data[start + i * stride] / 2147483648f;
                    break;
                }

                case AVSampleFormat.AV_SAMPLE_FMT_S64:
                case AVSampleFormat.AV_SAMPLE_FMT_S64P:
                {
                    long* data = (long*)plane;
                    for (int i = 0; i < samples; i++)
                        output[i * Channels + ch] = (float)(data[start + i * stride] / 9223372036854775808.0);
                    break;
                }

                case AVSampleFormat.AV_SAMPLE_FMT_FLT:
                case AVSampleFormat.AV_SAMPLE_FMT_FLTP:
                {
                    float* data = (float*)plane;
                    for (int i = 0; i < samples; i++)
                        output[i * Channels + ch] = data[start + i * stride];
                    break;
                }

                case AVSampleFormat.AV_SAMPLE_FMT_DBL:
                case AVSampleFormat.AV_SAMPLE_FMT_DBLP:
                {
                    double* data = (double*)plane;
                    for (int i = 0; i < samples; i++)
                        output[i * Channels + ch] = (float)data[start + i * stride];
                    break;
                }

                default:
                    Logger.Error($"[FFmpegAudioDecoder] Unsupported sample format {format}; emitting silence.");
                    output.Clear();
                    return;
            }
        }
    }

    /// <summary>
    /// Seeks to <paramref name="milliseconds"/> and discards any buffered output, so the next
    /// <see cref="Read"/> returns audio from the new position.
    /// </summary>
    /// <remarks>
    /// Lands on the keyframe at or before the requested time; for compressed formats the true
    /// position may be slightly earlier. Callers needing sample accuracy must discard the difference.
    /// </remarks>
    public void Seek(double milliseconds)
    {
        if (!CanSeek || formatContext == null || codecContext == null)
            return;

        long timestamp = (long)(Math.Max(0, milliseconds) / 1000.0 / timeBaseInSeconds);

        int result = ffmpeg.av_seek_frame(formatContext, audioStreamIndex, timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD);

        if (result < 0)
        {
            Logger.Error($"[FFmpegAudioDecoder] Seek to {milliseconds}ms failed: {errorString(result)}");
            return;
        }

        ffmpeg.avcodec_flush_buffers(codecContext);

        pendingOffset = 0;
        pendingCount = 0;
        flushed = false;
        EndOfStream = false;
    }

    private static int readPacket(void* opaque, byte* buffer, int bufferSize)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)opaque);

        if (!handle.IsAllocated || handle.Target is not FFmpegAudioDecoder decoder || decoder.source == null)
            return ffmpeg.AVERROR_EOF;

        int read = decoder.source.Read(new Span<byte>(buffer, bufferSize));
        return read == 0 ? ffmpeg.AVERROR_EOF : read;
    }

    private static long seekStream(void* opaque, long offset, int whence)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)opaque);

        if (!handle.IsAllocated || handle.Target is not FFmpegAudioDecoder decoder || decoder.source?.CanSeek != true)
            return -1;

        return whence switch
        {
            0 => decoder.source.Seek(offset, SeekOrigin.Begin),
            1 => decoder.source.Seek(offset, SeekOrigin.Current),
            2 => decoder.source.Seek(offset, SeekOrigin.End),
            // AVSEEK_SIZE — report the total length rather than moving.
            0x10000 => decoder.source.Length,
            _ => -1
        };
    }

    private static string errorString(int error)
    {
        const int buffer_size = 256;
        byte* buffer = stackalloc byte[buffer_size];
        ffmpeg.av_strerror(error, buffer, buffer_size);
        return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? error.ToString();
    }

    private bool isDisposed;

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        if (frame != null)
        {
            fixed (AVFrame** p = &frame)
                ffmpeg.av_frame_free(p);
        }

        if (packet != null)
        {
            fixed (AVPacket** p = &packet)
                ffmpeg.av_packet_free(p);
        }

        if (codecContext != null)
        {
            fixed (AVCodecContext** p = &codecContext)
                ffmpeg.avcodec_free_context(p);
        }

        if (formatContext != null && inputOpened)
        {
            fixed (AVFormatContext** p = &formatContext)
                ffmpeg.avformat_close_input(p);
        }
        else if (formatContext != null)
        {
            // open_input never took ownership, so the context (and the AVIO buffer it points at)
            // is still ours to free.
            ffmpeg.avformat_free_context(formatContext);
            formatContext = null;
        }

        if (ioContext != null)
        {
            ffmpeg.av_freep(&ioContext->buffer);
            fixed (AVIOContext** p = &ioContext)
                ffmpeg.avio_context_free(p);
        }

        source?.Dispose();
        source = null;

        readCallback = null;
        seekCallback = null;

        if (selfHandle.IsAllocated)
            selfHandle.Free();
    }
}
