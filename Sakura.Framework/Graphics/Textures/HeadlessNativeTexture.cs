// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Textures;

public sealed class HeadlessNativeTexture : INativeTexture
{
    private nint handle = 1;

    public nint Handle => handle;
    public int Width { get; }
    public int Height { get; }

    // There is no upload to wait for headlessly but a released texture must stop
    // reporting itself as available. Just match the real backend.
    public bool Available { get; private set; } = true;

    public HeadlessNativeTexture(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void Upload(ReadOnlySpan<byte> data) { }
    public void UploadRegion(int x, int y, int width, int height, ReadOnlySpan<byte> data) { }
    public void Bind(int slot = 0) { }

    public void Dispose()
    {
        handle = 0;
        Available = false;
    }
}
