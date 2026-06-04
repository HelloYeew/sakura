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
/// The actual upload is performed lazily on the draw thread the first time this texture
/// is about to be drawn. <see cref="UploadComplete"/> becomes true after the upload.
/// </summary>
public sealed class VideoTexture : IVideoTexture
{
    /// <summary>
    /// The GL-specific YUV plane container. Kept internal to the video layer;
    /// higher-level code uses <see cref="BindPlanes"/> and <see cref="UploadComplete"/>.
    /// </summary>
    internal VideoGLTexture GlTexture { get; }

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

    private volatile VideoTextureUpload? pendingUpload;

    private readonly ITextureManager textureManager;
    private readonly IRenderer renderer;
    private readonly GL gl;
    private bool isDisposed;

    public VideoTexture(IRenderer renderer, GL gl, ITextureManager textureManager, int width, int height)
    {
        this.renderer = renderer;
        this.gl = gl;
        this.textureManager = textureManager;
        GlTexture = new VideoGLTexture(gl, width, height);
        textureManager.RegisterVideoTexture(this);
    }

    /// <summary>
    /// Stores a pending upload. Called from the decode thread — no GL calls.
    /// </summary>
    public void SetData(VideoTextureUpload upload)
    {
        Volatile.Write(ref uploadComplete, false);
        pendingUpload = upload;
    }

    /// <summary>
    /// If there is a pending upload, performs it now. Must be called on the draw thread.
    /// </summary>
    public void FlushIfPending()
    {
        var upload = pendingUpload;
        if (upload == null) return;

        pendingUpload = null;
        upload.Upload(gl, GlTexture);
        upload.Dispose();
        Volatile.Write(ref uploadComplete, true);
        GlobalStatistics.Get<int>("Video", "Frames Uploaded").Value++;
    }

    /// <summary>
    /// Binds the Y, U, V planes to texture units 0, 1, 2.
    /// Must be called on the draw thread. Keeps all GL calls inside the video layer.
    /// </summary>
    public void BindPlanes() => GlTexture.BindPlanes();

    /// <summary>
    /// Called when this texture is returned to the pool for reuse.
    /// </summary>
    public void Reset()
    {
        var pending = pendingUpload;
        pendingUpload = null;
        pending?.Dispose();
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
