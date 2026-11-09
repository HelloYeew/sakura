// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Rendering;

namespace Sakura.Framework.Statistic;

/// <summary>
/// Every frame the renderer saw on screen, as it was taken on the draw thread: when its update began, when the GPU
/// finished it, and when it was shown. A thread's own timings say when each frame was started; these say when the
/// screen changed, which is what a missed refresh is.
/// </summary>
/// <remarks>
/// Only where the renderer can tell when a frame was shown (<see cref="IRenderer.TryTakeShownFrame"/>), and in step
/// with the draw thread: a frame is in here once the renderer has heard it was shown, a frame or two after it was drawn.
/// </remarks>
public sealed class ShownFrameStatistics
{
    /// <summary>
    /// Number of frames retained: over four seconds at 120 Hz.
    /// </summary>
    public const int CAPACITY = 512;

    private readonly FrameRing<ShownFrame> ring = new FrameRing<ShownFrame>(CAPACITY);

    /// <summary>
    /// Total frames recorded since startup. Also the cursor to start from for only the frames from now on.
    /// </summary>
    public long TotalFrames => ring.TotalFrames;

    /// <summary>
    /// Records a frame seen on screen. Only the draw thread calls it.
    /// </summary>
    internal void Record(in ShownFrame frame) => ring.Record(frame);

    /// <summary>
    /// Copies every frame recorded since <paramref name="cursor"/> into <paramref name="destination"/>, oldest first,
    /// and advances the cursor past them. Safe from any thread, as <see cref="ThreadFrameStatistics.Drain"/> is.
    /// </summary>
    /// <param name="destination">Receives the frames. Frames beyond its length are dropped, the oldest first.</param>
    /// <param name="cursor">The caller's read position, advanced by this call. Start it at <see cref="TotalFrames"/>.</param>
    /// <param name="skipped">Frames recorded that couldn't be handed back, because the caller fell too far behind.</param>
    /// <returns>The number of frames written to <paramref name="destination"/>.</returns>
    public int Drain(Span<ShownFrame> destination, ref long cursor, out long skipped) => ring.Drain(destination, ref cursor, out skipped);
}
