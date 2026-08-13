// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using FFmpeg.AutoGen;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// No-op <see cref="INativeVideoTexture"/> used by the headless renderer.
/// All operations are silently ignored.
/// </summary>
internal sealed class HeadlessNativeVideoTexture : INativeVideoTexture
{
    public int Width { get; }
    public int Height { get; }
    public bool Available => false;

    /// <summary>
    /// Note: it's always zero since nothing is bound headlessly, so there is nothing to count.
    /// </summary>
    public TextureBindCounter Binds { get; } = new TextureBindCounter();

    public HeadlessNativeVideoTexture(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void BindPlanes(bool tiling) { }
    public unsafe void Upload(AVFrame* frame) { }
    public void MarkAvailable() { }
    public void Dispose() { }
}
