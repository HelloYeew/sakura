// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// A fixed-capacity circular buffer of floats, written by a decode thread and read by the mix
/// thread.
/// </summary>
internal sealed class AudioRingBuffer
{
    private readonly Lock sync = new Lock();
    private readonly float[] buffer;

    private int readPosition;
    private int count;

    /// <summary>
    /// Total capacity in floats.
    /// </summary>
    public int Capacity => buffer.Length;

    /// <summary>
    /// Floats currently readable.
    /// </summary>
    public int Available
    {
        get
        {
            lock (sync)
                return count;
        }
    }

    /// <summary>
    /// Floats that can be written before the buffer is full.
    /// </summary>
    public int FreeSpace
    {
        get
        {
            lock (sync)
                return buffer.Length - count;
        }
    }

    public AudioRingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        buffer = new float[capacity];
    }

    /// <summary>
    /// Appends as much of <paramref name="source"/> as fits.
    /// </summary>
    /// <returns>
    /// The number of floats written. A short writing means the buffer is full and the caller should
    /// retain the remainder, dropping it would put a gap in the audio.
    /// </returns>
    public int Write(ReadOnlySpan<float> source)
    {
        if (source.IsEmpty)
            return 0;

        lock (sync)
        {
            int writable = Math.Min(source.Length, buffer.Length - count);

            if (writable == 0)
                return 0;

            int writePosition = (readPosition + count) % buffer.Length;
            int firstChunk = Math.Min(writable, buffer.Length - writePosition);

            source.Slice(0, firstChunk).CopyTo(buffer.AsSpan(writePosition));

            if (writable > firstChunk)
                source.Slice(firstChunk, writable - firstChunk).CopyTo(buffer);

            count += writable;
            return writable;
        }
    }

    /// <summary>
    /// Removes up to <paramref name="destination"/>'s length of floats from the front.
    /// </summary>
    /// <returns>The number of floats written, which is 0 when the buffer has run dry.</returns>
    public int Read(Span<float> destination)
    {
        if (destination.IsEmpty)
            return 0;

        lock (sync)
        {
            int readable = Math.Min(destination.Length, count);

            if (readable == 0)
                return 0;

            int firstChunk = Math.Min(readable, buffer.Length - readPosition);

            buffer.AsSpan(readPosition, firstChunk).CopyTo(destination);

            if (readable > firstChunk)
                buffer.AsSpan(0, readable - firstChunk).CopyTo(destination.Slice(firstChunk));

            readPosition = (readPosition + readable) % buffer.Length;
            count -= readable;

            return readable;
        }
    }

    /// <summary>
    /// Discards everything buffered.
    /// </summary>
    public void Clear()
    {
        lock (sync)
        {
            readPosition = 0;
            count = 0;
        }
    }
}
