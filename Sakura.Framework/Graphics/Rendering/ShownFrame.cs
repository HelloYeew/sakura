// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// A frame a renderer measured on screen (<see cref="IRenderer.TryTakeShownFrame"/>), as
/// <see cref="System.Diagnostics.Stopwatch"/> timestamps.
/// </summary>
public readonly struct ShownFrame
{
    /// <summary>
    /// When the update the frame shows began: what <see cref="IRenderer.SetFrameContentTime"/> was given.
    /// </summary>
    public long Content { get; }

    /// <summary>
    /// When the GPU finished drawing the frame, or 0 where the renderer can't tell.
    /// </summary>
    public long GpuDone { get; }

    /// <summary>
    /// When the frame was on screen.
    /// </summary>
    public long Shown { get; }

    /// <summary>
    /// A frame shown at <paramref name="shown"/>, from the update that began at <paramref name="content"/>, finished on
    /// the GPU at <paramref name="gpuDone"/> (0 where not known).
    /// </summary>
    public ShownFrame(long content, long gpuDone, long shown)
    {
        Content = content;
        GpuDone = gpuDone;
        Shown = shown;
    }
}
