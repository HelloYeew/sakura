// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// The raw image data that's ready for renderer to consume.
/// </summary>
public readonly struct ImageRawData : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; } // TODO: Maybe change to IMemoryOwner<byte> for better memory management?

    public ImageRawData(int width, int height, byte[] data)
    {
        Width = width;
        Height = height;
        Data = data;
    }

    public void Dispose()
    {
        // If using IMemoryOwner<byte>, we would call Dispose on it here.
        // Currently, nothing to dispose since we're using a byte array.
    }
}
