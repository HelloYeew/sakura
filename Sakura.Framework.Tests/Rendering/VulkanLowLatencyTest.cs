// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vulkan;
using Sakura.Framework.IO;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
using Sakura.Framework.Timing;
using Vortice.Vulkan;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// The low latency mode on the Vulkan renderer with no window, where no driver extension is enabled and the frame waits for
/// the GPU to finish the frame before. Runs wherever Vulkan renders offscreen: this Mac through KosmicKrisp, CI through
/// Mesa's lavapipe.
/// </summary>
public class VulkanLowLatencyTest
{
    private static readonly GlobalStatistic<long> stat_validation_messages = GlobalStatistics.Get<long>("Vulkan", "Validation Messages", StatisticKind.Cumulative);

    [TestCase(LowLatencyMode.On, LowLatencyProvider.GpuWait)]
    [TestCase(LowLatencyMode.Boost, LowLatencyProvider.GpuWait)]
    [TestCase(LowLatencyMode.Off, LowLatencyProvider.None)]
    public void FramesWaitForTheGpuWhereNoDriverCarriesIt(LowLatencyMode mode, LowLatencyProvider expected)
    {
        if (!VulkanRenderer.IsSupportedOffscreen())
            Assert.Ignore("No Vulkan driver here. On macOS, set SDL_VULKAN_LIBRARY and VK_DRIVER_FILES as the README's \"Test Vulkan on macOS\" section describes.");

        long validationBefore = stat_validation_messages.Value;

        using (var renderer = createRenderer(mode))
        {
            drawFrames(renderer, 5);

            Assert.That(renderer.LowLatencyProvider, Is.EqualTo(expected));
        }

        Assert.That(stat_validation_messages.Value - validationBefore, Is.Zero, "validation messages (the log has them)");
    }

    [Test]
    public void ItCanBeTurnedOnAndOffWhileDrawing()
    {
        if (!VulkanRenderer.IsSupportedOffscreen())
            Assert.Ignore("No Vulkan driver here.");

        long validationBefore = stat_validation_messages.Value;

        using (var renderer = createRenderer(LowLatencyMode.Off))
        {
            drawFrames(renderer, 2);
            renderer.SetLowLatencyMode(LowLatencyMode.On);
            drawFrames(renderer, 3);

            Assert.That(renderer.LowLatencyProvider, Is.EqualTo(LowLatencyProvider.GpuWait), "turned on");

            renderer.SetLowLatencyMode(LowLatencyMode.Off);
            drawFrames(renderer, 2);
        }

        Assert.That(stat_validation_messages.Value - validationBefore, Is.Zero, "validation messages (the log has them)");
    }

    [Test]
    public unsafe void AWaitThatNeverEndsIsGivenUp()
    {
        if (!VulkanRenderer.IsSupportedOffscreen())
            Assert.Ignore("No Vulkan driver here.");

        using var device = new VulkanDevice(null, out _);

        // Never submitted, so it never signals: a GPU that stopped, as far as the wait can tell.
        var fenceInfo = new VkFenceCreateInfo();
        VkFence fence;
        Assert.That(device.Api.vkCreateFence(&fenceInfo, null, &fence), Is.EqualTo(VkResult.Success));

        try
        {
            using var lowLatency = new VulkanLowLatency(device, 1_000_000);
            var seen = new List<LowLatencyProvider>();

            for (int i = 0; i < LowLatencyFallback.TIMEOUTS_BEFORE_GIVING_UP + 1; i++)
            {
                lowLatency.Sleep(LowLatencyMode.On, null, fence);
                seen.Add(lowLatency.Provider);
            }

            var expected = Enumerable.Repeat(LowLatencyProvider.GpuWait, LowLatencyFallback.TIMEOUTS_BEFORE_GIVING_UP - 1)
                                     .Append(LowLatencyProvider.None)
                                     .Append(LowLatencyProvider.None);

            Assert.That(seen, Is.EqualTo(expected), "the provider after each frame");
        }
        finally
        {
            device.Api.vkDestroyFence(fence, null);
        }
    }

    private static VulkanRenderer createRenderer(LowLatencyMode mode)
    {
        var renderer = new VulkanRenderer
        {
            ShaderStorage = new EmbeddedResourceStorage(typeof(App).Assembly, "Sakura.Framework.Resources").GetStorageForDirectory("Shaders"),
        };

        // Before the device is made, as the host does.
        renderer.SetLowLatencyMode(mode);
        renderer.InitializeOffscreen(32, 32);
        return renderer;
    }

    private static void drawFrames(VulkanRenderer renderer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            renderer.SleepForLowLatency();
            renderer.StartFrame();
            renderer.Draw(new FramedClock(new ManualClock()));
        }
    }
}
