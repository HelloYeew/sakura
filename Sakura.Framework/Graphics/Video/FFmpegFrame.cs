// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using FFmpeg.AutoGen;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// A lightweight wrapper around a native AVFrame* that supports optional return-to-pool semantics.
/// When a return delegate is provided, calling <see cref="Return"/> hands the frame back to the pool
/// instead of freeing it, eliminating per-frame native allocations in the hot path.
/// </summary>
internal sealed unsafe class FFmpegFrame : IDisposable
{
    public AVFrame* Pointer { get; }

    public AVPixelFormat PixelFormat
    {
        get => (AVPixelFormat)Pointer->format;
        set => Pointer->format = (int)value;
    }

    private readonly Action<FFmpegFrame>? returnDelegate;
    private bool disposed;

    /// <summary>
    /// Creates a new frame, allocating a native AVFrame.
    /// </summary>
    /// <param name="returnDelegate">
    /// Optional pool-return callback. When not null, <see cref="Return"/> calls this instead of freeing.
    /// </param>
    internal FFmpegFrame(Action<FFmpegFrame>? returnDelegate = null)
    {
        Pointer = ffmpeg.av_frame_alloc();
        this.returnDelegate = returnDelegate;
    }

    /// <summary>
    /// Returns this frame to its pool if a return delegate was provided, otherwise disposes it.
    /// Always call this instead of <see cref="Dispose"/> when done with a frame in the decode loop.
    /// </summary>
    public void Return()
    {
        Debug.Assert(Pointer != null);

        if (returnDelegate != null)
        {
            ffmpeg.av_frame_unref(Pointer);
            returnDelegate(this);
        }
        else
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (Pointer != null)
        {
            var ptr = Pointer;
            ffmpeg.av_frame_free(&ptr);
        }

        GC.SuppressFinalize(this);
    }
}
