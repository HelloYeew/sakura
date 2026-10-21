// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering.Vulkan;
using Vortice.Vulkan;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// The hand-declared FFmpeg Vulkan structs match FFmpeg's own layout. The expected offsets come from
/// compiling <c>offsetof</c> against FFmpeg 8.1.1's <c>libavutil/hwcontext_vulkan.h</c> (libavutil 60) on
/// a 64-bit target. A mismatch here is memory corruption at run time, with nothing to say where it came
/// from, so when the native libraries move to a new libavutil major version these numbers are taken
/// again.
/// </summary>
[TestFixture]
public class FFmpegVulkanLayoutTest
{
    [Test]
    public void DeviceQueueFamilyMatchesFFmpeg()
    {
        assertLayout<AVVulkanDeviceQueueFamily>(16, ("idx", 0), ("num", 4), ("flags", 8), ("video_caps", 12));
    }

    [Test]
    public void DeviceContextMatchesFFmpeg()
    {
        Assert.That(Unsafe.SizeOf<VkPhysicalDeviceFeatures2>(), Is.EqualTo(240), "VkPhysicalDeviceFeatures2");

        assertLayout<AVVulkanDeviceContext>(1400,
            ("alloc", 0), ("get_proc_addr", 8), ("inst", 16), ("phys_dev", 24), ("act_dev", 32),
            ("device_features", 40), ("enabled_inst_extensions", 280), ("nb_enabled_inst_extensions", 288),
            ("enabled_dev_extensions", 296), ("nb_enabled_dev_extensions", 304),
            ("queue_family_index", 308), ("nb_graphics_queues", 312), ("queue_family_tx_index", 316), ("nb_tx_queues", 320),
            ("queue_family_comp_index", 324), ("nb_comp_queues", 328), ("queue_family_encode_index", 332), ("nb_encode_queues", 336),
            ("queue_family_decode_index", 340), ("nb_decode_queues", 344),
            ("lock_queue", 352), ("unlock_queue", 360), ("qf", 368), ("nb_qf", 1392));
    }

    [Test]
    public void FramesContextMatchesFFmpeg()
    {
        assertLayout<AVVulkanFramesContext>(144,
            ("tiling", 0), ("usage", 4), ("create_pnext", 8), ("alloc_pnext", 16), ("flags", 80), ("img_flags", 84),
            ("format", 88), ("nb_layers", 120), ("lock_frame", 128), ("unlock_frame", 136));
    }

    /// <summary>
    /// Only read and updated in place, never allocated here, so its size doesn't matter; every offset does.
    /// </summary>
    [Test]
    public void FrameMatchesFFmpeg()
    {
        assertLayout<AVVkFrame>(504,
            ("img", 0), ("tiling", 64), ("mem", 72), ("size", 136), ("flags", 200), ("access", 204), ("layout", 236),
            ("sem", 272), ("sem_value", 336), ("internal", 400), ("offset", 408), ("queue_family", 472));
    }

    private static void assertLayout<T>(int size, params (string Field, int Offset)[] fields) where T : unmanaged
    {
        Assert.Multiple(() =>
        {
            Assert.That(Unsafe.SizeOf<T>(), Is.EqualTo(size), $"sizeof({typeof(T).Name})");

            foreach (var (field, offset) in fields)
                Assert.That((int)Marshal.OffsetOf<T>(field), Is.EqualTo(offset), $"offsetof({typeof(T).Name}, {field})");
        });
    }
}
