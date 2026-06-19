// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
///     Decodes and displays a video using GPU-side YUV→RGB conversion.
/// </summary>
/// <remarks>
///     <para>
///         <b>Time model:</b> the decoder produces frames with PTS timestamps in milliseconds.
///         <see cref="VideoSprite"/> tracks a playback clock that starts at 0 and increments with
///         real elapsed time. Frames are displayed when their PTS (offset-adjusted to start from 0)
///         is &lt;= the playback clock.
///     </para>
///     <para>
///         <b>Threading:</b>
///         <list type="bullet">
///             <item>FFmpeg decode runs on a dedicated <c>LongRunning</c> task thread.</item>
///             <item>GPU upload runs inside <c>ScheduleToDrawThread</c> (draw thread).</item>
///             <item><c>VideoSprite.Update()</c> runs on the update thread — no GL calls.</item>
///             <item><c>VideoDrawNode.Draw()</c> runs on the draw thread — all GL calls.</item>
///         </list>
///     </para>
/// </remarks>
public partial class VideoSprite : Drawable, IDisposable
{
    private readonly string? filePath;
    private readonly Stream? stream;
    private readonly VideoDecoder? providedDecoder;
    private bool syncToClock;

    private VideoDecoder decoder = null!;
    private IShader? videoShader;

    private readonly Queue<DecodedFrame> availableFrames = new();
    private DecodedFrame? lastFrame;

    // Passed to VideoDrawNode every GenerateDrawNodeSubtree call (update thread write,
    // draw thread read — triple-buffered draw nodes make this safe).
    private VideoTexture? currentVideoTexture;
    private VideoTexture? lastUploadedTexture; // last texture confirmed uploaded — fallback for draw node
    private float[]? currentMatrix;

    // Playback clock list
    // - seekBaseMs: the absolute video position (ms, 0-based) we last seeked to. Stays constant between seeks.
    // - elapsedMs: how much real time has elapsed since the last seek. Incremented by ElapsedFrameTime each update while playing.
    // - CurrentTime: seekBaseMs + elapsedMs — the current absolute video position.
    //
    // - ptsBias: PTS of the first frame received after a seek.
    //   targetPts = CurrentTime + ptsBias is what we compare frame PTSes against.
    //   This is because the decoder's PTS timestamps are not 0-based — they start at some offset baked into the container.
    private double seekBaseMs;
    private double elapsedMs;
    private double ptsBias = double.NaN;

    public bool IsPlaying { get; private set; }
    public bool Buffering { get; private set; }

    /// <summary>
    /// When <see langword="true"/>, video playback time is driven by the drawable's
    /// <see cref="Clock"/> rather than the internal real-time accumulator.
    /// This keeps the video frame perfectly in sync with whatever clock is assigned to
    /// this drawable — an audio track clock, the gameplay clock, or a rate-scaled test
    /// clock without any manual resync loop. When false (default) the video is self-contained
    /// and uses its own elapsed-time accumulator.
    /// <remarks>
    /// <see cref="Seek"/> and <see cref="IsPlaying"/> still work normally when enabled.
    /// The internal accumulator is not used, so there is no drift between the video
    /// and the master clock regardless of frame timing jitter.
    /// </remarks>
    /// </summary>
    public bool SyncToClock
    {
        get => syncToClock;
        set
        {
            syncToClock = value;
            // Reset the jump-detection baseline so the first frame after switching
            // modes doesn't falsely look like a seek.
            lastSyncTime = double.NaN;
        }
    }

    /// <summary>
    /// Gets the current playback position of the video in milliseconds.
    /// </summary>
    /// <remarks>
    /// When <see cref="SyncToClock"/> is <see langword="false"/> (default), this is
    /// driven by an internal accumulator: <c>seekBaseMs + elapsedMs</c>.
    /// When <see cref="SyncToClock"/> is <see langword="true"/>, this returns
    /// <c>Clock.CurrentTime</c> directly — no manual resync code needed.
    /// <para>
    /// This property is <see langword="virtual"/> for easier customization.
    /// </para>
    /// </remarks>
    public virtual double CurrentTime => SyncToClock ? Clock.CurrentTime : seekBaseMs + elapsedMs;

    public double Duration => decoder?.Duration ?? 0;
    public double OriginalWidth => decoder?.Width ?? 0;
    public double OriginalHeight => decoder?.Height ?? 0;

    public VideoSprite(string filePath)
    {
        this.filePath = filePath;
    }

    public VideoSprite(Stream stream)
    {
        this.stream = stream;
    }

    public VideoSprite(VideoDecoder decoder)
    {
        providedDecoder = decoder;
    }

    private IRenderer renderer = null!;

    [BackgroundDependencyLoader]
    private void load(AppHost host, ITextureManager textureManager, Configurations.FrameworkConfigManager config)
    {
        renderer = host.Renderer;

        decoder = providedDecoder
                  ?? (stream != null
                         ? new VideoDecoder(renderer, textureManager, stream)
                         : new VideoDecoder(renderer, textureManager, filePath!));

        decoder.HardwareAcceleration.BindTo(config.Get<bool>(Configurations.FrameworkSetting.HardwareAcceleration));

        // Shader must be compiled on the draw thread (GL context owner in multi-thread mode).
        renderer.ScheduleToDrawThread(() =>
        {
            videoShader = renderer.CreateShader(renderer.ShaderStorage, "video.vert", "video.frag");
        });

        decoder.Start();

        // Auto-dispose when removed from the scene so the decoder, FFmpeg contexts,
        // and video texture pool are always cleaned up without requiring explicit Dispose() calls.
        DisposeOnRemoval = true;

        Alpha = 0f;
        IsPlaying = true;
    }

    protected override DrawNode CreateDrawNode() => new VideoDrawNode();

    public override DrawNode GenerateDrawNodeSubtree(int frameIndex)
    {
        var node = base.GenerateDrawNodeSubtree(frameIndex) as VideoDrawNode;
        node?.ApplyVideoState(currentVideoTexture, currentMatrix, videoShader);
        return node!;
    }

    public void Play() => IsPlaying = true;
    public void Pause() => IsPlaying = false;

    public void Stop()
    {
        IsPlaying = false;
        seekTo(0);
    }

    /// <summary>
    /// Seeks to <paramref name="timeMs"/> milliseconds from the start of the video.
    /// </summary>
    public void Seek(double timeMs)
    {
        double clamped = Duration > 0 ? Math.Clamp(timeMs, 0, Duration) : Math.Max(0, timeMs);
        seekTo(clamped);
    }

    private void seekTo(double absoluteMs)
    {
        if (decoder == null || decoder.State == VideoDecoder.DecoderState.Preparing)
            return;

        // seekBaseMs records where in the video we seeked to.
        // elapsedMs resets to 0 — it will accumulate real time from this new position.
        seekBaseMs = absoluteMs;
        elapsedMs = 0;

        // Reset jump-detection baseline so the frame right after a seek is not
        // mistaken for another seek in SyncToClock mode.
        lastSyncTime = double.NaN;

        // The decoder's Seek() takes a 0-based absolute position in milliseconds. It internally
        // adds the container's start_time when converting to a stream timestamp and subtracts it
        // again when comparing decoded frame times against the skip target, so the start offset
        // is fully handled decoder-side. We must NOT add ptsBias here — doing so double-counts
        // the container start offset and makes every seek overshoot (the "seek lands elsewhere"
        // bug). Pass the plain absolute position.
        decoder.Seek(absoluteMs);

        // Flush all buffered frames — they belong to the old position.
        decoder.ReturnFrames(availableFrames);
        availableFrames.Clear();

        // Reset ptsBias — will be re-established from the first frame after this seek.
        // This is safe because elapsedMs=0 and seekBaseMs=absoluteMs, so even if the
        // first frame arrives with a slightly different PTS we'll re-anchor correctly.
        ptsBias = double.NaN;
    }

    /// <summary>
    /// Last clock time seen in <see cref="SyncToClock"/> mode, used to detect external seeks.
    /// </summary>
    private double lastSyncTime = double.NaN;

    /// <summary>
    /// How far the clock must jump (in ms) before it is treated as a seek rather
    /// than normal elapsed time in <see cref="SyncToClock"/> mode.
    /// Covers one worst-case audio buffer step (~10 ms) plus a frame at 30 fps (~33 ms).
    /// </summary>
    private const double sync_seek_threshold_ms = 50.0;

    public override void Update()
    {
        base.Update();

        if (!IsLoaded || decoder == null || decoder.State == VideoDecoder.DecoderState.Preparing) return;

        if (syncToClock)
        {
            double clockTime = Clock.CurrentTime;

            if (!double.IsNaN(lastSyncTime))
            {
                double delta = clockTime - lastSyncTime;

                // A jump larger than the threshold (backwards or large forward) means the
                // master clock was seeked externally — flush and re-anchor the decoder.
                if (Math.Abs(delta) > sync_seek_threshold_ms)
                    seekTo(Math.Max(0, clockTime));
            }

            lastSyncTime = clockTime;
            // No accumulator to advance — CurrentTime returns Clock.CurrentTime directly.
        }
        else
        {
            // Internal accumulator mode: advance elapsed time, capped at 100 ms to
            // avoid large jumps during GC pauses or OS scheduler hiccups.
            if (IsPlaying)
            {
                elapsedMs += Math.Min(Clock.ElapsedFrameTime, 100.0);
                if (Duration > 0 && seekBaseMs + elapsedMs >= Duration)
                    elapsedMs = Duration - seekBaseMs;
            }
        }

        // Pull newly decoded frames and schedule their GL upload on the draw thread.
        // decodedFrames are enqueued immediately by the decode thread after SetData(),
        // so we see them here without waiting for the draw thread.
        //
        // Each frame is stamped with the decoder's seek generation at decode time. Any frame
        // whose generation no longer matches the decoder's current generation belongs to a
        // position we have seeked away from — discard it (returning its texture) so it can
        // never be displayed or used to (re)anchor ptsBias. This is what stops seek/reverse
        // from jumping to the wrong place or freezing on a stale frame.
        int currentGeneration = decoder.SeekGeneration;
        foreach (var f in decoder.GetDecodedFrames())
        {
            if (f.Generation != currentGeneration)
            {
                decoder.ReturnFrames(new[] { f });
                continue;
            }

            availableFrames.Enqueue(f);

            // Schedule the actual GL upload for this frame's NativeTexture.
            // Runs in StartFrame() before any Draw() calls — safe GL ordering.
            var capturedVt = f.NativeTexture;
            renderer.ScheduleToDrawThread(() => capturedVt.FlushIfPending());
        }

        // Establish ptsBias from the first frame received after a seek.
        // ptsBias = firstFramePts - seekBaseMs
        // This means: targetPts = seekBaseMs + elapsedMs + ptsBias
        //           = seekBaseMs + elapsedMs + (firstFramePts - seekBaseMs)
        //           = firstFramePts + elapsedMs (tracks correctly from any seek point)
        if (double.IsNaN(ptsBias) && availableFrames.Count > 0)
            ptsBias = availableFrames.Peek().Time - seekBaseMs;

        if (!double.IsNaN(ptsBias))
        {
            // Raw PTS value that corresponds to the current playback position.
            double targetPts = CurrentTime + ptsBias;

            // Advance lastFrame to the newest frame whose PTS <= targetPts.
            while (availableFrames.Count > 0 && availableFrames.Peek().Time <= targetPts)
            {
                if (lastFrame != null)
                    decoder.ReturnFrames(new[] { lastFrame });

                lastFrame = availableFrames.Dequeue();
                GlobalStatistics.Get<int>("Video", "Frames Displayed").Value++;
            }

            // Count frames still queued ahead of playback position as skipped
            // (they arrived too late and will be superseded next update).
            int skipped = 0;
            foreach (var f in availableFrames)
            {
                if (f.Time < targetPts) skipped++;
                else break;
            }
            if (skipped > 0)
                GlobalStatistics.Get<int>("Video", "Frames Skipped").Value += skipped;
        }

        if (lastFrame != null)
        {
            var vt = lastFrame.NativeTexture;
            currentMatrix = decoder.GetConversionMatrix();

            // If the new frame's upload is already complete, use it directly.
            // Otherwise fall back to the last confirmed-uploaded texture — no black frames.
            if (vt.UploadComplete)
                lastUploadedTexture = vt;

            currentVideoTexture = lastUploadedTexture ?? vt;

            // Set Drawable.Texture so GenerateVertices() has correct Width/Height for FillMode.
            // This is now a dimension-only proxy with no GL handles.
            if (Texture != lastFrame.Texture)
                Texture = lastFrame.Texture;

            if (Alpha == 0f && lastUploadedTexture != null)
                Alpha = 1f;
        }

        Buffering = decoder.IsRunning && availableFrames.Count == 0 && lastFrame == null;

        GlobalStatistics.Get<bool>("Video", "Buffering").Value = Buffering;
        GlobalStatistics.Get<double>("Video", "Playback Position (ms)").Value = Math.Round(CurrentTime, 1);
        GlobalStatistics.Get<int>("Video", "Queue Depth").Value = availableFrames.Count;
    }

    private bool isDisposed;

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        if (decoder != null)
        {
            decoder.ReturnFrames(availableFrames);
            availableFrames.Clear();

            if (lastFrame != null)
            {
                decoder.ReturnFrames(new[] { lastFrame });
                lastFrame = null;
            }

            decoder.Dispose(); // internally schedules GL texture disposal on draw thread
        }

        // gl.DeleteProgram must run on the draw thread.
        if (videoShader != null)
        {
            var shader = videoShader;
            videoShader = null;
            renderer?.ScheduleToDrawThread(shader.Dispose);
        }
    }
}
