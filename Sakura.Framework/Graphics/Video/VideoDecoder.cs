// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Graphics.Video;

public unsafe class VideoDecoder : IDisposable
{
    private readonly string filePath;
    private AVFormatContext* formatContext;
    private AVCodecContext* codecContext;
    private SwsContext* swsContext;
    private int videoStreamIndex = -1;
    private double timeBase;
    private readonly ManualResetEventSlim decoderStateEvent = new ManualResetEventSlim(false);
    private volatile bool seekRequested;
    private double seekTargetMs;

    private readonly ConcurrentQueue<VideoFrame> frameQueue = new ConcurrentQueue<VideoFrame>();
    private CancellationTokenSource cancellationTokenSource;
    private Task decodeTask;

    private const int sws_bilinear = 2;

    public int Width => codecContext != null ? codecContext->width : 0;
    public int Height => codecContext != null ? codecContext->height : 0;
    public double Duration { get; private set; } // in milliseconds

    public VideoDecoder(string filePath)
    {
        this.filePath = filePath;
    }

    static VideoDecoder()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string frameworkRuntimePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "osx", "native");

            if (Directory.Exists(frameworkRuntimePath) && File.Exists(Path.Combine(frameworkRuntimePath, "libavformat.58.dylib")))
            {
                ffmpeg.RootPath = frameworkRuntimePath;
            }
            else if (Directory.Exists("/opt/homebrew/lib") && File.Exists("/opt/homebrew/lib/libavformat.dylib"))
            {
                ffmpeg.RootPath = "/opt/homebrew/lib";
            }
            else
            {
                ffmpeg.RootPath = frameworkRuntimePath;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            ffmpeg.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", architecture, "native");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
            ffmpeg.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", architecture, "native");
        }

        Logger.Debug($"[FFmpeg] RootPath resolved to: {ffmpeg.RootPath}");
    }

    public void Start()
    {
        InitializeFFmpeg();
        cancellationTokenSource = new CancellationTokenSource();
        decodeTask = Task.Factory.StartNew(DecodeLoop, cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Seek(double timeMs)
    {
        seekTargetMs = timeMs;
        seekRequested = true;
        decoderStateEvent.Set();
    }

    private void InitializeFFmpeg()
    {
        // Open Format Context
        var pFormatContext = ffmpeg.avformat_alloc_context();
        if (ffmpeg.avformat_open_input(&pFormatContext, filePath, null, null) != 0)
            throw new Exception($"Could not open video file: {filePath}");

        formatContext = pFormatContext;

        if (ffmpeg.avformat_find_stream_info(formatContext, null) < 0)
            throw new Exception("Could not find stream info");

        // Find Video Stream
        AVCodec* codec = null;
        videoStreamIndex = ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
        if (videoStreamIndex < 0)
            throw new Exception("Could not find a video stream");

        var stream = formatContext->streams[videoStreamIndex];

        // Calculate timebase to convert PTS to milliseconds
        timeBase = stream->time_base.num / (double)stream->time_base.den;
        if (formatContext->duration != ffmpeg.AV_NOPTS_VALUE)
        {
            // Global duration is measured in AV_TIME_BASE (microseconds)
            Duration = (formatContext->duration / (double)ffmpeg.AV_TIME_BASE) * 1000.0;
        }
        else if (stream->duration != ffmpeg.AV_NOPTS_VALUE)
        {
            Duration = (stream->duration * timeBase) * 1000.0;
        }
        else
        {
            // FFmpeg literally cannot determine the length of this file
            Duration = 0;
        }

        // Initialize Codec Context
        codecContext = ffmpeg.avcodec_alloc_context3(codec);
        ffmpeg.avcodec_parameters_to_context(codecContext, stream->codecpar);

        // Hardware Acceleration fallback logic could be added here before opening the codec

        if (ffmpeg.avcodec_open2(codecContext, codec, null) < 0)
            throw new Exception("Could not open codec");

        // Initialize Scaler (YUV -> RGBA)
        swsContext = ffmpeg.sws_getContext(
            codecContext->width, codecContext->height, codecContext->pix_fmt,
            codecContext->width, codecContext->height, AVPixelFormat.AV_PIX_FMT_RGBA,
            sws_bilinear, null, null, null);
    }

    private void DecodeLoop()
    {
        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        AVFrame* rgbaFrame = ffmpeg.av_frame_alloc();

        // Setup RGBA Frame buffer
        int bufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_RGBA, codecContext->width, codecContext->height, 1);
        byte* rgbaBuffer = (byte*)ffmpeg.av_malloc((ulong)bufferSize);
        rgbaFrame->data[0] = rgbaBuffer;
        rgbaFrame->linesize[0] = codecContext->width * 4;

        try
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                if (seekRequested)
                {
                    long seekTargetUs = (long)(seekTargetMs * 1000.0);
                    ffmpeg.avformat_seek_file(formatContext, -1, long.MinValue, seekTargetUs, long.MaxValue, ffmpeg.AVSEEK_FLAG_BACKWARD);
                    ffmpeg.avcodec_flush_buffers(codecContext);

                    while (frameQueue.TryDequeue(out var f)) f.Dispose();

                    seekRequested = false;
                    decoderStateEvent.Reset();
                    continue;
                }

                if (frameQueue.Count > 30)
                {
                    // wait up to 10ms for the queue to drain
                    decoderStateEvent.Wait(10, cancellationTokenSource.Token);
                    continue;
                }

                if (ffmpeg.av_read_frame(formatContext, packet) < 0)
                {
                    decoderStateEvent.Reset();

                    try
                    {
                        // wait indefinitely until Seek() calls decoderStateEvent.Set()
                        decoderStateEvent.Wait(cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    continue;
                }

                if (packet->stream_index == videoStreamIndex)
                {
                    ffmpeg.avcodec_send_packet(codecContext, packet);

                    while (ffmpeg.avcodec_receive_frame(codecContext, frame) == 0)
                    {
                        // Convert to RGBA
                        ffmpeg.sws_scale(swsContext, frame->data, frame->linesize, 0, frame->height, rgbaFrame->data, rgbaFrame->linesize);

                        // Calculate Timestamp in milliseconds
                        double ptsMs = (frame->best_effort_timestamp * timeBase) * 1000.0;

                        // Copy to managed byte array for the Texture pipeline
                        byte[] pixelData = new byte[bufferSize];
                        fixed (byte* dest = pixelData)
                        {
                            Buffer.MemoryCopy(rgbaBuffer, dest, bufferSize, bufferSize);
                        }

                        frameQueue.Enqueue(new VideoFrame
                        {
                            Timestamp = ptsMs,
                            Width = codecContext->width,
                            Height = codecContext->height,
                            PixelData = pixelData
                        });
                    }
                }
                ffmpeg.av_packet_unref(packet);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Video decoding failed", ex);
        }
        finally
        {
            ffmpeg.av_free(rgbaBuffer);
            ffmpeg.av_frame_free(&rgbaFrame);
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&packet);
        }
    }

    public bool TryGetNextFrame(out VideoFrame frame) => frameQueue.TryDequeue(out frame);

    public bool TryPeekNextFrame(out VideoFrame frame) => frameQueue.TryPeek(out frame);

    public void Dispose()
    {
        cancellationTokenSource.Cancel();
        decodeTask.Wait();

        decodeTask.Wait();
        decoderStateEvent.Dispose();

        if (swsContext != null)
            ffmpeg.sws_freeContext(swsContext);
        if (codecContext != null)
        {
            var pCodecContext = codecContext;
            ffmpeg.avcodec_free_context(&pCodecContext);
        }
        if (formatContext != null)
        {
            var pFormatContext = formatContext;
            ffmpeg.avformat_close_input(&pFormatContext);
        }
    }
}
