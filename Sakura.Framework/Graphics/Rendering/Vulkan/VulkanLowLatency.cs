// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// Carries <see cref="LowLatencyMode"/> on a Vulkan device: NVIDIA Reflex (<c>VK_NV_low_latency2</c>) or AMD Anti-Lag 2
/// (<c>VK_AMD_anti_lag</c>) where the device was made with one (<see cref="VulkanDevice.SupportsLowLatency2"/>,
/// <see cref="VulkanDevice.SupportsAntiLag"/>), else a wait for the GPU to finish the frame before.
/// </summary>
/// <remarks>
/// <para>
/// The driver extensions are enabled only when low latency is asked for as the device is made, so a mode turned on later
/// in the run waits for the GPU instead, until the next launch.
/// </para>
/// <para>
/// Reflex's frames are marked by the ID their present will carry: the simulation starts after the sleep, the render
/// submission runs from the frame's draw calls to its queue submit, and the present is wrapped. Sakura's draw takes an
/// update the update thread made, so the "simulation" here is the frame's start up to that take.
/// </para>
/// <para>
/// Draw thread only.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanLowLatency : IDisposable
{
    private static readonly GlobalStatistic<double> stat_wait = GlobalStatistics.Get<double>("Renderer", "Low Latency Wait", StatisticKind.PerFrame, StatisticUnit.Milliseconds);

    // Long enough for any frame the GPU could still finish; a lost device mustn't hang the draw thread.
    private const ulong wait_timeout_nanoseconds = 250_000_000;

    private readonly VulkanDevice device;

    private LowLatencyMode applied = LowLatencyMode.Off;

    // The swapchain Reflex's mode was last set on: a rebuilt one starts without it.
    private VkSwapchainKHR modeSwapchain;

    // What the current frame's markers go to, while Reflex slept for it.
    private VkSwapchainKHR markedSwapchain;
    private ulong markedPresentId;

    private VkSemaphore sleepSemaphore;
    private ulong sleepValue;

    private ulong antiLagFrame;
    private bool antiLagUpdated;

    private bool warned;

    /// <summary>
    /// What carries the mode, once it's been on.
    /// </summary>
    public LowLatencyProvider Provider { get; private set; }

    /// <summary>
    /// The present ID the current frame's work is attributed to, while Reflex slept for it; null otherwise.
    /// </summary>
    public ulong? MarkedPresentId => markedSwapchain.IsNotNull ? markedPresentId : null;

    public VulkanLowLatency(VulkanDevice device)
    {
        this.device = device;
    }

    /// <summary>
    /// First in a frame: applies <paramref name="mode"/>, then, while it's on, waits until the frame can start just in time
    /// for the GPU.
    /// </summary>
    /// <param name="mode">The mode asked for.</param>
    /// <param name="swapchain">The window's swapchain, or null when drawing offscreen.</param>
    /// <param name="previousFrame">The fence the frame before signals when the GPU has finished it.</param>
    public void Sleep(LowLatencyMode mode, VulkanSwapchain? swapchain, VkFence previousFrame)
    {
        markedSwapchain = VkSwapchainKHR.Null;
        antiLagUpdated = false;

        bool changed = mode != applied;
        applied = mode;

        if (changed && mode != LowLatencyMode.Off && Provider == LowLatencyProvider.None)
            choose(mode);

        long start = Stopwatch.GetTimestamp();

        if (device.SupportsLowLatency2)
            sleepReflex(mode, changed, swapchain);
        else if (device.SupportsAntiLag)
            updateAntiLag(mode, changed);
        else if (mode != LowLatencyMode.Off)
            waitForGpu(previousFrame);

        stat_wait.Value = mode != LowLatencyMode.Off ? Stopwatch.GetElapsedTime(start).TotalMilliseconds : 0;
        Mark(VkLatencyMarkerNV.SimulationStart);
    }

    /// <summary>
    /// Tells Reflex the frame has reached <paramref name="marker"/>. Does nothing unless Reflex slept for this frame.
    /// </summary>
    public void Mark(VkLatencyMarkerNV marker)
    {
        if (markedSwapchain.IsNull)
            return;

        var info = new VkSetLatencyMarkerInfoNV
        {
            presentID = markedPresentId,
            marker = marker,
        };

        device.Api.vkSetLatencyMarkerNV(markedSwapchain, &info);
    }

    /// <summary>
    /// Just before the frame is presented: Reflex's present marker, or Anti-Lag 2's present stage for the frame it delayed.
    /// </summary>
    public void BeforePresent()
    {
        Mark(VkLatencyMarkerNV.PresentStart);

        if (!antiLagUpdated)
            return;

        var presentation = new VkAntiLagPresentationInfoAMD
        {
            stage = VkAntiLagStageAMD.PresentAMD,
            frameIndex = antiLagFrame,
        };

        var data = new VkAntiLagDataAMD
        {
            mode = VkAntiLagModeAMD.OnAMD,
            pPresentationInfo = &presentation,
        };

        device.Api.vkAntiLagUpdateAMD(&data);
    }

    /// <summary>
    /// Just after the frame is presented.
    /// </summary>
    public void AfterPresent() => Mark(VkLatencyMarkerNV.PresentEnd);

    private void choose(LowLatencyMode mode)
    {
        if (device.SupportsLowLatency2)
        {
            Provider = LowLatencyProvider.NvidiaReflex;
            Logger.Verbose($"Vulkan: low latency through NVIDIA Reflex (VK_NV_low_latency2){(mode == LowLatencyMode.Boost ? ", with Boost" : string.Empty)}.");
        }
        else if (device.SupportsAntiLag)
        {
            Provider = LowLatencyProvider.AmdAntiLag;
            Logger.Verbose("Vulkan: low latency through AMD Anti-Lag 2 (VK_AMD_anti_lag).");
        }
        else
        {
            Provider = LowLatencyProvider.GpuWait;
            bool offered = device.AvailableExtensions.Contains("VK_NV_low_latency2") || device.AvailableExtensions.Contains("VK_AMD_anti_lag");
            Logger.Verbose(offered
                ? "Vulkan: low latency by waiting for the GPU to finish the frame before. The driver's own (Reflex or Anti-Lag 2) is enabled only when the mode is on at launch."
                : "Vulkan: low latency by waiting for the GPU to finish the frame before (the driver has neither Reflex nor Anti-Lag 2).");
        }
    }

    private void sleepReflex(LowLatencyMode mode, bool changed, VulkanSwapchain? swapchain)
    {
        // Before the first frame has built a swapchain there's nothing to pace yet.
        var handle = swapchain?.Handle ?? VkSwapchainKHR.Null;

        if (handle.IsNull)
            return;

        if (changed || handle != modeSwapchain)
        {
            var modeInfo = new VkLatencySleepModeInfoNV
            {
                lowLatencyMode = mode != LowLatencyMode.Off,
                lowLatencyBoost = mode == LowLatencyMode.Boost,
                minimumIntervalUs = 0,
            };

            var result = device.Api.vkSetLatencySleepModeNV(handle, &modeInfo);
            modeSwapchain = handle;

            if (result != VkResult.Success)
                warnOnce($"vkSetLatencySleepModeNV returned {result}");
        }

        if (mode == LowLatencyMode.Off)
            return;

        if (sleepSemaphore.IsNull && !createSleepSemaphore())
            return;

        ulong value = ++sleepValue;
        var semaphore = sleepSemaphore;

        var sleepInfo = new VkLatencySleepInfoNV
        {
            signalSemaphore = semaphore,
            value = value,
        };

        var slept = device.Api.vkLatencySleepNV(handle, &sleepInfo);

        if (slept != VkResult.Success)
        {
            warnOnce($"vkLatencySleepNV returned {slept}");
            return;
        }

        var waitInfo = new VkSemaphoreWaitInfo
        {
            semaphoreCount = 1,
            pSemaphores = &semaphore,
            pValues = &value,
        };

        var waited = device.Api.vkWaitSemaphores(&waitInfo, wait_timeout_nanoseconds);

        if (waited != VkResult.Success)
            warnOnce($"waiting for Reflex's sleep returned {waited}");

        markedSwapchain = handle;
        markedPresentId = swapchain!.LastPresentId + 1;
    }

    private void updateAntiLag(LowLatencyMode mode, bool changed)
    {
        if (mode == LowLatencyMode.Off)
        {
            if (!changed)
                return;

            var off = new VkAntiLagDataAMD { mode = VkAntiLagModeAMD.OffAMD };
            device.Api.vkAntiLagUpdateAMD(&off);
            return;
        }

        var presentation = new VkAntiLagPresentationInfoAMD
        {
            stage = VkAntiLagStageAMD.InputAMD,
            frameIndex = ++antiLagFrame,
        };

        var data = new VkAntiLagDataAMD
        {
            mode = VkAntiLagModeAMD.OnAMD,
            pPresentationInfo = &presentation,
        };

        device.Api.vkAntiLagUpdateAMD(&data);
        antiLagUpdated = true;
    }

    private void waitForGpu(VkFence previousFrame)
    {
        if (previousFrame.IsNull)
            return;

        var result = device.Api.vkWaitForFences(1, &previousFrame, true, wait_timeout_nanoseconds);

        if (result == VkResult.Timeout)
            warnOnce($"the GPU didn't finish a frame within {wait_timeout_nanoseconds / 1_000_000} ms");
    }

    private bool createSleepSemaphore()
    {
        var type = new VkSemaphoreTypeCreateInfo
        {
            semaphoreType = VkSemaphoreType.Timeline,
            initialValue = 0,
        };

        var info = new VkSemaphoreCreateInfo { pNext = &type };
        VkSemaphore semaphore;
        var result = device.Api.vkCreateSemaphore(&info, null, &semaphore);

        if (result != VkResult.Success)
        {
            warnOnce($"vkCreateSemaphore for Reflex's sleep returned {result}");
            return false;
        }

        sleepSemaphore = semaphore;
        device.SetName(VkObjectType.Semaphore, sleepSemaphore.Handle, "Reflex sleep");
        return true;
    }

    private void warnOnce(string message)
    {
        if (warned)
            return;

        warned = true;
        Logger.Warning($"Vulkan low latency: {message}. Said once.");
    }

    /// <summary>
    /// Destroys the sleep's semaphore. With the device idle.
    /// </summary>
    public void Dispose()
    {
        if (sleepSemaphore.IsNotNull)
            device.Api.vkDestroySemaphore(sleepSemaphore, null);

        sleepSemaphore = VkSemaphore.Null;
    }
}
