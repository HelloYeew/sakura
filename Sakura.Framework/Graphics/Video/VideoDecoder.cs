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
using Sakura.Framework.Reactive;
using Sakura.Framework.Statistic;
using Silk.NET.OpenGL;
using Texture = Sakura.Framework.Graphics.Textures.Texture;

namespace Sakura.Framework.Graphics.Video;

public unsafe class VideoDecoder : IDisposable
{
    public double Duration { get; private set; }
    public int Width  => codecContext != null ? codecContext->width  : 0;
    public int Height => codecContext != null ? codecContext->height : 0;
    public bool IsRunning => State == DecoderState.Running;
    public bool IsFaulted => State == DecoderState.Faulted;
    public bool CanSeek => videoStream?.CanSeek == true;
    public float LastDecodedFrameTime => lastDecodedFrameTime;
    public DecoderState State { get; private set; } = DecoderState.Ready;
    public bool Looping { get; set; }

    public readonly Reactive<bool> HardwareAcceleration = new Reactive<bool>(true);

    /// <summary>
    /// The hardware device type that was successfully initialised, or
    /// <see cref="AVHWDeviceType.AV_HWDEVICE_TYPE_NONE"/> when running on software.
    /// Updated each time the codec is (re)opened.
    /// </summary>
    public AVHWDeviceType ActiveHardwareDevice { get; private set; } = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

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
    private bool texturePoolWarmed;

    private const int io_buffer_size = 4096;
    private const int max_pending_frames = 4;

    private readonly ConcurrentQueue<DecodedFrame> decodedFrames = new ConcurrentQueue<DecodedFrame>();
    private readonly ConcurrentQueue<VideoTexture> availableTextures = new ConcurrentQueue<VideoTexture>();

    private readonly ConcurrentQueue<FFmpegFrame> hwTransferFrames = new ConcurrentQueue<FFmpegFrame>();
    private readonly ConcurrentQueue<FFmpegFrame> scalerFrames = new ConcurrentQueue<FFmpegFrame>();

    private void returnHwTransferFrame(FFmpegFrame f) => hwTransferFrames.Enqueue(f);
    private void returnScalerFrame(FFmpegFrame f) => scalerFrames.Enqueue(f);

    private volatile float lastDecodedFrameTime;
    private double? skipOutputUntilTime;

    private Task? decodeTask;
    private CancellationTokenSource? cts;
    private readonly ConcurrentQueue<Action> decoderCommands = new();

    private readonly IRenderer renderer;
    private readonly GL gl;
    private readonly ITextureManager textureManager;

    static VideoDecoder()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            ffmpeg.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "osx", "native");
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

        Logger.Verbose($"Initialized FFmpeg with root path {ffmpeg.RootPath}");
        DynamicallyLoadedBindings.Initialize();
    }

    public VideoDecoder(IRenderer renderer, GL gl, ITextureManager textureManager, string filePath)
        : this(renderer, gl, textureManager, File.OpenRead(filePath)) { }

    public VideoDecoder(IRenderer renderer, GL gl, ITextureManager textureManager, Stream stream)
    {
        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));

        this.renderer = renderer;
        this.gl = gl;
        this.textureManager = textureManager;
        videoStream = stream;
        selfHandle = GCHandle.Alloc(this);
    }

    public void Start()
    {
        prepareDecoding();
        texturePoolWarmed = false;

        // Subscribe to live hardware acceleration toggle.
        // The handler enqueues a codec-recreation command into the decode loop's
        // command queue so it is applied safely between frames — never mid-decode.
        HardwareAcceleration.ValueChanged += onHardwareAccelerationChanged;

        cts = new CancellationTokenSource();
        decodeTask = Task.Factory.StartNew(
            () => decodeLoop(cts.Token),
            cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void onHardwareAccelerationChanged(ValueChangedEvent<bool> e)
    {
        // Called from whatever thread changed the Reactive — could be the update thread
        // (user toggling a setting) or the main thread (config load).
        // Route through decoderCommands so it runs safely in the decode loop.
        decoderCommands.Enqueue(recreateCodecContext);
    }

    /// <summary>
    /// Closes and reopens the codec context, respecting the current
    /// <see cref="HardwareAcceleration"/> value. Runs on the decode thread
    /// (called from the <see cref="decoderCommands"/> queue).
    /// Flushes pending decoded frames and resets the pool warm-up flag so
    /// new textures are created for the (possibly different) pixel format.
    /// </summary>
    private void recreateCodecContext()
    {
        // Close the existing codec context (releases HW device context too)
        if (codecContext != null)
        {
            fixed (AVCodecContext** p = &codecContext)
                ffmpeg.avcodec_free_context(p);
            codecContext = null;
        }

        // Flush buffered frames — they may have been decoded with the old codec config
        while (decodedFrames.TryDequeue(out var staleFrame))
            availableTextures.Enqueue(staleFrame.NativeTexture);

        // Re-open with current HardwareAcceleration.Value
        AVCodec* codec = null;
        ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
        if (codec != null)
        {
            openCodec(codec);
            Logger.Verbose($"[VideoDecoder] Codec recreated — HW={HardwareAcceleration.Value}, device={ActiveHardwareDevice}");
        }

        // Reset pool warm-up so next frame triggers texture recreation if needed
        texturePoolWarmed = false;
        State = DecoderState.Ready;
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
    /// Returns consumed frames back so their <see cref="VideoTexture"/> instances can be reused.
    /// Call from the update thread after finishing with a frame.
    /// </summary>
    public void ReturnFrames(IEnumerable<DecodedFrame> frames)
    {
        foreach (var f in frames)
        {
            f.NativeTexture.Reset();
            availableTextures.Enqueue(f.NativeTexture);
        }
    }

    /// <summary>
    /// Drains all frames decoded since the last call. Called from the update thread.
    /// </summary>
    public IEnumerable<DecodedFrame> GetDecodedFrames()
    {
        var list = new List<DecodedFrame>(decodedFrames.Count);
        while (decodedFrames.TryDequeue(out var f))
            list.Add(f);
        return list;
    }

    public float[] GetConversionMatrix()
    {
        if (codecContext == null) return rec601_matrix;

        bool useHdtv = codecContext->colorspace == AVColorSpace.AVCOL_SPC_BT709
                    || (codecContext->colorspace == AVColorSpace.AVCOL_SPC_UNSPECIFIED
                        && (codecContext->width >= 704 || codecContext->height >= 576));
        return useHdtv ? rec709_matrix : rec601_matrix;
    }

    private static readonly float[] rec709_matrix =
    {
        1.164f,  1.164f, 1.164f,
        0.000f, -0.213f, 2.112f,
        1.793f, -0.533f, 0.000f
    };

    private static readonly float[] rec601_matrix =
    {
        1.164f,  1.164f, 1.164f,
        0.000f, -0.392f, 2.017f,
        1.596f, -0.813f, 0.000f
    };

    private static int readPacket(void* opaque, byte* buf, int bufSize)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)opaque);
        if (!handle.IsAllocated || handle.Target is not VideoDecoder d)
            return ffmpeg.AVERROR_EOF;
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
            0 => d.videoStream.Seek(offset, SeekOrigin.Begin),
            1 => d.videoStream.Seek(offset, SeekOrigin.Current),
            2 => d.videoStream.Seek(offset, SeekOrigin.End),
            0x10000 => d.videoStream.Length,
            _ => -1
        };
    }

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

        openCodec(codec);
    }

    /// <summary>
    /// Opens the codec context, trying hardware-accelerated decoders first (if
    /// <see cref="AllowHardwareAcceleration"/> is true) and falling back to the
    /// software decoder automatically on any failure.
    ///
    /// Hardware device priority:
    ///   macOS  — VideoToolbox
    ///   Windows — D3D11VA > DXVA2 > CUDA
    ///   Linux   — VAAPI > VDPAU > CUDA
    ///
    /// If every HW attempt fails, or if <see cref="AllowHardwareAcceleration"/> is
    /// false, the plain software codec is opened instead.
    /// <see cref="ActiveHardwareDevice"/> reflects the final outcome.
    /// </summary>
    private void openCodec(AVCodec* codec)
    {
        if (HardwareAcceleration.Value)
        {
            // Platform-preferred HW device types, tried in order.
            AVHWDeviceType[] candidates;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                candidates = new[] { AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX };
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                candidates = new[]
                {
                    AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                    AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
                    AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
                };
            else
                candidates = new[]
                {
                    AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
                    AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU,
                    AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
                };

            foreach (var hwType in candidates)
            {
                if (tryOpenCodecWithHardware(codec, hwType))
                {
                    ActiveHardwareDevice = hwType;
                    Logger.Verbose($"[VideoDecoder] Hardware decoding active: {hwType}");
                    GlobalStatistics.Get<string>("Video", "HW Decoder").Value = hwType.ToString().Replace("AV_HWDEVICE_TYPE_", "");
                    return;
                }
            }

            Logger.Verbose("[VideoDecoder] No hardware decoder available, falling back to software.");
        }

        // Software fallback (or AllowHardwareAcceleration == false)
        openCodecSoftware(codec);
    }

    private bool tryOpenCodecWithHardware(AVCodec* codec, AVHWDeviceType hwType)
    {
        // Check whether this codec supports the requested HW device type
        bool supported = false;
        for (int i = 0; ; i++)
        {
            var hwConfig = ffmpeg.avcodec_get_hw_config(codec, i);
            if (hwConfig == null) break;

            // AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01
            if ((hwConfig->methods & 0x01) != 0 && hwConfig->device_type == hwType)
            {
                supported = true;
                break;
            }
        }

        if (!supported)
            return false;

        var ctx = ffmpeg.avcodec_alloc_context3(codec);
        if (ctx == null) return false;

        ctx->pkt_timebase = avStream->time_base;
        ffmpeg.avcodec_parameters_to_context(ctx, avStream->codecpar);

        // Create the hardware device context
        AVBufferRef* hwDeviceCtx = null;
        int hwResult = ffmpeg.av_hwdevice_ctx_create(&hwDeviceCtx, hwType, null, null, 0);
        if (hwResult < 0)
        {
            ffmpeg.avcodec_free_context(&ctx);
            Logger.Verbose($"[VideoDecoder] Failed to create HW device context for {hwType}: {hwResult}");
            return false;
        }

        // Transfer ownership of hwDeviceCtx to the codec context.
        // avcodec_free_context will free it — do not call av_buffer_unref separately.
        ctx->hw_device_ctx = hwDeviceCtx;

        if (ffmpeg.avcodec_open2(ctx, codec, null) < 0)
        {
            ffmpeg.avcodec_free_context(&ctx);
            Logger.Verbose($"[VideoDecoder] Failed to open codec with HW device {hwType}");
            return false;
        }

        codecContext = ctx;
        return true;
    }

    private void openCodecSoftware(AVCodec* codec)
    {
        codecContext = ffmpeg.avcodec_alloc_context3(codec);
        codecContext->pkt_timebase = avStream->time_base;
        ffmpeg.avcodec_parameters_to_context(codecContext, avStream->codecpar);

        if (ffmpeg.avcodec_open2(codecContext, codec, null) < 0)
            throw new Exception("Could not open software codec.");

        ActiveHardwareDevice = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
        Logger.Verbose("[VideoDecoder] Software decoding active.");
        GlobalStatistics.Get<string>("Video", "HW Decoder").Value = "Software";
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
                        GlobalStatistics.Get<int>("Video", "Pool Available").Value = availableTextures.Count;
                        GlobalStatistics.Get<int>("Video", "Pending Frames").Value = decodedFrames.Count;
                        if (!texturePoolWarmed || !availableTextures.IsEmpty)
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
            if (Looping) Seek(0);
            else State = DecoderState.EndOfStream;
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

            int width  = frame.Pointer->width;
            int height = frame.Pointer->height;

            // Schedule pool warm-up on the draw thread on the very first frame.
            // texturePoolWarmed is only scheduled once; the decode loop then polls
            // availableTextures until textures arrive (typically within one draw frame ~4ms).
            if (!texturePoolWarmed)
            {
                texturePoolWarmed = true;
                var capturedW = width;
                var capturedH = height;
                renderer.ScheduleToDrawThread(() =>
                {
                    while (availableTextures.Count < max_pending_frames)
                        availableTextures.Enqueue(new VideoTexture(renderer, gl, textureManager, capturedW, capturedH));
                });
            }

            // Return this frame and sleep until the pool has textures.
            if (availableTextures.IsEmpty)
            {
                frame.Return();
                GlobalStatistics.Get<int>("Video", "Frames Dropped (Pool Full)").Value++;
                Thread.Sleep(1);
                continue;
            }

            if (!availableTextures.TryDequeue(out var tex))
            {
                frame.Return();
                GlobalStatistics.Get<int>("Video", "Frames Dropped (Pool Full)").Value++;
                Thread.Sleep(1);
                continue;
            }

            var upload = new VideoTextureUpload(frame);
            tex.SetData(upload);

            // Texture is a dimension-only proxy — no GL handles, no Video namespace import needed.
            // VideoSprite reads NativeTexture directly for rendering.
            var texture = new Texture(width, height);
            decodedFrames.Enqueue(new DecodedFrame { Time = frameTime, Texture = texture, NativeTexture = tex });
            GlobalStatistics.Get<int>("Video", "Frames Decoded").Value++;
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

        HardwareAcceleration.ValueChanged -= onHardwareAccelerationChanged;
        HardwareAcceleration.UnbindAll();

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

        // Return all decodedFrames textures to the pool, then dispose them all.
        while (decodedFrames.TryDequeue(out var frame))
            availableTextures.Enqueue(frame.NativeTexture);

        while (availableTextures.TryDequeue(out var vt))
            vt.Dispose(); // unregisters from textureManager, schedules GL delete

        while (hwTransferFrames.TryDequeue(out var hf)) hf.Dispose();
        while (scalerFrames.TryDequeue(out var sf)) sf.Dispose();

        videoStream?.Dispose();
        videoStream = null;

        if (selfHandle.IsAllocated)
            selfHandle.Free();
        GC.SuppressFinalize(this);
    }
}
