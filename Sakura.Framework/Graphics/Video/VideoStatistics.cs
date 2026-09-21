// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Textures;
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
    /// The format actually being sampled — what the readback produced on the copying path, or the
    /// <c>CVPixelBuffer</c>'s own layout on the zero-copy one, suffixed <c>(zero-copy)</c> so the two
    /// are distinguishable. Deliberately not the codec's surface type, which
    /// <see cref="stat_decoder_format"/> already reports: a hardware format here would say nothing
    /// about what the shader receives.
    /// </summary>
    private static readonly GlobalStatistic<string> stat_frame_format =
        GlobalStatistics.Get<string>("Video", "Frame Format");

    /// <summary>
    /// Size of one decoded frame, so 4K and 1080p are comparable at a glance. A property of the
    /// content, reported on every path — including zero-copy, where the frame is this large and none
    /// of it is moved. What is or is not moved is <see cref="stat_byte_rate"/>'s job.
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
    private static bool lastFrameZeroCopy;
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
    /// How a sampled plane layout reads in the overlay, matching the FFmpeg pixel-format spelling it
    /// corresponds to, so the two paths are comparable at a glance.
    /// </summary>
    private static string layoutName(VideoPlaneLayout layout) =>
        layout == VideoPlaneLayout.Nv12 ? "nv12" : "yuv420p";

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
    public static void RecordFrame(AVPixelFormat format, int width, int height, VideoPlaneLayout sampledLayout, bool zeroCopy)
    {
        if (format != lastFrameFormat || width != lastFrameWidth || height != lastFrameHeight || zeroCopy != lastFrameZeroCopy)
        {
            lastFrameFormat = format;
            lastFrameWidth = width;
            lastFrameHeight = height;
            lastFrameZeroCopy = zeroCopy;

            // On the zero-copy path `format` is the opaque hardware surface type, which names neither
            // what is sampled nor how big it is — av_image_get_buffer_size returns <= 0 for it. Both
            // answers come from the layout instead, which is the thing the shader actually sees.
            if (zeroCopy)
            {
                stat_frame_format.Value = $"{layoutName(sampledLayout)} (zero-copy)";
                frameBytes = NativeTextureMemory.BytesForVideoPlanes(width, height);
            }
            else
            {
                stat_frame_format.Value = formatName(format);

                // align 1: the packed size, which is what the upload moves. The decoder's own buffers
                // are padded wider than this, but that padding is skipped a row at a time rather than
                // sent.
                int size = ffmpeg.av_image_get_buffer_size(format, width, height, 1);
                frameBytes = size > 0 ? size : 0;
            }

            stat_frame_bytes.Value = frameBytes;
        }

        // Bytes the pipeline actually passes over the bus, which is the whole point of the rate: zero
        // on the zero-copy path, where the frame is never read back and never uploaded. A non-zero
        // Frame Bytes beside a zero rate is the result, not a bug.
        windowBytes += zeroCopy ? 0 : frameBytes;

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
