// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Video;

public class VideoFrame : IDisposable
{
    public double Timestamp { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] PixelData { get; set; }

    public void Dispose()
    {
        PixelData = null;
    }
}
