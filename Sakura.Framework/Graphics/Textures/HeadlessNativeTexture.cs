// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Textures;

public sealed class HeadlessNativeTexture : INativeTexture
{
    public nint Handle { get; } = 1;
    public int Width { get; }
    public int Height { get; }
    public bool Available => true;

    public HeadlessNativeTexture(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void Upload(ReadOnlySpan<byte> data) { }
    public void UploadRegion(int x, int y, int width, int height, ReadOnlySpan<byte> data) { }
    public void Bind(int slot = 0) { }
    public void Dispose() { }
}
