// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// Decides where each write into a ring buffer goes, when the buffer has to be discarded, and when it
/// should grow. Knows nothing about the buffer itself, so the wrap and growth rules can be tested
/// without a device.
/// </summary>
/// <remarks>
/// Writes go front to back with no-overwrite maps, which promise the driver that nothing the GPU may
/// still read is written over. When a write doesn't fit in what's left, the buffer is discarded
/// instead: the driver hands over fresh memory, keeps the old contents alive for draws still in
/// flight, and writing starts again from the front.
/// </remarks>
internal sealed class RingCursor
{
    /// <summary>
    /// Capacity in elements.
    /// </summary>
    public int Capacity { get; private set; }

    /// <summary>
    /// The capacity growth stops at. A single write larger than this still gets a buffer that fits it,
    /// through <see cref="Resize"/>.
    /// </summary>
    public int MaxCapacity { get; }

    private int position;

    /// <summary>
    /// Whether the next write has to discard even though it would fit: a new buffer's first map must be
    /// a discard.
    /// </summary>
    private bool discardNext = true;

    private int wrapsThisFrame;

    public RingCursor(int capacity, int maxCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCapacity, capacity);

        Capacity = capacity;
        MaxCapacity = maxCapacity;
    }

    /// <summary>
    /// Reserves <paramref name="count"/> elements, which must not exceed <see cref="Capacity"/>.
    /// </summary>
    /// <param name="count">How many elements the write needs.</param>
    /// <param name="discard">Whether the buffer has to be mapped with a discard for this write.</param>
    /// <returns>The element the write starts at.</returns>
    public int Allocate(int count, out bool discard)
    {
        if (count > Capacity)
            throw new ArgumentOutOfRangeException(nameof(count), count, $"A write can't be larger than the ring ({Capacity}); resize it first.");

        discard = discardNext || position + count > Capacity;

        if (discard)
        {
            if (!discardNext)
                wrapsThisFrame++;

            discardNext = false;
            position = 0;
        }

        int start = position;
        position += count;
        return start;
    }

    /// <summary>
    /// Ends a frame. A frame that had to wrap more than once is writing more than the ring holds, so the
    /// ring doubles, up to <see cref="MaxCapacity"/>. Wrapping once a frame is fine: that's one discard
    /// per frame instead of one per draw.
    /// </summary>
    /// <returns>Whether <see cref="Capacity"/> changed, so the buffer has to be recreated at it.</returns>
    public bool EndFrame()
    {
        bool grow = wrapsThisFrame > 1 && Capacity < MaxCapacity;
        wrapsThisFrame = 0;

        if (grow)
            Resize(Math.Min(Capacity * 2, MaxCapacity));

        return grow;
    }

    /// <summary>
    /// Starts over on a new buffer of <paramref name="capacity"/> elements.
    /// </summary>
    public void Resize(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Capacity = capacity;
        position = 0;
        discardNext = true;
    }
}
