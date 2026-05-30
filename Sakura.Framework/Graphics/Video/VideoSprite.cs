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
using Silk.NET.OpenGL;
using Shader = Sakura.Framework.Graphics.Rendering.Shader;

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
public class VideoSprite : Drawable, IDisposable
{
    private readonly string? filePath;
    private readonly Stream? stream;
    private readonly VideoDecoder? providedDecoder;

    private VideoDecoder decoder = null!;
    private IShader? videoShader;
    private GL gl;

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
    /// Gets the current playback position of the video in milliseconds.
    /// </summary>
    /// <remarks>
    /// By default, this is driven by an internal clock that accumulates elapsed real-time
    /// since the last seek (<c>seekBaseMs</c> + <c>elapsedMs</c>).
    /// <para>
    /// This property is <see langword="virtual"/> so it can be overridden to synchronize video
    /// playback with an external master clock (such as an audio track's time). When overridden,
    /// the video's update loop will automatically drop or hold frames to maintain perfect sync
    /// with the provided time.
    /// </para>
    /// </remarks>
    public double CurrentTime => seekBaseMs + elapsedMs;

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
        gl = GLRenderer.GL;

        decoder = providedDecoder
                  ?? (stream != null
                         ? new VideoDecoder(renderer, gl, textureManager, stream)
                         : new VideoDecoder(renderer, gl, textureManager, filePath!));

        decoder.HardwareAcceleration.BindTo(config.Get<bool>(Configurations.FrameworkSetting.HardwareAcceleration));

        // Shader must be compiled on the draw thread (GL context owner in multi-thread mode).
        renderer.ScheduleToDrawThread(() =>
        {
            videoShader = new Shader(gl,
                "Resources/Shaders/video.vert",
                "Resources/Shaders/video.frag");
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
        node?.ApplyVideoState(this, currentVideoTexture, currentMatrix, videoShader, gl);
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
        if (decoder == null)
            return;

        // seekBaseMs records where in the video we seeked to.
        // elapsedMs resets to 0 — it will accumulate real time from this new position.
        seekBaseMs = absoluteMs;
        elapsedMs = 0;

        // The decoder's Seek() takes a raw PTS value.
        // If we know ptsBias (the container's PTS offset), use it.
        // Otherwise pass absoluteMs directly — the decoder will seek close enough
        // and ptsBias will be re-established from the first arriving frame.
        double targetPts = double.IsNaN(ptsBias) ? absoluteMs : absoluteMs + ptsBias;
        decoder.Seek(targetPts);

        // Flush all buffered frames — they belong to the old position.
        decoder.ReturnFrames(availableFrames);
        availableFrames.Clear();

        if (lastFrame != null)
        {
            decoder.ReturnFrames(new[] { lastFrame });
            lastFrame = null;
        }

        currentVideoTexture = null;
        lastUploadedTexture = null;

        // Reset ptsBias — will be re-established from the first frame after this seek.
        // This is safe because elapsedMs=0 and seekBaseMs=absoluteMs, so even if the
        // first frame arrives with a slightly different PTS we'll re-anchor correctly.
        ptsBias = double.NaN;
    }

    public override void Update()
    {
        base.Update();

        if (!IsLoaded || decoder == null) return;

        // Advance elapsed time. Cap per-update advance at 100ms to avoid
        // skipping frames during GC pauses or OS scheduler hiccups.
        if (IsPlaying)
        {
            elapsedMs += Math.Min(Clock.ElapsedFrameTime, 100.0);
            if (Duration > 0 && seekBaseMs + elapsedMs >= Duration)
                elapsedMs = Duration - seekBaseMs;
        }

        // Pull newly decoded frames and schedule their GL upload on the draw thread.
        // decodedFrames are enqueued immediately by the decode thread after SetData(),
        // so we see them here without waiting for the draw thread.
        foreach (var f in decoder.GetDecodedFrames())
        {
            availableFrames.Enqueue(f);

            // Schedule the actual GL upload for this frame's NativeTexture.
            // Runs in StartFrame() before any Draw() calls — safe GL ordering.
            var capturedVt = f.NativeTexture;
            var capturedGl = gl;
            renderer.ScheduleToDrawThread(() => capturedVt.FlushIfPending(capturedGl));
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
