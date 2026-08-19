// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// An <see cref="IPcmSource"/> cursor over a shared, fully decoded <see cref="PcmBuffer"/>.
/// </summary>
internal sealed class MemoryPcmSource : IPcmSource
{
    private readonly PcmBuffer buffer;

    /// <summary>
    /// Invoked on <see cref="Dispose"/> so the owning sample can drop its reference count.
    /// </summary>
    private readonly Action? onDisposed;

    private int framePosition;
    private bool isDisposed;

    public double LengthMs => buffer.LengthMs;

    public double PositionMs => framePosition / (double)buffer.SampleRate * 1000.0;

    public bool Ended => framePosition >= buffer.FrameCount;

    public MemoryPcmSource(PcmBuffer buffer, Action? onDisposed = null)
    {
        this.buffer = buffer;
        this.onDisposed = onDisposed;
    }

    public int ReadFrames(Span<float> destination, int frameCount)
    {
        if (isDisposed || frameCount <= 0)
            return 0;

        int channels = buffer.Channels;
        int available = Math.Min(frameCount, buffer.FrameCount - framePosition);
        available = Math.Min(available, destination.Length / channels);

        if (available <= 0)
            return 0;

        buffer.Samples.AsSpan(framePosition * channels, available * channels).CopyTo(destination);
        framePosition += available;

        return available;
    }

    public void Seek(double milliseconds)
    {
        if (isDisposed)
            return;

        int frame = (int)(Math.Max(0, milliseconds) / 1000.0 * buffer.SampleRate);
        framePosition = Math.Clamp(frame, 0, buffer.FrameCount);
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        onDisposed?.Invoke();
    }
}
