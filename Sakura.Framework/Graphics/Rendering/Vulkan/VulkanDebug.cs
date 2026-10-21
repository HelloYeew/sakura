// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.InteropServices;
using System.Threading;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// Routes validation-layer and loader messages to <see cref="Logger"/>.
/// </summary>
/// <remarks>
/// Messages about invalid usage (and synchronization hazards) are warnings or errors, and are counted
/// in <c>Vulkan › Validation Messages</c>, which should always read zero. Performance hints are only
/// advice about valid usage, so they are logged at debug level and counted separately; the effect
/// passes produce one by design, pairing <c>shader.vert</c>, which writes every varying, with fragment
/// shaders that read only two of them. General messages come from the loader, mostly about installed
/// drivers that can't find their hardware (Mesa installs a whole set on Linux). They are logged as
/// warnings, since they explain a missing device, but not counted: they say nothing about how the
/// renderer uses Vulkan.
/// </remarks>
internal static unsafe class VulkanDebug
{
    private static readonly GlobalStatistic<long> stat_validation_messages = GlobalStatistics.Get<long>("Vulkan", "Validation Messages", StatisticKind.Cumulative);
    private static readonly GlobalStatistic<long> stat_performance_hints = GlobalStatistics.Get<long>("Vulkan", "Performance Hints", StatisticKind.Cumulative);

    private static long messageCount;
    private static long hintCount;

    public static VkDebugUtilsMessengerCreateInfoEXT CreateMessengerInfo() => new VkDebugUtilsMessengerCreateInfoEXT
    {
        messageSeverity = VkDebugUtilsMessageSeverityFlagsEXT.Warning | VkDebugUtilsMessageSeverityFlagsEXT.Error,
        messageType = VkDebugUtilsMessageTypeFlagsEXT.General | VkDebugUtilsMessageTypeFlagsEXT.Validation | VkDebugUtilsMessageTypeFlagsEXT.Performance,
        pfnUserCallback = &onMessage,
    };

    /// <summary>
    /// Called by the layer on whichever thread made the offending call, possibly a driver thread.
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint onMessage(VkDebugUtilsMessageSeverityFlagsEXT severity, VkDebugUtilsMessageTypeFlagsEXT types, VkDebugUtilsMessengerCallbackDataEXT* data, void* userData)
    {
        // An exception can't cross back into the driver, so nothing may escape this method.
        try
        {
            string message = Marshal.PtrToStringUTF8((nint)data->pMessage) ?? string.Empty;
            string text = $"Vulkan {types.ToString().ToLowerInvariant()}: {message}";

            if (types == VkDebugUtilsMessageTypeFlagsEXT.Performance)
            {
                stat_performance_hints.Value = Interlocked.Increment(ref hintCount);
                Logger.Debug(text);
                return 0;
            }

            if ((types & VkDebugUtilsMessageTypeFlagsEXT.Validation) == 0)
            {
                Logger.Warning(text);
                return 0;
            }

            stat_validation_messages.Value = Interlocked.Increment(ref messageCount);

            if ((severity & VkDebugUtilsMessageSeverityFlagsEXT.Error) != 0)
                Logger.Error(text);
            else
                Logger.Warning(text);
        }
        catch
        {
        }

        // VK_FALSE: never abort the call that triggered the message.
        return 0;
    }
}
