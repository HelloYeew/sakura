// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using SharpGen.Runtime;
using Vortice.Direct3D11;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// Times each frame on the GPU with timestamp queries, read back a few frames later without flushing,
/// so measuring never makes the draw thread wait for the GPU.
/// </summary>
/// <remarks>
/// The time runs from when the GPU reaches the frame's first command to when it reaches its last. A
/// driver that submits part of a frame before the CPU has finished recording it leaves the GPU idle
/// in between, and that idle time is counted too, so this is an upper bound on the GPU's work.
/// </remarks>
internal sealed class D3D11GpuTimer : IDisposable
{
    /// <summary>
    /// The frame being recorded, plus two earlier frames whose results can still be on their way back:
    /// with two frames of latency, the GPU can still be on the frame before last when a new one starts.
    /// </summary>
    private const int set_count = 3;

    private readonly ID3D11DeviceContext context;
    private readonly QuerySet[] sets = new QuerySet[set_count];

    /// <summary>
    /// The set the next frame records into, which is also the oldest one issued.
    /// </summary>
    private int next;

    private QuerySet? open;

    public D3D11GpuTimer(ID3D11Device device, ID3D11DeviceContext context)
    {
        this.context = context;

        for (int i = 0; i < set_count; i++)
        {
            sets[i] = new QuerySet(
                device.CreateQuery(QueryType.TimestampDisjoint, QueryFlags.None),
                device.CreateQuery(QueryType.Timestamp, QueryFlags.None),
                device.CreateQuery(QueryType.Timestamp, QueryFlags.None));
        }
    }

    /// <summary>
    /// Starts timing a frame, after collecting every earlier frame the GPU has finished.
    /// </summary>
    /// <returns>
    /// The GPU time of the newest frame collected, in milliseconds, or <see cref="double.NaN"/> if none
    /// came back or the ones that did were disjoint (the GPU's clock changed rate part-way through).
    /// </returns>
    public double BeginFrame()
    {
        double milliseconds = collect();

        if (open != null)
            return milliseconds;

        open = sets[next];

        // Still pending only if its result hasn't come back after a full lap of the ring, which only a
        // stalled or removed device does. It's dropped rather than waited for.
        open.Pending = false;

        context.Begin(open.Disjoint);
        context.End(open.Start);

        return milliseconds;
    }

    /// <summary>
    /// Stops timing the frame <see cref="BeginFrame"/> started. Goes before the present, so the time
    /// covers the frame's own work and not the composition of it.
    /// </summary>
    public void EndFrame()
    {
        if (open == null)
            return;

        context.End(open.End);
        context.End(open.Disjoint);

        open.Pending = true;
        open = null;
        next = (next + 1) % set_count;
    }

    private double collect()
    {
        double milliseconds = double.NaN;

        // Oldest first. The GPU finishes frames in order, so once one isn't back, none after it are.
        for (int i = 0; i < set_count; i++)
        {
            var set = sets[(next + i) % set_count];

            if (!set.Pending)
                continue;

            if (!tryGetData(set.Disjoint, out QueryDataTimestampDisjoint disjoint))
                break;

            set.Pending = false;

            if (disjoint.Disjoint || disjoint.Frequency == 0)
                continue;

            if (!tryGetData(set.Start, out ulong start) || !tryGetData(set.End, out ulong end) || end < start)
                continue;

            milliseconds = (end - start) * 1000.0 / disjoint.Frequency;
        }

        return milliseconds;
    }

    private unsafe bool tryGetData<T>(ID3D11Query query, out T data) where T : unmanaged
    {
        T value = default;
        Result result = context.GetData(query, (nint)(&value), (uint)sizeof(T), AsyncGetDataFlags.DoNotFlush);
        data = value;

        // S_FALSE means not finished yet, and SharpGen counts it as a success, so only S_OK says the
        // data is there.
        return result == Result.Ok;
    }

    public void Dispose()
    {
        foreach (var set in sets)
        {
            set?.Disjoint.Dispose();
            set?.Start.Dispose();
            set?.End.Dispose();
        }
    }

    private sealed class QuerySet
    {
        public readonly ID3D11Query Disjoint;
        public readonly ID3D11Query Start;
        public readonly ID3D11Query End;

        /// <summary>
        /// Whether the queries were issued and their result hasn't been collected yet.
        /// </summary>
        public bool Pending;

        public QuerySet(ID3D11Query disjoint, ID3D11Query start, ID3D11Query end)
        {
            Disjoint = disjoint;
            Start = start;
            End = end;
        }
    }
}
