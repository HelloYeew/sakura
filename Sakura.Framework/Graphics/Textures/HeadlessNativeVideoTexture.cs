// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Threading;
using FFmpeg.AutoGen;
using Sakura.Framework.Graphics.Video;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// No-op <see cref="INativeVideoTexture"/> used by the headless renderer.
/// All operations are silently ignored.
/// </summary>
internal sealed class HeadlessNativeVideoTexture : INativeVideoTexture
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Zero: nothing is allocated headlessly.
    /// </summary>
    public long PlaneBytes => 0;
    public VideoPlaneLayout Layout { get; }

    /// <summary>
    /// Tracked rather than hardcoded false: <see cref="VideoTexture.UploadComplete"/> is derived from
    /// this, so a stub that never reports available would make every headless frame look like a failed
    /// upload. Nothing is bound here, but "a frame was handed over" is still a true statement.
    /// </summary>
    public bool Available => Volatile.Read(ref available);
    private bool available;

    /// <summary>
    /// Note: it's always zero since nothing is bound headlessly, so there is nothing to count.
    /// </summary>
    public TextureBindCounter Binds { get; } = new TextureBindCounter();

    public HeadlessNativeVideoTexture(int width, int height, VideoPlaneLayout layout = VideoPlaneLayout.Yuv420P)
    {
        Width = width;
        Height = height;
        Layout = layout;
    }

    public void BindPlanes(bool tiling) { }
    public unsafe void Upload(AVFrame* frame) => MarkAvailable();
    public void MarkAvailable() => Volatile.Write(ref available, true);
    public void Dispose() { }
}
