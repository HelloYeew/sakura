// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.CompilerServices;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;
using Vortice.Direct3D11;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// A dynamic vertex or index buffer that draws append to, so a frame of many draws discards the buffer
/// about once instead of once per draw. See <see cref="RingCursor"/> for the rules.
/// </summary>
internal sealed class D3D11RingBuffer<T> : IDisposable where T : unmanaged
{
    /// <summary>
    /// How many times this frame a ring had to be discarded, all rings together. About one or two per
    /// ring per frame is healthy; one per draw means the ring is too small.
    /// </summary>
    private static readonly GlobalStatistic<int> stat_discards = GlobalStatistics.Get<int>("Direct3D11", "Buffer Discards", StatisticKind.PerFrame);

    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly BindFlags bindFlags;
    private readonly string name;
    private readonly RingCursor cursor;

    /// <summary>
    /// The buffer to bind. Replaced when the ring grows, so a caller that keeps it bound has to compare
    /// before each draw.
    /// </summary>
    public ID3D11Buffer NativeBuffer { get; private set; }

    // name says what the ring holds, for the log line when it grows.
    public D3D11RingBuffer(ID3D11Device device, ID3D11DeviceContext context, BindFlags bindFlags, int capacity, int maxCapacity, string name)
    {
        this.device = device;
        this.context = context;
        this.bindFlags = bindFlags;
        this.name = name;

        cursor = new RingCursor(capacity, maxCapacity);
        NativeBuffer = create(capacity);
    }

    /// <summary>
    /// Copies <paramref name="data"/> in after everything written since the last discard.
    /// </summary>
    /// <returns>The element <paramref name="data"/> starts at, for the draw's base vertex or start index.</returns>
    public unsafe int Write(ReadOnlySpan<T> data)
    {
        if (data.Length > cursor.Capacity)
        {
            cursor.Resize(data.Length);
            recreate();
        }

        int start = cursor.Allocate(data.Length, out bool discard);

        if (discard)
            stat_discards.Accumulator++;

        MappedSubresource mapped = context.Map(NativeBuffer, 0, discard ? MapMode.WriteDiscard : MapMode.WriteNoOverwrite, Vortice.Direct3D11.MapFlags.None);

        fixed (T* source = data)
        {
            T* destination = (T*)mapped.DataPointer + start;
            Buffer.MemoryCopy(source, destination, (long)(cursor.Capacity - start) * sizeof(T), (long)data.Length * sizeof(T));
        }

        context.Unmap(NativeBuffer, 0);
        return start;
    }

    /// <summary>
    /// Ends a frame, growing the ring if the frame needed more than it holds.
    /// </summary>
    public void EndFrame()
    {
        if (!cursor.EndFrame())
            return;

        recreate();
        Logger.Verbose($"Direct3D11 {name} ring grew to {cursor.Capacity} elements ({(long)cursor.Capacity * Unsafe.SizeOf<T>() / 1024} KB).");
    }

    private void recreate()
    {
        // Draws already issued keep the old buffer alive until the GPU is done with them.
        NativeBuffer.Dispose();
        NativeBuffer = create(cursor.Capacity);
    }

    private ID3D11Buffer create(int capacity) => device.CreateBuffer(new BufferDescription(
        (uint)(capacity * Unsafe.SizeOf<T>()), bindFlags, ResourceUsage.Dynamic, CpuAccessFlags.Write));

    public void Dispose() => NativeBuffer.Dispose();
}
