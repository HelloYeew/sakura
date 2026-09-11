// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;
using FFmpeg.AutoGen;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// Per-stage instrumentation for the video pipeline
/// </summary>
internal static class VideoStatistics
{
    private static readonly double ms_per_tick = 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Weight given to the newest sample. At 30 fps this settles within roughly half a second —
    /// steady enough to read off the window, quick enough to follow a seek or a resolution change.
    /// </summary>
    private const double smoothing = 0.1;

    /// <summary>
    /// How much wall time the byte-rate window covers before it is divided out and restarted.
    /// </summary>
    private const double rate_window_ms = 1000;

    /// <summary>
    /// Time inside <c>avcodec_send_packet</c> plus <c>avcodec_receive_frame</c>. The frame stays in
    /// GPU memory across this stage when a hardware decoder is active.
    /// </summary>
    private static readonly GlobalStatistic<double> stat_decode =
        GlobalStatistics.Get<double>("Video", "Decode Time", StatisticKind.Gauge, StatisticUnit.Milliseconds);

    /// <summary>
    /// Time inside <c>av_hwframe_transfer_data</c> — the GPU -> CPU readback. Zero on software decode.
    /// </summary>
    private static readonly GlobalStatistic<double> stat_transfer =
        GlobalStatistics.Get<double>("Video", "Transfer Time", StatisticKind.Gauge, StatisticUnit.Milliseconds);

    /// <summary>
    /// Time spent normalizing the frame to a samplable format (<c>sws_scale</c>). Near zero when the
    /// frame already arrives in the target format, which today only happens on software decoded of
    /// 4:2:0 8-bit content.
    /// </summary>
    private static readonly GlobalStatistic<double> stat_convert =
        GlobalStatistics.Get<double>("Video", "Convert Time", StatisticKind.Gauge, StatisticUnit.Milliseconds);

    /// <summary>
    /// Time spent pushing the planes to the GPU. Unlike the three above, this one is on the draw
    /// thread and so is already counted in the frame-time overlay's busy column.
    /// </summary>
    private static readonly GlobalStatistic<double> stat_upload =
        GlobalStatistics.Get<double>("Video", "Upload Time", StatisticKind.Gauge, StatisticUnit.Milliseconds);

    /// <summary>
    /// The format the codec hands back, before any readback. A hardware surface type here
    /// (<c>videotoolbox_vld</c>, <c>d3d11</c>, <c>vaapi</c>) confirms hardware decode is really live.
    /// </summary>
    private static readonly GlobalStatistic<string> stat_decoder_format =
        GlobalStatistics.Get<string>("Video", "Decoder Format");

    /// <summary>
    /// The format reaching the conversion step for hardware decode what the readback produced.
    /// </summary>
    private static readonly GlobalStatistic<string> stat_frame_format =
        GlobalStatistics.Get<string>("Video", "Frame Format");

    /// <summary>
    /// Size of one frame as it reaches the conversion step, so 4K and 1080p are comparable at a
    /// glance. That is what the readback moved and what the conversion reads; the upload moves the
    /// same amount again while NV12 and YUV420P are both 12 bits per pixel, which is every case the
    /// hardware path hits today.
    /// </summary>
    private static readonly GlobalStatistic<long> stat_frame_bytes =
        GlobalStatistics.Get<long>("Video", "Frame Bytes", StatisticKind.Gauge, StatisticUnit.Bytes);

    /// <summary>
    /// <see cref="stat_frame_bytes"/> times the rate frames are actually emitted at — the cost of a
    /// single full-frame pass. The pipeline makes several passes per frame, so the bus traffic is a
    /// multiple of this, not this.
    /// </summary>
    private static readonly GlobalStatistic<double> stat_byte_rate =
        GlobalStatistics.Get<double>("Video", "Frame Byte Rate", StatisticKind.Gauge, StatisticUnit.BytesPerSecond);

    // Decode-thread only: the last format/size a size was computed for, so av_image_get_buffer_size
    // and the string conversion run on change rather than on every frame.
    private static AVPixelFormat lastDecoderFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    private static AVPixelFormat lastFrameFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    private static int lastFrameWidth;
    private static int lastFrameHeight;
    private static long frameBytes;

    // Decode-thread only: the open byte-rate window.
    private static long windowBytes;
    private static long windowStart;

    public static void RecordDecode(long ticks) => accumulate(stat_decode, ticks);

    /// <summary>
    /// Records the readback stage. Call with zero on the software path so the average decays to zero
    /// rather than sitting on a stale hardware reading.
    /// </summary>
    public static void RecordTransfer(long ticks) => accumulate(stat_transfer, ticks);

    public static void RecordConvert(long ticks) => accumulate(stat_convert, ticks);

    public static void RecordUpload(long ticks) => accumulate(stat_upload, ticks);

    /// <summary>
    /// Records the format the codec produced, before any hardware readback.
    /// </summary>
    public static void RecordDecoderFormat(AVPixelFormat format)
    {
        if (format == lastDecoderFormat)
            return;

        lastDecoderFormat = format;
        stat_decoder_format.Value = formatName(format);
    }

    /// <summary>
    /// Records a frame arriving at the conversion step and folds its size into the byte rate.
    /// </summary>
    public static void RecordFrame(AVPixelFormat format, int width, int height)
    {
        if (format != lastFrameFormat || width != lastFrameWidth || height != lastFrameHeight)
        {
            lastFrameFormat = format;
            lastFrameWidth = width;
            lastFrameHeight = height;

            stat_frame_format.Value = formatName(format);

            // align 1: the packed size, which is what the upload moves. The decoder's own buffers are
            // padded wider than this, but that padding is skipped a row at a time rather than sent.
            int size = ffmpeg.av_image_get_buffer_size(format, width, height, 1);
            frameBytes = size > 0 ? size : 0;
            stat_frame_bytes.Value = frameBytes;
        }

        windowBytes += frameBytes;

        long now = Stopwatch.GetTimestamp();

        if (windowStart == 0)
        {
            windowStart = now;
            return;
        }

        double elapsed = (now - windowStart) * ms_per_tick;

        if (elapsed < rate_window_ms)
            return;

        stat_byte_rate.Value = windowBytes * 1000.0 / elapsed;
        windowBytes = 0;
        windowStart = now;
    }

    private static void accumulate(GlobalStatistic<double> stat, long ticks)
    {
        double ms = ticks * ms_per_tick;
        double previous = stat.Value;

        // A zero previous value means nothing has been recorded yet (or the statistic was cleared),
        // so seed rather than easing up from zero over the first dozen frames.
        stat.Value = previous > 0 ? previous + (ms - previous) * smoothing : ms;
    }

    private static string formatName(AVPixelFormat format) =>
        ffmpeg.av_get_pix_fmt_name(format) ?? format.ToString();
}
