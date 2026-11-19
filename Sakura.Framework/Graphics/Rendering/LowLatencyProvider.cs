// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Platform;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// What carries <see cref="LowLatencyMode"/> on the device the renderer draws with.
/// </summary>
public enum LowLatencyProvider
{
    /// <summary>
    /// Nothing: the mode is off, hasn't been used yet, or the renderer has no way to carry it.
    /// </summary>
    None,

    /// <summary>
    /// Sakura's own: each frame waits for the GPU to finish the frame before it. Any graphics card.
    /// </summary>
    GpuWait,

    /// <summary>
    /// NVIDIA Reflex, through NVAPI on Direct3D 11 or <c>VK_NV_low_latency2</c> on Vulkan.
    /// </summary>
    NvidiaReflex,

    /// <summary>
    /// AMD Anti-Lag 2, through AMD's driver extension on Direct3D 11 or <c>VK_AMD_anti_lag</c> on Vulkan. On Linux a
    /// Vulkan layer such as low_latency_layer can provide the extension on other graphics cards too.
    /// </summary>
    AmdAntiLag
}
