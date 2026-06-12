// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Threading;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// A YUV420P video texture.
/// <see cref="SetData"/> stores the upload request on the decode thread (no render-thread calls).
/// The actual upload is performed lazily on the render thread the first time this texture
/// is about to be drawn. <see cref="UploadComplete"/> becomes true after the upload.
/// </summary>
public sealed class VideoTexture : IVideoTexture
{
    /// <summary>
    /// The backend-specific YUV plane container.
    /// Higher-level code uses <see cref="BindPlanes"/> and <see cref="UploadComplete"/>.
    /// </summary>
    internal INativeVideoTexture NativeTexture { get; }

    public int Width  => NativeTexture.Width;
    public int Height => NativeTexture.Height;

    /// <summary>
    /// True once the render thread has flushed the pending upload.
    /// Written via <see cref="Volatile"/> — safe to read from any thread.
    /// </summary>
    public bool UploadComplete => Volatile.Read(ref uploadComplete);
    private bool uploadComplete;

    private volatile VideoTextureUpload? pendingUpload;

    private readonly ITextureManager textureManager;
    private readonly IRenderer renderer;
    private bool isDisposed;

    public VideoTexture(IRenderer renderer, ITextureManager textureManager, int width, int height)
    {
        this.renderer = renderer;
        this.textureManager = textureManager;
        NativeTexture = renderer.CreateVideoTexture(width, height);
        textureManager.RegisterVideoTexture(this);
    }

    /// <summary>
    /// Stores a pending upload. Called from the decode thread — no render-thread calls.
    /// </summary>
    public void SetData(VideoTextureUpload upload)
    {
        Volatile.Write(ref uploadComplete, false);
        pendingUpload = upload;
    }

    /// <summary>
    /// If there is a pending upload, performs it now. Must be called on the render thread.
    /// </summary>
    public void FlushIfPending()
    {
        var upload = pendingUpload;
        if (upload == null) return;

        pendingUpload = null;
        upload.Upload(NativeTexture);
        upload.Dispose();
        Volatile.Write(ref uploadComplete, true);
        GlobalStatistics.Get<int>("Video", "Frames Uploaded").Value++;
    }

    /// <summary>
    /// Binds the Y, U, V planes to the appropriate texture slots for the current backend.
    /// Must be called on the render thread.
    /// </summary>
    public void BindPlanes() => NativeTexture.BindPlanes();

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

        var tex = NativeTexture;
        renderer.ScheduleToDrawThread(tex.Dispose);
    }
}
