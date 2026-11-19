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
/// A provider that stops working gives way to the next (<see cref="LowLatencyFallback"/>), with a verbose log line and
/// nothing for the player to see: Reflex refusing a call, or its sleep never signalling, falls back to the GPU wait, and a
/// GPU wait that keeps running out its timeout stops waiting. Anti-Lag 2's call reports nothing, so it can't be seen to fail.
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

    /// <summary>
    /// Long enough for any frame the GPU could still finish; a lost device mustn't hang the draw thread.
    /// </summary>
    internal const ulong DEFAULT_WAIT_TIMEOUT_NANOSECONDS = 250_000_000;

    private readonly VulkanDevice device;
    private readonly ulong waitTimeoutNanoseconds;

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

    private int timeoutsInARow;

    /// <summary>
    /// What carries the mode, once it's been on. It changes if that stops working.
    /// </summary>
    public LowLatencyProvider Provider { get; private set; }

    /// <summary>
    /// The present ID the current frame's work is attributed to, while Reflex slept for it; null otherwise.
    /// </summary>
    public ulong? MarkedPresentId => markedSwapchain.IsNotNull ? markedPresentId : null;

    /// <param name="device">The device, which says which driver extension it was made with.</param>
    /// <param name="waitTimeoutNanoseconds">How long a wait may take before it counts as one that never ends.</param>
    public VulkanLowLatency(VulkanDevice device, ulong waitTimeoutNanoseconds = DEFAULT_WAIT_TIMEOUT_NANOSECONDS)
    {
        this.device = device;
        this.waitTimeoutNanoseconds = waitTimeoutNanoseconds;
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

        if (changed)
            timeoutsInARow = 0;

        if (changed && mode != LowLatencyMode.Off && Provider == LowLatencyProvider.None)
            choose(mode);

        long start = Stopwatch.GetTimestamp();

        switch (Provider)
        {
            case LowLatencyProvider.NvidiaReflex:
                sleepReflex(mode, changed, swapchain);
                break;

            case LowLatencyProvider.AmdAntiLag:
                updateAntiLag(mode, changed);
                break;

            case LowLatencyProvider.GpuWait:
                if (mode != LowLatencyMode.Off)
                    waitForGpu(previousFrame);

                break;
        }

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

    /// <summary>
    /// Lets go of the provider that stopped working and takes the next (<see cref="LowLatencyFallback.After"/>), for the rest
    /// of the run.
    /// </summary>
    private void fallBack(string why, VkSwapchainKHR swapchain)
    {
        var failed = Provider;

        // Told to stop pacing first, as far as it still listens, so the driver doesn't keep pacing on top of the next one.
        if (failed == LowLatencyProvider.NvidiaReflex && swapchain.IsNotNull)
        {
            var off = new VkLatencySleepModeInfoNV();
            device.Api.vkSetLatencySleepModeNV(swapchain, &off);
        }
        else if (failed == LowLatencyProvider.AmdAntiLag)
        {
            var off = new VkAntiLagDataAMD { mode = VkAntiLagModeAMD.OffAMD };
            device.Api.vkAntiLagUpdateAMD(&off);
        }

        Provider = LowLatencyFallback.After(failed);
        markedSwapchain = VkSwapchainKHR.Null;
        antiLagUpdated = false;
        timeoutsInARow = 0;

        string now = Provider == LowLatencyProvider.GpuWait ? "frames wait for the GPU to finish the frame before instead" : "frames no longer wait";
        Logger.Verbose($"Vulkan: low latency through {LowLatencyFallback.Describe(failed)} stopped working ({why}), so {now}.");
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
            {
                fallBack($"vkSetLatencySleepModeNV returned {result}", handle);
                return;
            }
        }

        if (mode == LowLatencyMode.Off)
            return;

        if (sleepSemaphore.IsNull && !createSleepSemaphore(out var created))
        {
            fallBack($"vkCreateSemaphore for its sleep returned {created}", handle);
            return;
        }

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
            fallBack($"vkLatencySleepNV returned {slept}", handle);
            return;
        }

        var waitInfo = new VkSemaphoreWaitInfo
        {
            semaphoreCount = 1,
            pSemaphores = &semaphore,
            pValues = &value,
        };

        var waited = device.Api.vkWaitSemaphores(&waitInfo, waitTimeoutNanoseconds);

        if (waited == VkResult.Timeout)
        {
            if (++timeoutsInARow >= LowLatencyFallback.TIMEOUTS_BEFORE_GIVING_UP)
            {
                fallBack($"its sleep didn't end within {waitTimeoutNanoseconds / 1_000_000} ms, {timeoutsInARow} times in a row", handle);
                return;
            }
        }
        else if (waited != VkResult.Success)
        {
            fallBack($"waiting for its sleep returned {waited}", handle);
            return;
        }
        else
        {
            timeoutsInARow = 0;
        }

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

        var result = device.Api.vkWaitForFences(1, &previousFrame, true, waitTimeoutNanoseconds);

        if (result == VkResult.Success)
        {
            timeoutsInARow = 0;
            return;
        }

        if (result != VkResult.Timeout)
            fallBack($"waiting for the frame before returned {result}", VkSwapchainKHR.Null);
        else if (++timeoutsInARow >= LowLatencyFallback.TIMEOUTS_BEFORE_GIVING_UP)
            fallBack($"the GPU didn't finish a frame within {waitTimeoutNanoseconds / 1_000_000} ms, {timeoutsInARow} times in a row", VkSwapchainKHR.Null);
    }

    private bool createSleepSemaphore(out VkResult result)
    {
        var type = new VkSemaphoreTypeCreateInfo
        {
            semaphoreType = VkSemaphoreType.Timeline,
            initialValue = 0,
        };

        var info = new VkSemaphoreCreateInfo { pNext = &type };
        VkSemaphore semaphore;
        result = device.Api.vkCreateSemaphore(&info, null, &semaphore);

        if (result != VkResult.Success)
            return false;

        sleepSemaphore = semaphore;
        device.SetName(VkObjectType.Semaphore, sleepSemaphore.Handle, "Reflex sleep");
        return true;
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
