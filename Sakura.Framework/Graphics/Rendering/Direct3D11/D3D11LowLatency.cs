// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
using SharpGen.Runtime;
using Vortice.Direct3D11;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// Carries <see cref="LowLatencyMode"/> on a Direct3D 11 device: NVIDIA Reflex where the device takes it, else AMD
/// Anti-Lag 2, else a wait for the GPU to finish the frame before, through a fence.
/// </summary>
/// <remarks>
/// <para>
/// Chosen the first time the mode is on, so with it off no driver's latency library is ever loaded. Once chosen, the
/// provider stays: turning the mode off tells it so, and stops the calls each frame.
/// </para>
/// <para>
/// Reflex's frames are marked as NVAPI asks: the simulation starts after the sleep, the render submission starts before
/// the frame's draw calls and ends before the present, and the present is wrapped. Sakura's draw takes an update the update
/// thread made, so the "simulation" here is the frame's start up to that take.
/// </para>
/// <para>
/// Draw thread only.
/// </para>
/// </remarks>
internal sealed class D3D11LowLatency : IDisposable
{
    private static readonly GlobalStatistic<double> stat_wait = GlobalStatistics.Get<double>("Renderer", "Low Latency Wait", StatisticKind.PerFrame, StatisticUnit.Milliseconds);

    // Long enough for any frame the GPU could still finish; a lost device mustn't hang the draw thread.
    private const uint gpu_wait_timeout_ms = 250;

    private const uint wait_timeout = 0x00000102;

    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;

    private LowLatencyMode applied = LowLatencyMode.Off;
    private bool chosen;

    private NvapiReflex? reflex;
    private AmdAntiLag2? antiLag;

    private ID3D11Fence? fence;
    private ID3D11DeviceContext4? context4;
    private nint fenceEvent;
    private ulong fenceValue;

    private ulong frameId;
    private bool marking;
    private bool warned;

    /// <summary>
    /// What carries the mode, once it's been on.
    /// </summary>
    public LowLatencyProvider Provider { get; private set; }

    public D3D11LowLatency(ID3D11Device device, ID3D11DeviceContext context)
    {
        this.device = device;
        this.context = context;
    }

    /// <summary>
    /// First in a frame: applies <paramref name="mode"/> if it changed, then, while it's on, waits until the frame can start
    /// just in time for the GPU.
    /// </summary>
    public void Sleep(LowLatencyMode mode)
    {
        marking = false;

        if (mode != applied)
            apply(mode);

        if (applied == LowLatencyMode.Off)
        {
            stat_wait.Value = 0;
            return;
        }

        frameId++;
        long start = Stopwatch.GetTimestamp();

        switch (Provider)
        {
            case LowLatencyProvider.NvidiaReflex:
                int status = reflex!.Sleep();
                marking = status == NvapiReflex.STATUS_OK;

                if (!marking)
                    warnOnce($"NvAPI_D3D_Sleep returned {NvapiReflex.DescribeStatus(status)}");

                break;

            case LowLatencyProvider.AmdAntiLag:
                if (!antiLag!.Update())
                    warnOnce("Anti-Lag 2's update was refused");

                break;

            case LowLatencyProvider.GpuWait:
                waitForGpu();
                break;
        }

        stat_wait.Value = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Mark(NvLatencyMarker.SimulationStart);
    }

    /// <summary>
    /// Tells Reflex the frame has reached <paramref name="marker"/>. Does nothing unless Reflex slept for this frame.
    /// </summary>
    public void Mark(NvLatencyMarker marker)
    {
        if (!marking)
            return;

        int status = reflex!.Mark(frameId, marker);

        if (status != NvapiReflex.STATUS_OK)
            warnOnce($"NvAPI_D3D_SetLatencyMarker returned {NvapiReflex.DescribeStatus(status)}");
    }

    /// <summary>
    /// After the present: where the wait for the GPU carries the mode, marks the end of the frame's work for the next frame
    /// to wait on.
    /// </summary>
    public void FramePresented()
    {
        if (applied == LowLatencyMode.Off || Provider != LowLatencyProvider.GpuWait || fence == null || context4 == null)
            return;

        context4.Signal(fence, ++fenceValue);

        // The signal waits in the context's commands until they're sent, and the next frame waits for it before it sends any.
        context.Flush();
    }

    private void apply(LowLatencyMode mode)
    {
        if (!chosen && mode != LowLatencyMode.Off)
        {
            choose(mode);
        }
        else
        {
            switch (Provider)
            {
                case LowLatencyProvider.NvidiaReflex:
                    int status = reflex!.SetMode(mode);

                    if (status != NvapiReflex.STATUS_OK)
                        warnOnce($"NvAPI_D3D_SetSleepMode returned {NvapiReflex.DescribeStatus(status)}");

                    break;

                case LowLatencyProvider.AmdAntiLag:
                    antiLag!.SetEnabled(mode != LowLatencyMode.Off);
                    break;
            }
        }

        applied = mode;

        // A fence signalled before the mode went off would be waited on long after.
        fenceValue = fence?.CompletedValue ?? 0;
    }

    private void choose(LowLatencyMode mode)
    {
        chosen = true;

        reflex = NvapiReflex.TryCreate(device.NativePointer, mode, out string reflexReason);

        if (reflex != null)
        {
            Provider = LowLatencyProvider.NvidiaReflex;
            Logger.Verbose($"Direct3D11: low latency through NVIDIA Reflex{(mode == LowLatencyMode.Boost ? ", with Boost" : string.Empty)}.");
            return;
        }

        antiLag = AmdAntiLag2.TryCreate(out string antiLagReason);

        if (antiLag != null)
        {
            int result = antiLag.SetEnabled(true);

            if (result == 0)
            {
                Provider = LowLatencyProvider.AmdAntiLag;
                Logger.Verbose("Direct3D11: low latency through AMD Anti-Lag 2.");
                return;
            }

            antiLagReason = $"turning it on returned 0x{result:X8}";
            antiLag.Dispose();
            antiLag = null;
        }

        if (createFence(out string fenceReason))
        {
            Provider = LowLatencyProvider.GpuWait;
            Logger.Verbose($"Direct3D11: low latency by waiting for the GPU to finish the frame before (no Reflex: {reflexReason}; no Anti-Lag 2: {antiLagReason}).");
            return;
        }

        Provider = LowLatencyProvider.None;
        Logger.Verbose($"Direct3D11: nothing here can carry the low latency mode (no Reflex: {reflexReason}; no Anti-Lag 2: {antiLagReason}; no fence: {fenceReason}).");
    }

    private bool createFence(out string reason)
    {
        using var device5 = device.QueryInterfaceOrNull<ID3D11Device5>();
        context4 = context.QueryInterfaceOrNull<ID3D11DeviceContext4>();

        if (device5 == null || context4 == null)
        {
            reason = "Direct3D 11.4 isn't there (Windows 10 1703 or later)";
            return false;
        }

        try
        {
            fence = device5.CreateFence<ID3D11Fence>(0, FenceFlags.None);
        }
        catch (SharpGenException e)
        {
            reason = $"CreateFence failed ({e.ResultCode})";
            return false;
        }

        fenceEvent = CreateEventW(0, false, false, null);

        if (fenceEvent == 0)
        {
            reason = "CreateEvent failed";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Waits until the GPU has finished the frame before, which <see cref="FramePresented"/> marked.
    /// </summary>
    private void waitForGpu()
    {
        if (fence == null || fenceValue == 0 || fence.CompletedValue >= fenceValue)
            return;

        fence.SetEventOnCompletion(fenceValue, fenceEvent);

        if (WaitForSingleObjectEx(fenceEvent, gpu_wait_timeout_ms, false) == wait_timeout)
            warnOnce($"the GPU didn't finish a frame within {gpu_wait_timeout_ms} ms");
    }

    private void warnOnce(string message)
    {
        if (warned)
            return;

        warned = true;
        Logger.Warning($"Direct3D11 low latency: {message}. Said once.");
    }

    /// <summary>
    /// Releases the fence and Anti-Lag 2's interface.
    /// </summary>
    public void Dispose()
    {
        antiLag?.Dispose();
        fence?.Dispose();
        context4?.Dispose();

        if (fenceEvent != 0)
            CloseHandle(fenceEvent);

        fenceEvent = 0;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateEventW(nint attributes, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32", SetLastError = true)]
    private static extern uint WaitForSingleObjectEx(nint handle, uint milliseconds, bool alertable);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}
