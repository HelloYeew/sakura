// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// Splits how long frames take to reach the screen (<see cref="DisplayLatency"/>) into where the time goes, settled over
/// the frames before: the update the frame shows, its wait for the draw to take it, the draw, the GPU, and presenting:
/// from the GPU finishing it (or the draw handing it over, where the GPU's time isn't known) to it being on screen.
/// </summary>
/// <remarks>
/// The host notes when each frame it draws was updated, taken and handed over (<see cref="AddDrawn"/>), and the renderer
/// says when frames reached the screen (<see cref="IRenderer.TryTakeShownFrame"/>), matched up by the update's time.
/// Each part is eased the way <see cref="DisplayLatency"/> eases the whole, so the parts add up to the same figure while
/// the display stays as it is.
/// </remarks>
internal sealed class DisplayLatencySplit
{
    private const double smoothing = 0.05;

    /// <summary>
    /// Beyond any display's latency, as <see cref="DisplayLatency"/> has it: a frame shown after a stall.
    /// </summary>
    private const double max_ms = 500;

    /// <summary>
    /// Frames drawn that are kept to be matched: more than can be on their way to the screen at once.
    /// </summary>
    private const int drawn_frames_kept = 16;

    // Logged once there are this many, several seconds' worth, so the figures have settled past the app's start.
    private const int readings_to_log = 600;

    private static readonly double ms_per_tick = 1000.0 / Stopwatch.Frequency;

    private static readonly GlobalStatistic<double> stat_update = GlobalStatistics.Get<double>("Renderer", "Display Latency: Update", StatisticKind.Gauge, StatisticUnit.Milliseconds);
    private static readonly GlobalStatistic<double> stat_waiting = GlobalStatistics.Get<double>("Renderer", "Display Latency: Waiting for Draw", StatisticKind.Gauge, StatisticUnit.Milliseconds);
    private static readonly GlobalStatistic<double> stat_draw = GlobalStatistics.Get<double>("Renderer", "Display Latency: Draw", StatisticKind.Gauge, StatisticUnit.Milliseconds);
    private static readonly GlobalStatistic<double> stat_gpu = GlobalStatistics.Get<double>("Renderer", "Display Latency: GPU", StatisticKind.Gauge, StatisticUnit.Milliseconds);
    private static readonly GlobalStatistic<double> stat_presenting = GlobalStatistics.Get<double>("Renderer", "Display Latency: Presenting", StatisticKind.Gauge, StatisticUnit.Milliseconds);

    private readonly long[] contentTicks = new long[drawn_frames_kept];
    private readonly long[] updatedTicks = new long[drawn_frames_kept];
    private readonly long[] takenTicks = new long[drawn_frames_kept];
    private readonly long[] submittedTicks = new long[drawn_frames_kept];
    private int nextDrawn;

    private double update;
    private double waiting;
    private double draw;
    private double afterSubmit;
    private double gpu;
    private bool gpuKnown;
    private int readings;

    /// <summary>
    /// The update, its wait for the draw, the draw, the GPU and presenting, in milliseconds, settled; all 0 before the
    /// first frame is matched. The GPU's part is 0, and presenting all from the draw handing the frame over, where the
    /// renderer can't tell when the GPU finished.
    /// </summary>
    public (double Update, double Waiting, double Draw, double Gpu, double Presenting) Milliseconds => (update, waiting, draw, gpu, Math.Max(0, afterSubmit - gpu));

    /// <summary>
    /// Notes a frame the draw just handed over, all <see cref="Stopwatch"/> timestamps: when the update it shows began,
    /// when that update finished, when the draw took it, and when the draw had handed it over.
    /// </summary>
    public void AddDrawn(long content, long updated, long taken, long submitted)
    {
        contentTicks[nextDrawn] = content;
        updatedTicks[nextDrawn] = updated;
        takenTicks[nextDrawn] = taken;
        submittedTicks[nextDrawn] = submitted;
        nextDrawn = (nextDrawn + 1) % drawn_frames_kept;
    }

    /// <summary>
    /// Splits a frame the renderer saw on screen, where it's one of the frames drawn lately.
    /// </summary>
    public void AddShown(ShownFrame frame)
    {
        int index = findDrawn(frame.Content, frame.Shown);

        if (index < 0)
            return;

        long submitted = submittedTicks[index];
        double frameUpdate = (updatedTicks[index] - frame.Content) * ms_per_tick;
        double frameWaiting = (takenTicks[index] - updatedTicks[index]) * ms_per_tick;
        double frameDraw = (submitted - takenTicks[index]) * ms_per_tick;
        double frameAfterSubmit = (frame.Shown - submitted) * ms_per_tick;
        double total = frameUpdate + frameWaiting + frameDraw + frameAfterSubmit;

        if (total <= 0 || total > max_ms || frameUpdate < 0 || frameWaiting < 0 || frameDraw < 0)
            return;

        // Settled from the frames that have it, so presenting is what's left of the time after the draw.
        bool frameGpuKnown = frame.GpuDone >= submitted && frame.GpuDone <= frame.Shown;
        double frameGpu = frameGpuKnown ? (frame.GpuDone - submitted) * ms_per_tick : 0;

        if (readings == 0)
        {
            update = frameUpdate;
            waiting = frameWaiting;
            draw = frameDraw;
            afterSubmit = frameAfterSubmit;
        }
        else
        {
            update += (frameUpdate - update) * smoothing;
            waiting += (frameWaiting - waiting) * smoothing;
            draw += (frameDraw - draw) * smoothing;
            afterSubmit += (frameAfterSubmit - afterSubmit) * smoothing;
        }

        if (frameGpuKnown)
        {
            gpu = gpuKnown ? gpu + (frameGpu - gpu) * smoothing : frameGpu;
            gpuKnown = true;
        }

        var parts = Milliseconds;

        stat_update.Value = Math.Round(parts.Update, 1);
        stat_waiting.Value = Math.Round(parts.Waiting, 1);
        stat_draw.Value = Math.Round(parts.Draw, 1);
        stat_gpu.Value = Math.Round(parts.Gpu, 1);
        stat_presenting.Value = Math.Round(parts.Presenting, 1);

        if (++readings == readings_to_log)
        {
            string afterDraw = gpuKnown
                ? $"{parts.Gpu:0.0} ms on the GPU, {parts.Presenting:0.0} ms presenting"
                : $"{parts.Presenting:0.0} ms from handed over to shown";

            Logger.Verbose($"Frames reach the screen {update + waiting + draw + afterSubmit:0.0} ms after their update began: {parts.Update:0.0} ms updating, "
                           + $"{parts.Waiting:0.0} ms waiting for the draw, {parts.Draw:0.0} ms drawing, {afterDraw}");
        }
    }

    /// <summary>
    /// The latest frame drawn from the update that began at <paramref name="content"/> and handed over by
    /// <paramref name="shown"/>, or -1. The same update is drawn again when no newer one is ready; the latest of those
    /// handed over in time is the one that can have been shown.
    /// </summary>
    private int findDrawn(long content, long shown)
    {
        for (int i = 1; i <= drawn_frames_kept; i++)
        {
            int index = (nextDrawn - i + drawn_frames_kept) % drawn_frames_kept;

            if (contentTicks[index] == content && submittedTicks[index] <= shown)
                return index;
        }

        return -1;
    }
}
