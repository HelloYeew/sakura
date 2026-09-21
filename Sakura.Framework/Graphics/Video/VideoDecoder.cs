// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Direct3D11;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Sakura.Framework.Statistic;
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

    /// <summary>
    /// Monotonically increasing seek counter. Incremented (atomically) the instant
    /// <see cref="Seek"/> is requested — on the caller's thread, before the decode thread
    /// processes the seek. Every <see cref="DecodedFrame"/> is stamped with the generation it
    /// was produced under, so consumers can discard frames that belong to a position we have
    /// since seeked away from. This is the single source of truth that keeps seek/reverse/loop
    /// from anchoring onto stale frames.
    /// </summary>
    public int SeekGeneration => Volatile.Read(ref seekGeneration);
    private int seekGeneration;

    public readonly Reactive<bool> HardwareAcceleration = new Reactive<bool>(true);

    /// <summary>
    /// Whether hardware-decoded frames may be sampled where the decoder produced them, skipping the
    /// readback and the upload entirely. Only has an effect where the active renderer supports it,
    /// Metal with VideoToolbox today; see <see cref="IRenderer.CanSampleHardwareFrame"/>.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="HardwareAcceleration"/> this needs no codec reopen: the frames are identical
    /// either way, and only their route to the GPU changes. Flipping it re-warms the texture pool on the
    /// next frame because the path is part of <see cref="VideoTextureShape"/>.
    /// <para>
    /// It stays switchable at runtime for two reasons. It is the kill switch if the interop misbehaves
    /// on hardware nobody has tried
    /// </para>
    /// </remarks>
    public readonly Reactive<bool> ZeroCopy = new Reactive<bool>(true);

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

    /// <summary>
    /// Held for the codec context's lifetime: FFmpeg keeps the raw function pointer, so letting the
    /// delegate be collected would leave it calling into freed memory.
    /// </summary>
    private AVCodecContext_get_format? getFormatCallback;

    private GCHandle selfHandle;
    private bool inputOpened;

    // The shape of the pool is currently warmed for, or null before the first frame. A frame that does not
    // match means the codec was reopened into a different format — toggling hardware acceleration does
    // exactly that, flipping between NV12 and YUV420P — or that zero-copy was toggled, so a fresh set is
    // warmed and the old textures are dropped as they come back (see tryEmitFrame). Frames already
    // queued for display keep working while that happens: the shader is picked from each texture's own
    // layout, so the two layouts coexist.
    private VideoTextureShape? poolShape;

    private const int io_buffer_size = 4096;

    /// <summary>
    /// How many textures the pool holds, on either path.
    /// </summary>
    private const int max_pending_frames = 6;

    private readonly ConcurrentQueue<DecodedFrame> decodedFrames = new ConcurrentQueue<DecodedFrame>();
    private readonly ConcurrentQueue<VideoTexture> availableTextures = new ConcurrentQueue<VideoTexture>();

    private readonly ConcurrentQueue<FFmpegFrame> hwTransferFrames = new ConcurrentQueue<FFmpegFrame>();
    private readonly ConcurrentQueue<FFmpegFrame> scalerFrames = new ConcurrentQueue<FFmpegFrame>();

    /// <summary>
    /// Frames that carry a zero-copy hardware reference rather than pixels. Pooled for the same reason
    /// as the others — the alternative is an <c>av_frame_alloc</c> per displayed frame on the thread
    /// that competes with audio decoding.
    /// </summary>
    private readonly ConcurrentQueue<FFmpegFrame> zeroCopyFrames = new ConcurrentQueue<FFmpegFrame>();

    private void returnHwTransferFrame(FFmpegFrame f) => hwTransferFrames.Enqueue(f);
    private void returnScalerFrame(FFmpegFrame f) => scalerFrames.Enqueue(f);
    private void returnZeroCopyFrame(FFmpegFrame f) => zeroCopyFrames.Enqueue(f);

    private volatile float lastDecodedFrameTime;
    private double? skipOutputUntilTime;

    // The generation stamped onto frames the decode thread is currently producing.
    // Set when a seek command runs; frames carry it so consumers can match against SeekGeneration.
    private int decodeGeneration;

    // Holds back the most recent decoded frame during a post-seek skip so we always have at
    // least one frame to emit even if every decoded frame is earlier than the seek target
    // (e.g. seek landed past the last keyframe). Returned to the pool when superseded.
    private FFmpegFrame? skipHeldFrame;
    private double skipHeldFrameTime;

    // A YUV420P frame that has been decoded+converted but could not be uploaded yet because the
    // texture pool was momentarily empty. Held here and retried on the next decode iteration so
    // the frame is never silently dropped (which previously caused missing/blank frames under
    // load). Stamped with the generation it was decoded under.
    private FFmpegFrame? pendingUploadFrame;
    private double pendingUploadFrameTime;
    private int pendingUploadFrameGeneration;

    // Time spent in avcodec_send_packet since the last frame came out, attributed to the next frame
    // that does. Decode-thread only. Without this the decode stage under-reports on software decoded,
    // where a sending can block on the codec's internal thread pool.
    private long pendingSendTicks;

    private Task? decodeTask;
    private CancellationTokenSource? cts;
    private readonly ConcurrentQueue<Action> decoderCommands = new ConcurrentQueue<Action>();

    private readonly IRenderer renderer;
    private readonly ITextureManager textureManager;

    static VideoDecoder() => FFmpegLibrary.EnsureInitialized();

    public VideoDecoder(IRenderer renderer, ITextureManager textureManager, string filePath)
        : this(renderer, textureManager, File.OpenRead(filePath)) { }

    public VideoDecoder(IRenderer renderer, ITextureManager textureManager, Stream stream)
    {
        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));

        this.renderer = renderer;
        this.textureManager = textureManager;
        videoStream = stream;
        selfHandle = GCHandle.Alloc(this);
    }

    public void Start()
    {
        State = DecoderState.Preparing;
        poolShape = null;

        HardwareAcceleration.ValueChanged += onHardwareAccelerationChanged;

        cts = new CancellationTokenSource();

        decodeTask = Task.Factory.StartNew(() =>
        {
            try
            {
                prepareDecoding();
                State = DecoderState.Ready;
            }
            catch (Exception ex)
            {
                Logger.Error($"[VideoDecoder] prepareDecoding failed: {ex}");
                State = DecoderState.Faulted;
                return;
            }

            decodeLoop(cts.Token);
        }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
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

        // Bump the generation so the sprite discards any in-flight frames from the old codec
        // instead of continuing to display textures we are about to recycle (avoids a flash of
        // a stale/blank frame while the codec is swapped).
        int generation = Interlocked.Increment(ref seekGeneration);
        decodeGeneration = generation;

        // Flush buffered frames — they may have been decoded with the old codec config.
        // Reset() clears each texture's pending upload before returning it to the pool.
        while (decodedFrames.TryDequeue(out var staleFrame))
        {
            staleFrame.NativeTexture.Reset();
            availableTextures.Enqueue(staleFrame.NativeTexture);
        }

        // Discard any held/pending native frames from the old codec config.
        skipHeldFrame?.Return();
        skipHeldFrame = null;
        pendingUploadFrame?.Return();
        pendingUploadFrame = null;
        skipOutputUntilTime = null;

        // Re-open with current HardwareAcceleration.Value
        AVCodec* codec = null;
        ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
        if (codec != null)
        {
            openCodec(codec);
            Logger.Verbose($"[VideoDecoder] Codec recreated — HW={HardwareAcceleration.Value}, device={ActiveHardwareDevice}");
        }

        // The pool is deliberately left alone: the next frame's size and layout decide whether it needs
        // rebuilding. Blanket-resetting the warm-up flag here would warm a second full set even when
        // the format did not change, and nothing ever retired the first.
        State = DecoderState.Ready;
    }

    public void Seek(double targetMs)
    {
        if (!CanSeek)
            throw new InvalidOperationException("Underlying stream does not support seeking.");

        // Bump the generation NOW, on the caller's thread, so any frame still sitting in the
        // queues (decoded before this call) is immediately recognisable as stale by consumers,
        // and so frames produced by this seek carry the new generation. We snapshot it for the
        // command closure rather than reading the shared field inside the decode thread.
        int generation = Interlocked.Increment(ref seekGeneration);

        decoderCommands.Enqueue(() =>
        {
            // A newer seek may have been queued after this one — if so, skip this stale seek
            // entirely so we don't seek backwards and forwards redundantly.
            if (generation != Volatile.Read(ref seekGeneration))
                return;

            ffmpeg.avcodec_flush_buffers(codecContext);

            // Drop any frames already decoded for the old position so they cannot be handed out.
            while (decodedFrames.TryDequeue(out var stale))
            {
                stale.NativeTexture.Reset();
                availableTextures.Enqueue(stale.NativeTexture);
            }

            // Discard any held/pending native frames belonging to the old position.
            skipHeldFrame?.Return();
            skipHeldFrame = null;
            pendingUploadFrame?.Return();
            pendingUploadFrame = null;

            // targetMs is a 0-based position. av_seek_frame works in stream-timebase units that
            // INCLUDE the container's start_time, so add start_time back when converting. The
            // skip target below stays 0-based to match the 0-based frameTime computed in
            // readDecodedFrames — keeping the two consistent is what makes seeks land precisely.
            long startTime = avStream->start_time != ffmpeg.AV_NOPTS_VALUE ? avStream->start_time : 0;
            long ts = (long)(targetMs / timeBaseInSeconds / 1000.0) + startTime;
            ffmpeg.av_seek_frame(formatContext, avStream->index, ts, ffmpeg.AVSEEK_FLAG_BACKWARD);

            // Seek with BACKWARD lands on the keyframe at or before the target. We want to skip
            // the frames between that keyframe and the target so playback resumes at the right
            // place — but we must never skip the LAST decodable frame, or a seek that lands
            // between keyframes (or past the last keyframe) would emit nothing and freeze.
            skipOutputUntilTime = targetMs;
            decodeGeneration = generation;
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

    /// <summary>
    /// The affine YUV->RGB transform for this stream's declared colorspace and range, as a
    /// column-major float[16]. Frames carry their own values and may disagree with the stream's, so
    /// prefer a texture's <see cref="VideoTexture.ConversionMatrix"/> where one is in hand; this is the
    /// fallback for before the first frame arrives.
    /// </summary>
    public float[] GetConversionMatrix()
    {
        if (codecContext == null)
            return affineFrom(rec601_limited, limited_luma_offset);

        return ConversionMatrixFor(codecContext->colorspace, codecContext->color_range, codecContext->width, codecContext->height);
    }

    /// <summary>
    /// The affine transform for one frame, preferring the frame's own colorspace and range over the
    /// stream's. A hardware decoder can hand back full-range NV12 (VideoToolbox's
    /// <c>420YpCbCr8BiPlanarFullRange</c>) from a stream that declares nothing, and reading it off the
    /// frame is the only way to tell.
    /// </summary>
    private float[] conversionMatrixFor(AVFrame* frame)
    {
        var colorspace = frame->colorspace != AVColorSpace.AVCOL_SPC_UNSPECIFIED || codecContext == null
            ? frame->colorspace
            : codecContext->colorspace;

        var range = frame->color_range != AVColorRange.AVCOL_RANGE_UNSPECIFIED || codecContext == null
            ? frame->color_range
            : codecContext->color_range;

        return ConversionMatrixFor(colorspace, range, frame->width, frame->height);
    }

    internal static float[] ConversionMatrixFor(AVColorSpace colorspace, AVColorRange range, int width, int height)
    {
        // Unspecified colorspace falls back to the size heuristic that has always been here: anything
        // SD-sized is assumed BT.601, anything larger BT.709.
        bool useHdtv = colorspace == AVColorSpace.AVCOL_SPC_BT709
                    || (colorspace == AVColorSpace.AVCOL_SPC_UNSPECIFIED && (width >= 704 || height >= 576));

        // Unspecified range means limited: that is the assumption for broadcast-derived video, and the
        // one every frame that reached this code before the range was read at all was treated under.
        bool fullRange = range == AVColorRange.AVCOL_RANGE_JPEG;

        if (fullRange)
            return affineFrom(useHdtv ? rec709_full : rec601_full, 0f);

        return affineFrom(useHdtv ? rec709_limited : rec601_limited, limited_luma_offset);
    }

    // Column-major 3x3 YUV->RGB coefficients: m[0..2] is the Y column, m[3..5] Cb, m[6..8] Cr, so the
    // shader's mat * (y, cb, cr) reads them straight through.
    //
    // The limited-range pairs carry the 255/219 luma and 255/224 chroma expansion (hence the 1.164s);
    // the full-range pairs do not, because full-range samples already span the whole 0-255. Picking the
    // wrong one of a pair is a washed-out or crushed picture, not a broken one, which is exactly why it
    // went unnoticed before the frame's color range was consulted at all.

    private static readonly float[] rec709_limited =
    {
        1.164f,  1.164f, 1.164f,
        0.000f, -0.213f, 2.112f,
        1.793f, -0.533f, 0.000f
    };

    private static readonly float[] rec601_limited =
    {
        1.164f,  1.164f, 1.164f,
        0.000f, -0.392f, 2.017f,
        1.596f, -0.813f, 0.000f
    };

    private static readonly float[] rec709_full =
    {
        1.0000f,  1.0000f, 1.0000f,
        0.0000f, -0.1873f, 1.8556f,
        1.5748f, -0.4681f, 0.0000f
    };

    private static readonly float[] rec601_full =
    {
        1.0000f,  1.000000f, 1.000f,
        0.0000f, -0.344136f, 1.772f,
        1.4020f, -0.714136f, 0.000f
    };

    /// <summary>
    /// Black level for limited-range video, and the chroma centre. Normalised by 255 because that is
    /// what a UNORM8 sampler divides by — the shader previously used 16/256 and 128/256, which is a
    /// fraction of a code value off.
    /// </summary>
    private const float limited_luma_offset = 16f / 255f;

    private const float chroma_offset = 128f / 255f;

    /// <summary>
    /// Folds a 3x3 colour matrix and the sample offsets into the single affine transform the shader
    /// applies as <c>u_YuvCoeff * vec4(y, cb, cr, 1.0)</c>: the coefficients occupy columns 0-2, and
    /// column 3 carries <c>-(M * offset)</c> so the subtraction happens inside the same multiply.
    /// Returned column-major, the layout GLSL reads a <c>mat4</c> in.
    /// </summary>
    private static float[] affineFrom(float[] m, float lumaOffset)
    {
        float[] result = new float[16];

        for (int row = 0; row < 3; row++)
        {
            float cy = m[row];
            float cb = m[3 + row];
            float cr = m[6 + row];

            result[row] = cy;
            result[4 + row] = cb;
            result[8 + row] = cr;
            result[12 + row] = -(cy * lumaOffset + cb * chroma_offset + cr * chroma_offset);
        }

        result[15] = 1f;
        return result;
    }

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

        // Create the hardware device context. D3D11VA gets a special one built around the renderer's
        // own device; everything else lets FFmpeg create its own, which is right for them because
        // nothing samples their frames in place.
        AVBufferRef* hwDeviceCtx = hwType == AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA
            ? createSharedD3D11DeviceContext()
            : createOwnedDeviceContext(hwType);

        if (hwDeviceCtx == null)
        {
            ffmpeg.avcodec_free_context(&ctx);
            return false;
        }

        // Transfer ownership of hwDeviceCtx to the codec context.
        // avcodec_free_context will free it — do not call av_buffer_unref separately.
        ctx->hw_device_ctx = hwDeviceCtx;

        // D3D11VA only: claim the frame context ourselves so the decoder's texture array is created
        // bindable. See negotiateD3D11Format.
        if (hwType == AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA && renderer is ID3D11Renderer)
        {
            getFormatCallback = negotiateD3D11Format;
            ctx->get_format = getFormatCallback;
        }

        if (ffmpeg.avcodec_open2(ctx, codec, null) < 0)
        {
            ffmpeg.avcodec_free_context(&ctx);
            Logger.Verbose($"[VideoDecoder] Failed to open codec with HW device {hwType}");
            return false;
        }

        codecContext = ctx;
        return true;
    }

    /// <summary>
    /// The plain case: FFmpeg allocates and owns the device. Right for every backend that reads its
    /// frames back to the CPU, because nothing on our side ever touches that device.
    /// </summary>
    private AVBufferRef* createOwnedDeviceContext(AVHWDeviceType hwType)
    {
        AVBufferRef* ctx = null;
        int result = ffmpeg.av_hwdevice_ctx_create(&ctx, hwType, null, null, 0);

        if (result < 0)
        {
            Logger.Verbose($"[VideoDecoder] Failed to create HW device context for {hwType}: {result}");
            return null;
        }

        return ctx;
    }

    /// <summary>
    /// Builds a D3D11VA device context around the <em>renderer's</em> <c>ID3D11Device</c> instead of
    /// letting FFmpeg create its own.
    /// </summary>
    /// <remarks>
    /// This is the precondition for zero-copy on this backend and not an optimization on top of it: another
    ///  cannot sample a texture produced on one device without a shared-resource copy,
    /// which is the exact copy the whole phase exists to delete. Sharing also puts decoding and draw on
    /// one immediate context, which is what makes the frame lifetime story hold —
    /// see <see cref="D3D11HardwareVideoTexture"/>.
    /// <para>
    /// Returns null when the renderer is not D3D11 or has no device yet, and the caller then falls back
    /// to <see cref="createOwnedDeviceContext"/>. That path still decodes in hardware; it just reads
    /// the frames back like every other backend.
    /// </para>
    /// </remarks>
    private AVBufferRef* createSharedD3D11DeviceContext()
    {
        if (renderer is not ID3D11Renderer d3d11 || d3d11.NativeDevicePointer == nint.Zero)
        {
            Logger.Verbose("[VideoDecoder] D3D11VA without a D3D11 renderer to share a device with; letting FFmpeg own one.");
            return createOwnedDeviceContext(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
        }

        AVBufferRef* ctx = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);

        if (ctx == null)
        {
            Logger.Verbose("[VideoDecoder] av_hwdevice_ctx_alloc failed for D3D11VA.");
            return null;
        }

        var deviceCtx = (AVHWDeviceContext*)ctx->data;
        var d3dCtx = (AVD3D11VADeviceContext*)deviceCtx->hwctx;

        // FFmpeg releases this on teardown, so hand it a reference of its own rather than ours —
        // otherwise closing a video would drop the renderer's device out from under the app.
        var device = new Vortice.Direct3D11.ID3D11Device(d3d11.NativeDevicePointer);
        device.AddRef();

        d3dCtx->device = (FFmpeg.AutoGen.ID3D11Device*)d3d11.NativeDevicePointer;

        int result = ffmpeg.av_hwdevice_ctx_init(ctx);

        if (result < 0)
        {
            Logger.Warning($"[VideoDecoder] Failed to initialise a shared D3D11 device context ({result}); falling back to an FFmpeg-owned device.");
            ffmpeg.av_buffer_unref(&ctx);
            device.Release();
            return createOwnedDeviceContext(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
        }

        Logger.Verbose("[VideoDecoder] D3D11VA sharing the renderer's device.");
        return ctx;
    }

    /// <summary>
    /// <c>get_format</c> for D3D11VA: takes over allocation of the frame context so the decoder's
    /// texture array is created with <c>D3D11_BIND_SHADER_RESOURCE</c>.
    /// </summary>
    /// <remarks>
    /// Without this flag the array cannot be bound as a shader resource <em>at all</em>, whatever the
    /// view says — and the flag can only be set before <c>av_hwframe_ctx_init</c>, which is why the
    /// frame context has to be built here rather than left to FFmpeg's default.
    /// <para>
    /// The pool is also deepened by <see cref="max_pending_frames"/>. D3D11VA's pool is a single
    /// texture array whose <c>ArraySize</c> is fixed at init, unlike VideoToolbox's growable buffer
    /// pool — so every frame the display path holds is one the decoder cannot use, and the default
    /// size assumes nobody holds any.
    /// </para>
    /// <para>
    /// Falling through to the next format in the list is always safe: it means the readback path.
    /// </para>
    /// </remarks>
    private AVPixelFormat negotiateD3D11Format(AVCodecContext* ctx, AVPixelFormat* formats)
    {
        AVPixelFormat first = formats != null ? formats[0] : AVPixelFormat.AV_PIX_FMT_NONE;

        for (AVPixelFormat* f = formats; f != null && *f != AVPixelFormat.AV_PIX_FMT_NONE; f++)
        {
            if (*f != AVPixelFormat.AV_PIX_FMT_D3D11)
                continue;

            AVBufferRef* framesRef = null;
            int result = ffmpeg.avcodec_get_hw_frames_parameters(ctx, ctx->hw_device_ctx, AVPixelFormat.AV_PIX_FMT_D3D11, &framesRef);

            if (result < 0 || framesRef == null)
            {
                Logger.Warning($"[VideoDecoder] avcodec_get_hw_frames_parameters failed ({result}); D3D11 frames will not be bindable.");
                return first;
            }

            var frames = (AVHWFramesContext*)framesRef->data;
            var d3dFrames = (AVD3D11VAFramesContext*)frames->hwctx;

            d3dFrames->BindFlags |= d3_d11_bind_shader_resource;
            frames->initial_pool_size += max_pending_frames;

            result = ffmpeg.av_hwframe_ctx_init(framesRef);

            if (result < 0)
            {
                Logger.Warning($"[VideoDecoder] av_hwframe_ctx_init failed for a bindable D3D11 array ({result}); falling back.");
                ffmpeg.av_buffer_unref(&framesRef);
                return first;
            }

            ctx->hw_frames_ctx = framesRef;
            return AVPixelFormat.AV_PIX_FMT_D3D11;
        }

        return first;
    }

    /// <summary>
    /// <c>D3D11_BIND_SHADER_RESOURCE</c>. Spelled out because FFmpeg.AutoGen exposes
    /// <c>BindFlags</c> as a plain integer rather than the D3D enum, and Vortice's
    /// <c>BindFlags.ShaderResource</c> is a different type.
    /// </summary>
    private const uint d3_d11_bind_shader_resource = 0x8;

    /// <summary>
    /// Decode threads to give the software codec.
    /// </summary>
    private static int softwareDecodeThreads() => Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    private void openCodecSoftware(AVCodec* codec)
    {
        codecContext = ffmpeg.avcodec_alloc_context3(codec);
        codecContext->pkt_timebase = avStream->time_base;
        ffmpeg.avcodec_parameters_to_context(codecContext, avStream->codecpar);

        int threads = softwareDecodeThreads();
        codecContext->thread_count = threads;

        if (ffmpeg.avcodec_open2(codecContext, codec, null) < 0)
            throw new Exception("Could not open software codec.");

        ActiveHardwareDevice = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
        Logger.Verbose($"[VideoDecoder] Software decoding active, {threads} decode threads.");
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

                        // First, retry any frame that was decoded but couldn't be uploaded last
                        // time because the pool was empty. Only drop it if it now belongs to a
                        // superseded seek generation.
                        if (pendingUploadFrame != null)
                        {
                            if (pendingUploadFrameGeneration != Volatile.Read(ref seekGeneration))
                            {
                                pendingUploadFrame.Return();
                                pendingUploadFrame = null;
                            }
                            else if (tryEmitFrame(pendingUploadFrame, pendingUploadFrameTime, pendingUploadFrameGeneration))
                            {
                                pendingUploadFrame = null;
                            }
                            else
                            {
                                // Pool still empty — wait for the draw thread to recycle a texture.
                                Thread.Sleep(1);
                                break;
                            }
                        }

                        if (poolShape == null || !availableTextures.IsEmpty)
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
        long sendStart = Stopwatch.GetTimestamp();
        int result = ffmpeg.avcodec_send_packet(codecContext, packet);
        pendingSendTicks += Stopwatch.GetTimestamp() - sendStart;

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
            long decodeStart = Stopwatch.GetTimestamp();
            int result = ffmpeg.avcodec_receive_frame(codecContext, receiveFrame);
            long decodeTicks = Stopwatch.GetTimestamp() - decodeStart;

            if (result < 0)
            {
                // Codec fully drained (EOF) while still in a post-seek skip means the seek target
                // was at or past the last frame. Emit the held frame so we show the final frame
                // instead of freezing on blank. EAGAIN just means "need more input" — keep holding.
                if (result == ffmpeg.AVERROR_EOF && skipOutputUntilTime.HasValue && skipHeldFrame != null)
                {
                    var held = skipHeldFrame;
                    skipHeldFrame = null;
                    skipOutputUntilTime = null;
                    if (!tryEmitFrame(held, skipHeldFrameTime, decodeGeneration))
                    {
                        pendingUploadFrame = held;
                        pendingUploadFrameTime = skipHeldFrameTime;
                        pendingUploadFrameGeneration = decodeGeneration;
                    }
                }
                break;
            }

            VideoStatistics.RecordDecode(pendingSendTicks + decodeTicks);
            pendingSendTicks = 0;
            VideoStatistics.RecordDecoderFormat((AVPixelFormat)receiveFrame->format);

            long ts = receiveFrame->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE
                ? receiveFrame->best_effort_timestamp
                : receiveFrame->pts;

            long startTime = avStream->start_time != ffmpeg.AV_NOPTS_VALUE ? avStream->start_time : 0;
            double frameTime = (ts - startTime) * timeBaseInSeconds * 1000.0;

            // Resolve pixel format — transfer from HW memory if needed
            FFmpegFrame frame;

            if (((AVPixelFormat)receiveFrame->format).IsHardwareFormat())
            {
                // Asked per frame, not per format: a hardware pixel format says nothing about what the
                // underlying buffer holds, and the renderer is the only thing that can tell. A false
                // answer — no zero-copy path on this backend, 10-bit content, an interop failure —
                // falls through to the readback, which always works.
                if (ZeroCopy.Value && renderer.CanSampleHardwareFrame(receiveFrame))
                {
                    VideoStatistics.RecordTransfer(0);

                    if (!zeroCopyFrames.TryDequeue(out var hwRef))
                        hwRef = new FFmpegFrame(returnZeroCopyFrame);

                    // Moves the reference, not the pixels: hwRef now holds the frame's claim on the
                    // CVPixelBuffer and keeps VideoToolbox from recycling it until the frame is
                    // returned to the pool.
                    ffmpeg.av_frame_move_ref(hwRef.Pointer, receiveFrame);
                    frame = hwRef;
                }
                else
                {
                    if (!hwTransferFrames.TryDequeue(out var hwFrame))
                        hwFrame = new FFmpegFrame(returnHwTransferFrame);

                    long transferStart = Stopwatch.GetTimestamp();
                    int transferResult = ffmpeg.av_hwframe_transfer_data(hwFrame.Pointer, receiveFrame, 0);
                    VideoStatistics.RecordTransfer(Stopwatch.GetTimestamp() - transferStart);

                    if (transferResult < 0)
                    {
                        Logger.Warning($"[VideoDecoder] HW frame transfer failed: {transferResult}");
                        hwFrame.Return();
                        continue;
                    }

                    frame = hwFrame;
                }
            }
            else
            {
                VideoStatistics.RecordTransfer(0);
                frame = new FFmpegFrame();
                ffmpeg.av_frame_move_ref(frame.Pointer, receiveFrame);
            }

            lastDecodedFrameTime = (float)frameTime;

            VideoStatistics.RecordFrame(frame.PixelFormat, frame.Pointer->width, frame.Pointer->height, layoutFor(frame.PixelFormat), frame.PixelFormat.IsHardwareFormat());

            long convertStart = Stopwatch.GetTimestamp();
            frame = ensureSamplableFormat(frame);
            VideoStatistics.RecordConvert(Stopwatch.GetTimestamp() - convertStart);

            if (frame == null) continue;

            // Post-seek skip: drop frames between the landed keyframe and the seek target so
            // playback resumes at the requested time. Critically, we HOLD BACK the most recent
            // skipped frame instead of discarding it, so that if the target lands past the last
            // decodable frame (or between keyframes with imperfect timestamps) we still have a
            // frame to show instead of freezing on a blank screen.
            if (skipOutputUntilTime.HasValue)
            {
                if (frameTime < skipOutputUntilTime.Value)
                {
                    // Supersede any previously held frame, returning it to its pool.
                    skipHeldFrame?.Return();
                    skipHeldFrame = frame;
                    skipHeldFrameTime = frameTime;
                    continue;
                }

                // Reached the target. The held frame (if any) is no longer needed.
                skipHeldFrame?.Return();
                skipHeldFrame = null;
                skipOutputUntilTime = null;
            }

            var shape = shapeFor(frame);

            // Schedule pool warm-up on the draw thread on the very first frame, and again whenever the
            // frames stop matching what the pool holds. The decode loop then polls availableTextures
            // until textures arrive (typically within one draw frame ~4ms); textures from the previous
            // shape are dropped in tryEmitFrame as they come back.
            if (poolShape != shape)
            {
                poolShape = shape;

                renderer.ScheduleToDrawThread(() =>
                {
                    for (int i = 0; i < max_pending_frames; i++)
                        availableTextures.Enqueue(new VideoTexture(renderer, textureManager, shape));
                });
            }

            // Try to upload into a pooled texture. If the pool is momentarily empty we keep the
            // frame in pendingUploadFrame and retry next iteration rather than dropping it.
            if (!tryEmitFrame(frame, frameTime, decodeGeneration))
            {
                pendingUploadFrame = frame;
                pendingUploadFrameTime = frameTime;
                pendingUploadFrameGeneration = decodeGeneration;
                return; // back off; decode loop will retry the pending frame shortly
            }
        }
    }

    /// <summary>
    /// Uploads a frame into a pooled texture and enqueues it for display.
    /// Returns false (without consuming the frame) if the texture pool holds nothing this frame can go
    /// into — the caller is responsible for holding the frame and retrying.
    /// </summary>
    private bool tryEmitFrame(FFmpegFrame frame, double frameTime, int generation)
    {
        // Derived from the frame rather than read off poolShape: a frame held back during a post-seek
        // skip, or parked in pendingUploadFrame, can be emitted after a format change moved the pool on,
        // and it still has to land in a texture that fits *it*.
        var shape = shapeFor(frame);

        VideoTexture? tex = null;

        // Textures from a previous shape come back through ReturnFrames long after the pool was
        // re-warmed for a new one. Drop them here rather than uploading a frame into a plane set that
        // cannot hold it — the alternative is a silent mis-sample, since an NV12 frame's second plane
        // is twice as wide in bytes as a YUV420P texture expects.
        while (availableTextures.TryDequeue(out var candidate))
        {
            if (candidate.Shape == shape)
            {
                tex = candidate;
                break;
            }

            candidate.Dispose();
        }

        if (tex == null)
        {
            GlobalStatistics.Get<int>("Video", "Frames Waiting (Pool Empty)", StatisticKind.Cumulative).Value++;
            return false;
        }

        var upload = new VideoTextureUpload(frame);

        tex.SetData(upload, conversionMatrixFor(frame.Pointer));

        // Texture is a dimension-only proxy — no GL handles, no Video namespace import needed.
        // VideoSprite reads NativeTexture directly for rendering.
        var texture = new Texture(shape.Width, shape.Height);
        decodedFrames.Enqueue(new DecodedFrame
        {
            Time = frameTime,
            Texture = texture,
            NativeTexture = tex,
            Generation = generation,
        });
        GlobalStatistics.Get<int>("Video", "Frames Decoded", StatisticKind.Cumulative).Value++;
        return true;
    }

    /// <summary>
    /// The texture shape a decoded frame needs.
    /// </summary>
    /// <remarks>
    /// The zero-copy flag is read back off the pixel format rather than carried alongside: a frame that
    /// still has a hardware format here is one that was never read back, because the transfer path
    /// replaces it with NV12 or YUV420P. Deriving it means the two cannot drift apart, which matters
    /// because this is called both as a frame is produced and again when a held one is emitted later.
    /// </remarks>
    private static VideoTextureShape shapeFor(FFmpegFrame frame) => new VideoTextureShape(
        frame.Pointer->width,
        frame.Pointer->height,
        layoutFor(frame.PixelFormat),
        frame.PixelFormat.IsHardwareFormat());

    /// <summary>
    /// Which plane set a samplable frame maps onto. Only the two formats
    /// <see cref="ensureSamplableFormat"/> can produce reach this.
    /// </summary>
    private static VideoPlaneLayout layoutFor(AVPixelFormat format) => format switch
    {
        AVPixelFormat.AV_PIX_FMT_NV12 => VideoPlaneLayout.Nv12,

        // Zero-copy hardware frames
        AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX => VideoPlaneLayout.Nv12,
        AVPixelFormat.AV_PIX_FMT_D3D11 => VideoPlaneLayout.Nv12,

        _ => VideoPlaneLayout.Yuv420P,
    };

    /// <summary>
    /// Brings a frame into a format the shaders can sample directly, converting only when it is in
    /// neither.
    /// </summary>
    private FFmpegFrame ensureSamplableFormat(FFmpegFrame frame)
    {
        // A hardware frame that reached here is one of the renderer said it would sample in place, so
        // there is nothing to convert, and nothing here could convert it anyway, since its data[]
        // are opaque handles rather than pixels.
        if (frame.PixelFormat.IsHardwareFormat())
            return frame;

        if (frame.PixelFormat == AVPixelFormat.AV_PIX_FMT_NV12)
            return frame;

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
                scaled.Return(); // back to the scaler pool, not Dispose — keeps the pool slot
                frame.Return();
                return null!;
            }
        }

        if (swsContext == null)
        {
            Logger.Warning("[VideoDecoder] sws_getCachedContext returned null.");
            scaled.Return();
            frame.Return();
            return null!;
        }

        int scaleResult = ffmpeg.sws_scale(
            swsContext,
            frame.Pointer->data, frame.Pointer->linesize, 0, h,
            scaled.Pointer->data, scaled.Pointer->linesize);

        frame.Return();

        if (scaleResult < 0)
        {
            Logger.Warning($"[VideoDecoder] sws_scale failed: {scaleResult}");
            scaled.Return();
            return null!;
        }

        return scaled;
    }

    public enum DecoderState
    {
        Preparing = -1,
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

        // Free any native frames held outside the pools (post-seek skip / pending upload).
        skipHeldFrame?.Dispose();
        skipHeldFrame = null;
        pendingUploadFrame?.Dispose();
        pendingUploadFrame = null;

        // Return all decodedFrames textures to the pool, then dispose them all.
        while (decodedFrames.TryDequeue(out var frame))
            availableTextures.Enqueue(frame.NativeTexture);

        while (availableTextures.TryDequeue(out var vt))
            vt.Dispose(); // unregisters from textureManager, schedules GL delete

        while (hwTransferFrames.TryDequeue(out var hf)) hf.Dispose();
        while (scalerFrames.TryDequeue(out var sf)) sf.Dispose();
        while (zeroCopyFrames.TryDequeue(out var zf)) zf.Dispose();

        videoStream?.Dispose();
        videoStream = null;

        if (selfHandle.IsAllocated)
            selfHandle.Free();
        GC.SuppressFinalize(this);
    }
}
