// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Silk.NET.OpenGL;
using Texture = Sakura.Framework.Graphics.Textures.Texture;

namespace Sakura.Framework.Graphics.Video;

public unsafe class VideoDecoder : IDisposable
{
    public double Duration { get; private set; }
    public int Width => codecContext != null ? codecContext->width : 0;
    public int Height => codecContext != null ? codecContext->height : 0;
    public bool IsRunning => State == DecoderState.Running;
    public bool IsFaulted => State == DecoderState.Faulted;
    public bool CanSeek => videoStream?.CanSeek == true;
    public float LastDecodedFrameTime => lastDecodedFrameTime;
    public DecoderState State { get; private set; } = DecoderState.Ready;
    public bool Looping { get; set; }

    private Stream? videoStream;
    private AVFormatContext* formatContext;
    private AVIOContext* ioContext;
    private AVStream* avStream;
    private AVCodecContext* codecContext;
    private SwsContext* swsContext;
    private int videoStreamIndex = -1;
    private double timeBaseInSeconds;

    private avio_alloc_context_read_packet? readPacketCallback;
    private avio_alloc_context_seek? seekCallback;
    private GCHandle selfHandle;
    private bool inputOpened;

    private const int io_buffer_size = 4096;
    private const int max_pending_frames = 8;

    private volatile float lastDecodedFrameTime;
    private double? skipOutputUntilTime;

    /// <summary>
    /// Counts frames that have been scheduled for GPU upload but not yet enqueued
    /// into decodedFrames. The decode loop throttles on (decodedFrames + pendingUploads).
    /// </summary>
    private int pendingUploads;

    private Task? decodeTask;
    private CancellationTokenSource? cts;
    private readonly ConcurrentQueue<Action> decoderCommands = new ConcurrentQueue<Action>();

    /// <summary>
    /// Frames ready for <see cref="VideoSprite"/> to consume (update thread reads this)
    /// </summary>
    private readonly ConcurrentQueue<DecodedFrame> decodedFrames = new ConcurrentQueue<DecodedFrame>();

    /// <summary>
    /// Textures returned by <see cref="VideoSprite"/> after display (reused for next frames)
    /// </summary>
    private readonly ConcurrentQueue<VideoGLTexture> availableTextures = new ConcurrentQueue<VideoGLTexture>();

    private readonly ConcurrentQueue<FFmpegFrame> hwTransferFrames = new ConcurrentQueue<FFmpegFrame>();
    private readonly ConcurrentQueue<FFmpegFrame> scalerFrames = new ConcurrentQueue<FFmpegFrame>();

    private void returnHwTransferFrame(FFmpegFrame frame) => hwTransferFrames.Enqueue(frame);
    private void returnScalerFrame(FFmpegFrame frame) => scalerFrames.Enqueue(frame);

    private readonly IRenderer renderer;
    private readonly GL gl;

    static VideoDecoder()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ffmpeg.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "osx", "native");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            ffmpeg.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", arch, "native");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
            ffmpeg.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", arch, "native");
        }

        Logger.Debug($"[FFmpeg] RootPath: {ffmpeg.RootPath}");
        DynamicallyLoadedBindings.Initialize();
    }

    public VideoDecoder(IRenderer renderer, GL gl, string filePath)
        : this(renderer, gl, File.OpenRead(filePath)) { }

    public VideoDecoder(IRenderer renderer, GL gl, Stream stream)
    {
        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));

        this.renderer = renderer;
        this.gl = gl;
        videoStream = stream;
        selfHandle = GCHandle.Alloc(this);
    }

    public void Start()
    {
        prepareDecoding();
        cts = new CancellationTokenSource();
        decodeTask = Task.Factory.StartNew(
            () => decodeLoop(cts.Token),
            cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );
    }

    public void Seek(double targetMs)
    {
        if (!CanSeek)
            throw new InvalidOperationException("Underlying stream does not support seeking.");

        decoderCommands.Enqueue(() =>
        {
            ffmpeg.avcodec_flush_buffers(codecContext);
            long ts = (long)(targetMs / timeBaseInSeconds / 1000.0);
            ffmpeg.av_seek_frame(formatContext, avStream->index, ts, ffmpeg.AVSEEK_FLAG_BACKWARD);
            skipOutputUntilTime = targetMs;
            State = DecoderState.Ready;
        });
    }

    /// <summary>
    /// Returns consumed frames back so their GPU textures can be reused.
    /// Call from the update thread after you're done displaying a frame.
    /// </summary>
    public void ReturnFrames(IEnumerable<DecodedFrame> frames)
    {
        foreach (var f in frames)
        {
            if (f.Texture.VideoGlTexture != null)
                availableTextures.Enqueue(f.Texture.VideoGlTexture);
        }
    }

    /// <summary>
    /// Drains all frames that have been decoded since the last call.
    /// Called from the update thread (VideoSprite.Update).
    /// </summary>
    public IEnumerable<DecodedFrame> GetDecodedFrames()
    {
        var list = new List<DecodedFrame>(decodedFrames.Count);
        while (decodedFrames.TryDequeue(out var f))
            list.Add(f);
        return list;
    }

    /// <summary>
    /// Returns the column-major 3x3 YUV→RGB matrix for the video shader.
    /// </summary>
    public float[] GetConversionMatrix()
    {
        if (codecContext == null) return rec601_matrix;

        bool useHdtv = codecContext->colorspace == AVColorSpace.AVCOL_SPC_BT709
                    || (codecContext->colorspace == AVColorSpace.AVCOL_SPC_UNSPECIFIED
                        && (codecContext->width >= 704 || codecContext->height >= 576));
        return useHdtv ? rec709_matrix : rec601_matrix;
    }

    // Rec.709 (HDTV) — column-major for mat3 uniform
    private static readonly float[] rec709_matrix =
    {
        1.164f,  1.164f, 1.164f,
        0.000f, -0.213f, 2.112f,
        1.793f, -0.533f, 0.000f
    };

    // Rec.601 (SDTV)
    private static readonly float[] rec601_matrix =
    {
        1.164f,  1.164f, 1.164f,
        0.000f, -0.392f, 2.017f,
        1.596f, -0.813f, 0.000f
    };

    private static int readPacket(void* opaque, byte* buf, int bufSize)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)opaque);
        if (!handle.IsAllocated || handle.Target is not VideoDecoder d) return ffmpeg.AVERROR_EOF;
        int read = d.videoStream!.Read(new Span<byte>(buf, bufSize));
        return read == 0 ? ffmpeg.AVERROR_EOF : read;
    }

    private static long seekStream(void* opaque, long offset, int whence)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)opaque);
        if (!handle.IsAllocated || handle.Target is not VideoDecoder d || !d.videoStream!.CanSeek)
            return -1;

        return whence switch
        {
            0       => d.videoStream.Seek(offset, SeekOrigin.Begin),
            1       => d.videoStream.Seek(offset, SeekOrigin.Current),
            2       => d.videoStream.Seek(offset, SeekOrigin.End),
            0x10000 => d.videoStream.Length,   // AVSEEK_SIZE
            _       => -1
        };
    }

    // ── Init ────────────────────────────────────────────────────────────────────

    private void prepareDecoding()
    {
        readPacketCallback = readPacket;
        seekCallback = videoStream!.CanSeek ? seekStream : null;

        byte* ioBuf = (byte*)ffmpeg.av_malloc(io_buffer_size);
        ioContext = ffmpeg.avio_alloc_context(
            ioBuf, io_buffer_size, 0,
            (void*)GCHandle.ToIntPtr(selfHandle),
            readPacketCallback, null, seekCallback);

        var fc = ffmpeg.avformat_alloc_context();
        fc->pb = ioContext;
        fc->flags |= ffmpeg.AVFMT_FLAG_GENPTS;

        int openResult = ffmpeg.avformat_open_input(&fc, "pipe:", null, null);
        if (openResult < 0)
            throw new Exception($"avformat_open_input failed: {openResult}");

        inputOpened = true;
        formatContext = fc;

        if (ffmpeg.avformat_find_stream_info(formatContext, null) < 0)
            throw new Exception("Could not find stream info.");

        AVCodec* codec = null;
        videoStreamIndex = ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
        if (videoStreamIndex < 0)
            throw new Exception("No video stream found.");

        avStream = formatContext->streams[videoStreamIndex];
        timeBaseInSeconds = avStream->time_base.num / (double)avStream->time_base.den;

        Duration = avStream->duration > 0
            ? avStream->duration * timeBaseInSeconds * 1000.0
            : formatContext->duration / (double)ffmpeg.AV_TIME_BASE * 1000.0;

        codecContext = ffmpeg.avcodec_alloc_context3(codec);
        codecContext->pkt_timebase = avStream->time_base;
        ffmpeg.avcodec_parameters_to_context(codecContext, avStream->codecpar);

        if (ffmpeg.avcodec_open2(codecContext, codec, null) < 0)
            throw new Exception("Could not open codec.");
    }

    private void decodeLoop(CancellationToken ct)
    {
        var packet = ffmpeg.av_packet_alloc();
        var receiveFrame = ffmpeg.av_frame_alloc();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                switch (State)
                {
                    case DecoderState.Ready:
                    case DecoderState.Running:
                        // Count both decoded frames waiting to be consumed and frames
                        // scheduled for GPU upload but not yet in decodedFrames.
                        if (decodedFrames.Count + pendingUploads < max_pending_frames)
                            decodeNextFrame(packet, receiveFrame);
                        else
                        {
                            State = DecoderState.Ready;
                            Thread.Sleep(1);
                        }
                        break;

                    case DecoderState.EndOfStream:
                        Thread.Sleep(50);
                        break;

                    default:
                        return;
                }

                while (decoderCommands.TryDequeue(out var cmd))
                {
                    if (ct.IsCancellationRequested) return;
                    cmd();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("VideoDecoder faulted", ex);
            State = DecoderState.Faulted;
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            ffmpeg.av_frame_free(&receiveFrame);
            if (State != DecoderState.Faulted)
                State = DecoderState.Stopped;
        }
    }

    private void decodeNextFrame(AVPacket* packet, AVFrame* receiveFrame)
    {
        int readResult = 0;
        if (packet->buf == null)
            readResult = ffmpeg.av_read_frame(formatContext, packet);

        if (readResult >= 0)
        {
            State = DecoderState.Running;
            bool unref = true;

            if (packet->stream_index == videoStreamIndex)
            {
                int sendResult = sendPacket(receiveFrame, packet);
                if (sendResult == -ffmpeg.EAGAIN) unref = false;
            }

            if (unref) ffmpeg.av_packet_unref(packet);
        }
        else if (readResult == ffmpeg.AVERROR_EOF)
        {
            sendPacket(receiveFrame, null);
            if (Looping)
                Seek(0);
            else
                State = DecoderState.EndOfStream;
        }
        else if (readResult == -ffmpeg.EAGAIN)
        {
            State = DecoderState.Ready;
            Thread.Sleep(1);
        }
        else
        {
            Logger.Warning($"[VideoDecoder] av_read_frame error: {readResult}");
            Thread.Sleep(1);
        }
    }

    private int sendPacket(AVFrame* receiveFrame, AVPacket* packet)
    {
        int result = ffmpeg.avcodec_send_packet(codecContext, packet);
        if (result == 0 || result == -ffmpeg.EAGAIN)
            readDecodedFrames(receiveFrame);
        else
            Logger.Warning($"[VideoDecoder] avcodec_send_packet error: {result}");
        return result;
    }

    private void readDecodedFrames(AVFrame* receiveFrame)
    {
        while (true)
        {
            int result = ffmpeg.avcodec_receive_frame(codecContext, receiveFrame);
            if (result < 0) break;

            long ts = receiveFrame->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE
                ? receiveFrame->best_effort_timestamp
                : receiveFrame->pts;

            // Guard against AV_NOPTS_VALUE start_time (common for many container formats)
            long startTime = avStream->start_time != ffmpeg.AV_NOPTS_VALUE ? avStream->start_time : 0;
            double frameTime = (ts - startTime) * timeBaseInSeconds * 1000.0;

            if (skipOutputUntilTime.HasValue)
            {
                if (frameTime < skipOutputUntilTime.Value) continue;
                skipOutputUntilTime = null;
            }

            // Resolve pixel format — transfer from HW memory if needed
            FFmpegFrame frame;

            if (((AVPixelFormat)receiveFrame->format).IsHardwareFormat())
            {
                if (!hwTransferFrames.TryDequeue(out var hwFrame))
                    hwFrame = new FFmpegFrame(returnHwTransferFrame);

                int transferResult = ffmpeg.av_hwframe_transfer_data(hwFrame.Pointer, receiveFrame, 0);
                if (transferResult < 0)
                {
                    Logger.Warning($"[VideoDecoder] HW frame transfer failed: {transferResult}");
                    hwFrame.Return();
                    continue;
                }

                frame = hwFrame;
            }
            else
            {
                frame = new FFmpegFrame();
                ffmpeg.av_frame_move_ref(frame.Pointer, receiveFrame);
            }

            lastDecodedFrameTime = (float)frameTime;

            frame = ensureYuv420P(frame);
            if (frame == null) continue;

            // Capture for closure
            var capturedFrame = frame;
            var capturedTime = frameTime;
            var capturedGl = gl;

            // Try to grab a pooled texture. If none available, create one on the draw thread.
            availableTextures.TryDequeue(out var pooledTex);

            Interlocked.Increment(ref pendingUploads);
            renderer.ScheduleToDrawThread(() =>
            {
                var yuvTex = pooledTex ?? new VideoGLTexture(capturedGl, capturedFrame.Pointer->width, capturedFrame.Pointer->height);

                var upload = new VideoTextureUpload(capturedFrame);
                upload.Upload(capturedGl, yuvTex);
                upload.Dispose(); // returns capturedFrame to pool

                var texture = new Texture(yuvTex);
                decodedFrames.Enqueue(new DecodedFrame { Time = capturedTime, Texture = texture });
                Interlocked.Decrement(ref pendingUploads);
            });
        }
    }

    private FFmpegFrame ensureYuv420P(FFmpegFrame frame)
    {
        const AVPixelFormat target = AVPixelFormat.AV_PIX_FMT_YUV420P;
        if (frame.PixelFormat == target) return frame;

        int w = frame.Pointer->width, h = frame.Pointer->height;

        swsContext = ffmpeg.sws_getCachedContext(
            swsContext, w, h, frame.PixelFormat,
            w, h, target,
            4, null, null, null); // 4 = SWS_BILINEAR

        if (!scalerFrames.TryDequeue(out var scaled))
            scaled = new FFmpegFrame(returnScalerFrame);

        if (scaled.PixelFormat != target || scaled.Pointer->width != w || scaled.Pointer->height != h)
        {
            ffmpeg.av_frame_unref(scaled.Pointer);
            scaled.PixelFormat = target;
            scaled.Pointer->width = w;
            scaled.Pointer->height = h;

            if (ffmpeg.av_frame_get_buffer(scaled.Pointer, 0) < 0)
            {
                Logger.Warning("[VideoDecoder] Failed to allocate scaler frame buffer.");
                scaled.Dispose();
                frame.Return();
                return null!;
            }
        }

        int scaleResult = ffmpeg.sws_scale(
            swsContext,
            frame.Pointer->data, frame.Pointer->linesize, 0, h,
            scaled.Pointer->data, scaled.Pointer->linesize);

        frame.Return();

        if (scaleResult < 0)
        {
            Logger.Warning($"[VideoDecoder] sws_scale failed: {scaleResult}");
            scaled.Dispose();
            return null!;
        }

        return scaled;
    }

    /// <summary>
    /// State of the decoder
    /// </summary>
    public enum DecoderState
    {
        Ready = 0,
        Running = 1,
        Faulted = 2,
        EndOfStream = 3,
        Stopped = 4,
    }

    private bool isDisposed;

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        decoderCommands.Clear();
        cts?.Cancel();
        decodeTask?.Wait();

        if (formatContext != null && inputOpened)
        {
            fixed (AVFormatContext** p = &formatContext)
                ffmpeg.avformat_close_input(p);
        }

        if (ioContext != null)
        {
            ffmpeg.av_freep(&ioContext->buffer);
            fixed (AVIOContext** p = &ioContext)
                ffmpeg.avio_context_free(p);
        }

        if (codecContext != null)
        {
            fixed (AVCodecContext** p = &codecContext)
                ffmpeg.avcodec_free_context(p);
        }

        if (swsContext != null)
            ffmpeg.sws_freeContext(swsContext);

        // Drain decodedFrames and return their textures for disposal
        while (decodedFrames.TryDequeue(out var f))
        {
            if (f.Texture.VideoGlTexture != null)
                availableTextures.Enqueue(f.Texture.VideoGlTexture);
        }

        // Dispose all pooled GL textures on the draw thread
        var texturesToDispose = new List<VideoGLTexture>();
        while (availableTextures.TryDequeue(out var t))
            texturesToDispose.Add(t);

        if (texturesToDispose.Count > 0)
        {
            renderer.ScheduleToDrawThread(() =>
            {
                foreach (var t in texturesToDispose)
                    t.Dispose();
            });
        }

        while (hwTransferFrames.TryDequeue(out var hf))
            hf.Dispose();
        while (scalerFrames.TryDequeue(out var sf))
            sf.Dispose();

        videoStream?.Dispose();
        videoStream = null;

        if (selfHandle.IsAllocated) selfHandle.Free();

        GC.SuppressFinalize(this);
    }
}

internal static class AvPixelFormatExtensions
{
    public static bool IsHardwareFormat(this AVPixelFormat fmt) => fmt switch
    {
        AVPixelFormat.AV_PIX_FMT_VDPAU         => true,
        AVPixelFormat.AV_PIX_FMT_CUDA           => true,
        AVPixelFormat.AV_PIX_FMT_VAAPI          => true,
        AVPixelFormat.AV_PIX_FMT_DXVA2_VLD      => true,
        AVPixelFormat.AV_PIX_FMT_D3D11          => true,
        AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX   => true,
        AVPixelFormat.AV_PIX_FMT_MEDIACODEC     => true,
        _                                       => false,
    };
}
