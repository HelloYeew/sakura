// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.InteropServices;
using System.Threading;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;
using Vortice.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// Routes validation-layer messages to <see cref="Logger"/>.
/// </summary>
internal static unsafe class VulkanDebug
{
    private static readonly GlobalStatistic<long> stat_validation_messages = GlobalStatistics.Get<long>("Vulkan", "Validation Messages", StatisticKind.Cumulative);

    private static long messageCount;

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
            stat_validation_messages.Value = Interlocked.Increment(ref messageCount);

            string message = Marshal.PtrToStringUTF8((nint)data->pMessage) ?? string.Empty;
            string text = $"Vulkan {types.ToString().ToLowerInvariant()}: {message}";

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
