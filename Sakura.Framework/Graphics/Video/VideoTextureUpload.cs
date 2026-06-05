// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// Carries a decoded YUV420P frame to the render thread for upload.
/// The actual GPU upload is delegated to <see cref="INativeVideoTexture.Upload"/>
/// so this class has no knowledge of the backend.
/// Disposing returns the underlying <see cref="FFmpegFrame"/> to its pool.
/// </summary>
public sealed unsafe class VideoTextureUpload : IDisposable
{
    private readonly FFmpegFrame frame;
    private bool disposed;

    public AVFrame* Frame => frame.Pointer;

    public int Width => Frame->width;
    public int Height => Frame->height;

    internal VideoTextureUpload(FFmpegFrame frame)
    {
        this.frame = frame;
    }

    /// <summary>
    /// Uploads the frame into <paramref name="target"/>. Must be called on the render thread.
    /// </summary>
    public void Upload(INativeVideoTexture target) => target.Upload(Frame);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        frame.Return();
    }
}
