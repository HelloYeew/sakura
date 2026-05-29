// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Threading;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Statistic;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// A YUV420P video texture following the osu!framework upload pattern:
/// <see cref="SetData"/> stores the upload request on the decode thread (no GL calls).
/// The actual upload is performed lazily by <see cref="VideoDrawNode"/> on the draw thread
/// the first time this texture is about to be drawn.
/// <see cref="UploadComplete"/> becomes true after the draw thread performs the upload.
/// This means <see cref="VideoDecoder"/> can enqueue a <see cref="DecodedFrame"/> immediately
/// after <see cref="SetData"/> without waiting for the draw thread — eliminating the
/// decode→draw→update→draw latency chain that caused stuttering.
/// </summary>
public sealed class VideoTexture : IVideoTexture
{
    public VideoGLTexture GlTexture { get; }

    // IVideoTexture
    public int Width  => GlTexture.Width;
    public int Height => GlTexture.Height;
    public uint YPlaneHandle => GlTexture.YHandle;

    /// <summary>
    /// True once the draw thread has flushed the pending upload.
    /// Written via <see cref="Volatile"/> — safe to read from any thread.
    /// </summary>
    public bool UploadComplete => Volatile.Read(ref uploadComplete);
    private bool uploadComplete;

    /// <summary>
    /// Pending upload set by the decode thread, consumed by the draw thread.
    /// Volatile so the draw thread always sees the latest write.
    /// </summary>
    private volatile VideoTextureUpload? pendingUpload;

    private readonly ITextureManager textureManager;
    private bool isDisposed;

    /// <summary>
    /// Must be called on the draw thread (GL context owner).
    /// </summary>
    private readonly IRenderer renderer;

    public VideoTexture(IRenderer renderer, GL gl, ITextureManager textureManager, int width, int height)
    {
        this.renderer = renderer;
        this.textureManager = textureManager;
        GlTexture = new VideoGLTexture(gl, width, height);
        textureManager.RegisterVideoTexture(this);
    }

    /// <summary>
    /// Stores a pending upload. Called from the decode thread — no GL calls.
    /// The draw thread will flush this via <see cref="FlushIfPending"/>.
    /// </summary>
    public void SetData(VideoTextureUpload upload)
    {
        Volatile.Write(ref uploadComplete, false);
        pendingUpload = upload;
    }

    /// <summary>
    /// If there is a pending upload, performs it now. Must be called on the draw thread.
    /// Called by <see cref="VideoDrawNode"/> before drawing each frame.
    /// </summary>
    public void FlushIfPending(GL gl)
    {
        var upload = pendingUpload;
        if (upload == null) return;

        pendingUpload = null; // clear before upload so a racing SetData isn't lost
        upload.Upload(gl, GlTexture);
        upload.Dispose(); // returns FFmpegFrame to its pool
        Volatile.Write(ref uploadComplete, true);
        GlobalStatistics.Get<int>("Video", "Frames Uploaded").Value++;
    }

    /// <summary>
    /// Called when this texture is returned to the pool for reuse.
    /// Disposes any pending upload that hasn't been flushed yet.
    /// Does NOT reset UploadComplete — the old pixel data remains valid on the GPU
    /// until SetData() is called for the next frame, preventing black frames during
    /// the window between Reset() and the next upload completing.
    /// </summary>
    public void Reset()
    {
        var pending = pendingUpload;
        pendingUpload = null;
        pending?.Dispose(); // returns FFmpegFrame to native pool
        // UploadComplete intentionally NOT cleared here — cleared in SetData() instead.
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        pendingUpload?.Dispose();
        pendingUpload = null;

        textureManager.UnregisterVideoTexture(this);

        var tex = GlTexture;
        renderer.ScheduleToDrawThread(tex.Dispose);
    }
}
