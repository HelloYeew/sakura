// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ObjectExtensions;
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
///         <b>Time model:</b> the decoder produces frames timed in milliseconds from the start of the
///         video (it takes the container's start offset off). A frame is displayed once the playback
///         position, <see cref="CurrentTime"/>, reaches its time. The position is either the sprite's own,
///         advancing with real elapsed time, or its <see cref="Drawable.Clock"/>'s (<see cref="SyncToClock"/>).
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
public partial class VideoSprite : Drawable
{
    private readonly string? filePath;
    private readonly Stream? stream;
    private readonly VideoDecoder? providedDecoder;
    private bool syncToClock;

    private VideoDecoder decoder = null!;
    private VideoShaderSet? videoShaders;

    private readonly Queue<DecodedFrame> availableFrames = new();
    private DecodedFrame? lastFrame;

    // Passed to VideoDrawNode every GenerateDrawNodeSubtree call (update thread write,
    // draw thread read — triple-buffered draw nodes make this safe).
    private VideoTexture? currentVideoTexture;
    private VideoTexture? lastUploadedTexture; // last texture confirmed uploaded — fallback for draw node
    private float[]? currentMatrix;

    /// <summary>
    /// The frame this sprite is drawing now, for a <see cref="VideoMirror"/> to draw again.
    /// </summary>
    internal IVideoTexture? DisplayedTexture => currentVideoTexture;

    /// <summary>
    /// The YUV→RGB matrix for <see cref="DisplayedTexture"/>.
    /// </summary>
    internal float[]? DisplayedMatrix => currentMatrix;

    /// <summary>
    /// How many times this sprite has updated, so a mirror can say when it was last drawn.
    /// </summary>
    internal long Updates { get; private set; }

    // Playback clock list
    // - seekBaseMs: the absolute video position (ms, 0-based) we last seeked to. Stays constant between seeks.
    // - elapsedMs: how much real time has elapsed since the last seek. Incremented by ElapsedFrameTime each update while playing.
    // - CurrentTime: seekBaseMs + elapsedMs — the current absolute video position.
    private double seekBaseMs;
    private double elapsedMs;

    /// <summary>
    /// Whether no frame from the last seek has been displayed yet. The first one goes up as soon as it arrives, even if
    /// it's timed after the position: a seek that lands short of a keyframe resumes at the next one, and until the position
    /// gets there that's the nearest frame there is. Shown by its time instead, the frame from before the seek would stay up.
    /// </summary>
    private bool awaitingSeekFrame = true;

    /// <summary>
    /// How far the video has played since the last seek: the seek's position, or the latest frame displayed because the
    /// position reached it. In <see cref="SyncToClock"/> mode, a clock this far behind it has been moved back.
    /// </summary>
    private double playedToMs;

    /// <summary>
    /// The time of the newest frame queued since the last seek, or NaN.
    /// </summary>
    private double newestQueuedMs = double.NaN;

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
    /// The video plays at the clock's rate, stands still when the clock does, and follows it when it's
    /// moved: back by more than <see cref="sync_seek_back_ms"/>, or ahead by more than
    /// <see cref="sync_seek_ahead_ms"/> past what's decoded, is a seek. Anything less is played through, so a
    /// fast clock or a slow frame never makes the decoder start over. <see cref="IsPlaying"/> has no effect.
    /// </remarks>
    /// </summary>
    public bool SyncToClock
    {
        get => syncToClock;
        set => syncToClock = value;
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

    /// <summary>
    /// The time of the frame on screen, from the start of the video, or NaN before the first. Behind
    /// <see cref="CurrentTime"/> by up to a frame's length while playing; after a seek, ahead of it until the
    /// position reaches the first frame the seek found.
    /// </summary>
    internal double DisplayedFrameTime => lastFrame?.Time ?? double.NaN;

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

        // Bound rather than left at its default so the two paths can be compared live on one decode —
        // and so there is a switch to reach for if the interop misbehaves somewhere. Only has an effect
        // on a backend that offers zero-copy completely; see VideoDecoder.ZeroCopy.
        decoder.ZeroCopy.BindTo(config.Get<bool>(Configurations.FrameworkSetting.VideoZeroCopy));

        // Shaders must be compiled on the draw thread (GL context owner in multi-thread mode). Both
        // layout variants are built here because which one is needed is not known until the first
        // frame arrives — see VideoShaderSet.
        renderer.ScheduleToDrawThread(() =>
        {
            videoShaders = VideoShaderSet.Create(renderer);
        });

        decoder.Start();

        Alpha = 0f;
        IsPlaying = true;
    }

    protected override DrawNode CreateDrawNode() => new VideoDrawNode();

    public override DrawNode GenerateDrawNodeSubtree(int frameIndex)
    {
        var node = base.GenerateDrawNodeSubtree(frameIndex) as VideoDrawNode;
        // The shader follows the texture's own layout, so frames from before a format change keep
        // drawing correctly while they drain out of the queue.
        node?.ApplyVideoState(currentVideoTexture, currentMatrix,
            currentVideoTexture != null ? videoShaders?.For(currentVideoTexture.Layout) : null);

        // Record what this node is now holding, so a retired frame's texture is only returned to the
        // pool once no node still points at it
        if (frameIndex >= 0 && frameIndex < stampedTextures.Length)
            stampedTextures[frameIndex] = currentVideoTexture;

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
        if (decoder.IsNull() || decoder.State == VideoDecoder.DecoderState.Preparing)
            return;

        // seekBaseMs records where in the video we seeked to.
        // elapsedMs resets to 0 — it will accumulate real time from this new position.
        seekBaseMs = absoluteMs;
        elapsedMs = 0;

        // The decoder's Seek() takes a 0-based absolute position in milliseconds, as are the times of the frames it
        // produces: it adds the container's start_time when converting to a stream timestamp and subtracts it from
        // what it decodes, so the start offset is handled decoder-side.
        decoder.Seek(absoluteMs);

        // Flush all buffered frames — they belong to the old position.
        decoder.ReturnFrames(availableFrames);
        availableFrames.Clear();

        awaitingSeekFrame = true;
        playedToMs = absoluteMs;
        newestQueuedMs = double.NaN;
    }

    /// <summary>
    /// How far a <see cref="SyncToClock"/> clock has to go back from where the video has played to before the video seeks
    /// after it, rather than holding its frame until the clock catches up. Over a frame or two, so that a clock correcting
    /// itself by a little doesn't start the decoder over.
    /// </summary>
    private const double sync_seek_back_ms = 100;

    /// <summary>
    /// How far a <see cref="SyncToClock"/> clock has to be ahead of the newest decoded frame before the video seeks to it,
    /// rather than decoding its way there. Decoding through a second is quicker than a seek, which has to start from a
    /// keyframe, and it's what keeps a clock running fast (a song sped up) from seeking at all.
    /// </summary>
    private const double sync_seek_ahead_ms = 1000;

    /// <summary>
    /// How far a <see cref="SyncToClock"/> clock may get past a seek before its first frame has come before it's sought
    /// again: a decoder slow to land on a frame shouldn't be told to start over while it's still getting there.
    /// </summary>
    private const double sync_seek_ahead_while_seeking_ms = 3000;

    /// <summary>
    /// Seeks after the clock in <see cref="SyncToClock"/> mode when it has moved somewhere this video can't play its way to.
    /// </summary>
    private void followClock()
    {
        double position = Math.Max(0, Clock.CurrentTime);

        if (position < playedToMs - sync_seek_back_ms)
        {
            seekTo(position);
            return;
        }

        // Past the end, the video is at its last frame: sought there like anywhere else, rather than decoded through to it
        // all at once (a song whose preview starts after its video ends).
        double target = Duration > 0 ? Math.Min(position, Duration) : position;

        if (awaitingSeekFrame)
        {
            if (target > playedToMs + sync_seek_ahead_while_seeking_ms)
                seekTo(target);

            return;
        }

        if (decoder.State == VideoDecoder.DecoderState.EndOfStream && availableFrames.Count == 0)
            return;

        double newest = double.IsNaN(newestQueuedMs) ? lastFrame?.Time ?? playedToMs : newestQueuedMs;

        if (target > newest + sync_seek_ahead_ms)
            seekTo(target);
    }

    public override void Update()
    {
        base.Update();

        Updates++;

        if (!IsLoaded || decoder.IsNull() || decoder.State == VideoDecoder.DecoderState.Preparing) return;

        if (syncToClock)
        {
            // No accumulator to advance — CurrentTime returns Clock.CurrentTime directly.
            followClock();
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
        // never be displayed. This is what stops seek/reverse from jumping to the wrong place
        // or freezing on a stale frame.
        int currentGeneration = decoder.SeekGeneration;
        foreach (var f in decoder.GetDecodedFrames())
        {
            if (f.Generation != currentGeneration)
            {
                decoder.ReturnFrames(new[] { f });
                continue;
            }

            availableFrames.Enqueue(f);
            newestQueuedMs = f.Time;

            // Schedule the actual GL upload for this frame's NativeTexture.
            // Runs in StartFrame() before any Draw() calls — safe GL ordering.
            var capturedVt = f.NativeTexture;
            renderer.ScheduleToDrawThread(() => capturedVt.FlushIfPending());
        }

        // Frames are timed from the start of the video, as the position is, so each goes up once the position reaches it.
        // Not timed from the first frame after a seek, as they once were: a seek that landed late then put the whole video
        // that far ahead for good, where nothing could see it (a retry on a video whose first keyframe is a few frames in).
        double position = CurrentTime;
        int shown = 0;

        while (availableFrames.Count > 0 && availableFrames.Peek().Time <= position)
        {
            var frame = availableFrames.Dequeue();
            playedToMs = Math.Max(playedToMs, frame.Time);
            display(frame);
            shown++;
        }

        if (awaitingSeekFrame && shown == 0 && availableFrames.Count > 0)
        {
            display(availableFrames.Dequeue());
            shown++;
        }

        if (shown > 0)
            awaitingSeekFrame = false;

        // Frames that came up and went in one update were never drawn: the decoder fell behind the position.
        if (shown > 1)
            GlobalStatistics.Get<int>("Video", "Frames Skipped", StatisticKind.Cumulative).Value += shown - 1;

        if (lastFrame != null)
        {
            var vt = lastFrame.NativeTexture;
            // The texture's matrix is the one stamped on for the frame it actually holds, so it
            // reflects that frame's colour range; the decoder's is a stream-level fallback for before
            // any frame has landed.
            currentMatrix = vt.ConversionMatrix ?? decoder.GetConversionMatrix();

            // If the new frame's upload is already complete, use it directly.
            // Otherwise fall back to the last confirmed-uploaded texture — no black frames.
            if (vt.UploadComplete)
                lastUploadedTexture = vt;
            else
                // The frame due for display has not reached the draw thread yet, so this update draws
                // the previous texture again.
                GlobalStatistics.Get<long>("Video", "Fallback To Previous Texture", StatisticKind.Cumulative).Value++;

            currentVideoTexture = lastUploadedTexture ?? vt;

            // Set Drawable.Texture so GenerateVertices() has correct Width/Height for FillMode.
            // This is now a dimension-only proxy with no GL handles.
            if (Texture != lastFrame.Texture)
                Texture = lastFrame.Texture;

            if (Alpha == 0f && lastUploadedTexture != null)
                Alpha = 1f;
        }

        releaseRetiredFrames();

        Buffering = decoder.IsRunning && availableFrames.Count == 0 && lastFrame == null;

        GlobalStatistics.Get<bool>("Video", "Buffering").Value = Buffering;
        GlobalStatistics.Get<double>("Video", "Playback Position", StatisticKind.Gauge, StatisticUnit.Milliseconds).Value = Math.Round(CurrentTime, 1);
        GlobalStatistics.Get<int>("Video", "Queue Depth").Value = availableFrames.Count;
        GlobalStatistics.Get<int>("Video", "Frames Retiring").Value = retiringFrames.Count;
    }

    private void display(DecodedFrame frame)
    {
        if (lastFrame != null)
            retire(lastFrame);

        lastFrame = frame;
        GlobalStatistics.Get<int>("Video", "Frames Displayed", StatisticKind.Cumulative).Value++;
    }

    /// <summary>
    /// Frames that have left the display but whose textures may still be read by a draw node.
    /// </summary>
    private readonly List<DecodedFrame> retiringFrames = new List<DecodedFrame>();

    /// <summary>
    /// The texture handed to each of <c>Drawable</c>'s three buffered draw nodes, indexed the same way.
    /// </summary>
    private readonly IVideoTexture?[] stampedTextures = new IVideoTexture?[3];

    /// <summary>
    /// The mirrors drawing this sprite's frames. Their draw nodes can still hold a frame this sprite has
    /// moved on from, so a retired frame waits for them too. Update thread only.
    /// </summary>
    private readonly List<VideoMirror> mirrors = new List<VideoMirror>();

    internal void AddMirror(VideoMirror mirror) => mirrors.Add(mirror);

    internal void RemoveMirror(VideoMirror mirror) => mirrors.Remove(mirror);

    /// <summary>
    /// Takes a frame off the display without releasing its texture yet.
    /// </summary>
    private void retire(DecodedFrame frame) => retiringFrames.Add(frame);

    /// <summary>
    /// Returns every retired frame whose texture nothing can still be reading.
    /// </summary>
    /// <param name="force">
    /// Release regardless, for disposal, the draw nodes go with the sprite, so nothing survives to
    /// read the texture.
    /// </param>
    private void releaseRetiredFrames(bool force = false)
    {
        for (int i = retiringFrames.Count - 1; i >= 0; i--)
        {
            var frame = retiringFrames[i];

            if (!force && StillReferenced(frame.NativeTexture))
                continue;

            singleFrame[0] = frame;
            decoder.ReturnFrames(singleFrame);
            retiringFrames.RemoveAt(i);
        }

        singleFrame[0] = null!;
    }

    /// <summary>
    /// Whether anything can still read <paramref name="texture"/> like the display path, the fallback
    /// behind it, a draw node it was stamped onto, or a mirror.
    /// </summary>
    internal bool StillReferenced(IVideoTexture texture)
    {
        if (ReferenceEquals(texture, currentVideoTexture) || ReferenceEquals(texture, lastUploadedTexture))
            return true;

        foreach (var stamped in stampedTextures)
        {
            if (ReferenceEquals(texture, stamped))
                return true;
        }

        foreach (var mirror in mirrors)
        {
            if (mirror.Holds(texture, Updates))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reused so the per-frame release path does not allocate an array every time a frame is returned.
    /// Update thread only.
    /// </summary>
    private readonly DecodedFrame[] singleFrame = new DecodedFrame[1];

    /// <summary>
    /// Releases the decoder, its FFmpeg contexts and the video texture pool. Runs automatically when this
    /// sprite is removed from the scene (see <see cref="Container.Remove(Drawable, bool)"/>).
    /// </summary>
    protected override void Dispose(bool isDisposing)
    {
        if (IsDisposed) return;

        if (decoder.IsNotNull())
        {
            decoder.ReturnFrames(availableFrames);
            availableFrames.Clear();

            // Nothing outlives this sprite to read them, so the draw-node hold does not apply. A mirror
            // can, but the textures go with the decoder below, and a draw node skips a disposed texture.
            Array.Clear(stampedTextures);
            mirrors.Clear();
            releaseRetiredFrames(force: true);
            retiringFrames.Clear();

            if (lastFrame != null)
            {
                decoder.ReturnFrames(new[] { lastFrame });
                lastFrame = null;
            }

            currentVideoTexture = null;
            lastUploadedTexture = null;

            decoder.Dispose(); // internally schedules GL texture disposal on draw thread
        }

        // gl.DeleteProgram must run on the draw thread.
        if (videoShaders != null)
        {
            var shaders = videoShaders;
            videoShaders = null;
            renderer?.ScheduleToDrawThread(shaders.Dispose);
        }

        base.Dispose(isDisposing);
    }
}
