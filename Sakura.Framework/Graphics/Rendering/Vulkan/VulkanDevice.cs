// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Sakura.Framework.Development;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Sakura.Framework.Graphics.Rendering.Vulkan;

/// <summary>
/// The Vulkan instance, the physical device chosen to render with, and the logical device and queues
/// created on it.
/// </summary>
/// <remarks>
/// This lives as long as the renderer. The window's surface is only used to choose a device that can
/// present to it, so a lost surface (Android going to the background, <c>VK_ERROR_SURFACE_LOST_KHR</c>)
/// costs a new surface and swapchain, never the device and the resources created on it.
/// </remarks>
internal sealed unsafe class VulkanDevice : IDisposable
{
    private const string surface_extension = "VK_KHR_surface";
    private const string swapchain_extension = "VK_KHR_swapchain";
    private const string portability_enumeration_extension = "VK_KHR_portability_enumeration";
    private const string portability_subset_extension = "VK_KHR_portability_subset";
    private const string properties2_extension = "VK_KHR_get_physical_device_properties2";
    private const string driver_properties_extension = "VK_KHR_driver_properties";
    private const string debug_utils_extension = "VK_EXT_debug_utils";
    private const string validation_features_extension = "VK_EXT_validation_features";
    private const string validation_layer = "VK_LAYER_KHRONOS_validation";

    /// <summary>
    /// Selects the physical device whose name contains this text, overriding the automatic choice (for
    /// hybrid laptops, or to test one driver of several).
    /// </summary>
    private const string device_override_variable = "SAKURA_VULKAN_DEVICE";

    /// <summary>
    /// <c>1</c> turns the validation layer on in a release build, <c>0</c> turns it off in a debug one.
    /// </summary>
    private const string validation_variable = "SAKURA_VULKAN_VALIDATION";

    /// <summary>
    /// The surface extension of every window system the framework runs on. A loader offering none of
    /// them can't present anywhere.
    /// </summary>
    private static readonly string[] platform_surface_extensions =
    [
        "VK_KHR_win32_surface",
        "VK_KHR_xlib_surface",
        "VK_KHR_xcb_surface",
        "VK_KHR_wayland_surface",
        "VK_EXT_metal_surface",
        "VK_KHR_android_surface",
    ];

    /// <summary>
    /// Device extensions that nothing requires but later work can use: present pacing, switching present
    /// modes without a new swapchain, and memory budgets. Their availability is logged at startup.
    /// </summary>
    private static readonly string[] optional_device_extensions =
    [
        "VK_KHR_present_id",
        "VK_KHR_present_wait",
        "VK_KHR_swapchain_maintenance1",
        "VK_EXT_swapchain_maintenance1",
        "VK_EXT_memory_budget",
    ];

    private static readonly VkVersion version_1_0 = new VkVersion(1, 0, 0);
    private static readonly VkVersion version_1_1 = new VkVersion(1, 1, 0);
    private static readonly VkVersion version_1_2 = new VkVersion(1, 2, 0);

    private static readonly Lazy<bool> loader_available = new Lazy<bool>(() =>
    {
        // SDL loads the loader for the window itself, from the SDL_VULKAN_LIBRARY hint when that is
        // set, and the instance has to come from the same library.
        string? library = Environment.GetEnvironmentVariable("SDL_VULKAN_LIBRARY");
        return vkInitialize(string.IsNullOrEmpty(library) ? null : library) == VkResult.Success;
    });

    private static readonly Lazy<bool> is_supported = new Lazy<bool>(() => probe(true));
    private static readonly Lazy<bool> is_supported_offscreen = new Lazy<bool>(() => probe(false));

    public VkInstance Instance { get; }
    public VkInstanceApi InstanceApi { get; }
    public VkPhysicalDevice PhysicalDevice { get; }
    public VkDevice Handle { get; }
    public VkDeviceApi Api { get; }

    public uint GraphicsQueueFamily { get; }
    public VkQueue GraphicsQueue { get; }

    /// <summary>
    /// The queue family presentation happens on. Almost always <see cref="GraphicsQueueFamily"/>; when it
    /// isn't, swapchain images are shared between the two families.
    /// </summary>
    public uint PresentQueueFamily { get; }

    public VkQueue PresentQueue { get; }

    public VkPhysicalDeviceProperties Properties { get; }

    public string DeviceName { get; }

    /// <summary>
    /// The driver's own name and version string (e.g. "KosmicKrisp", "NVIDIA 566.36"), or the device
    /// name where the driver can't report one.
    /// </summary>
    public string DriverDescription { get; }

    /// <summary>
    /// Every extension the physical device offers, enabled or not.
    /// </summary>
    public IReadOnlySet<string> AvailableExtensions { get; }

    /// <summary>
    /// Whether <c>VK_EXT_debug_utils</c> is enabled, so objects can be given names that debuggers show.
    /// </summary>
    public bool HasDebugUtils { get; }

    public bool ValidationEnabled { get; }

    private readonly VkDebugUtilsMessengerEXT debugMessenger;

    /// <summary>
    /// Whether this machine can run the Vulkan renderer at all. Creates and destroys a throwaway
    /// instance and creates no window, so a missing or broken driver is ruled out before a window is
    /// made for it. The answer is computed once per process.
    /// </summary>
    public static bool IsSupported() => is_supported.Value;

    /// <summary>
    /// Whether this machine can render offscreen through Vulkan, with no window: a usable loader and a
    /// device with a graphics queue. Computed once per process.
    /// </summary>
    public static bool IsSupportedOffscreen() => is_supported_offscreen.Value;

    /// <summary>
    /// Creates the instance and device for <paramref name="windowSurface"/>, which is also where the
    /// surface comes from that the chosen device has to present to.
    /// </summary>
    /// <param name="windowSurface">The window to render into, or null to render offscreen only, with no
    /// surface, swapchain or presentation.</param>
    /// <param name="surface">The surface created to choose the device (null without a window). The caller
    /// owns it from here.</param>
    public VulkanDevice(IVulkanGraphicsSurface? windowSurface, out VkSurfaceKHR surface)
    {
        if (!loader_available.Value)
            throw new InvalidOperationException("The Vulkan loader could not be loaded.");

        var instanceExtensions = enumerateInstanceExtensions(null);

        ValidationEnabled = shouldEnableValidation();

        var extensions = new List<string>(windowSurface?.RequiredInstanceExtensions ?? []);
        var layers = new List<string>();
        bool syncValidation = false;

        if (ValidationEnabled)
        {
            layers.Add(validation_layer);

            var layerExtensions = enumerateInstanceExtensions(validation_layer);
            instanceExtensions.UnionWith(layerExtensions);

            if (layerExtensions.Contains(validation_features_extension))
            {
                extensions.Add(validation_features_extension);
                syncValidation = true;
            }
        }

        HasDebugUtils = instanceExtensions.Contains(debug_utils_extension);

        if (HasDebugUtils)
            extensions.Add(debug_utils_extension);

        var flags = VkInstanceCreateFlags.None;

        // Portability drivers (MoltenVK) are only enumerated when the application says it can cope with
        // them. KosmicKrisp is conformant and doesn't need this.
        if (instanceExtensions.Contains(portability_enumeration_extension))
        {
            extensions.Add(portability_enumeration_extension);
            flags |= VkInstanceCreateFlags.EnumeratePortabilityKHR;
        }

        VkVersion apiVersion = requestedApiVersion();

        // Needed to ask a 1.0 device for its driver name, and required by VK_KHR_portability_subset.
        if (apiVersion.Minor == 0 && instanceExtensions.Contains(properties2_extension))
            extensions.Add(properties2_extension);

        Instance = createInstance(extensions, layers, flags, apiVersion, ValidationEnabled && HasDebugUtils, syncValidation);
        InstanceApi = GetApi(Instance);

        if (ValidationEnabled && HasDebugUtils)
        {
            var messengerInfo = VulkanDebug.CreateMessengerInfo();
            VkDebugUtilsMessengerEXT messenger;
            Check(InstanceApi.vkCreateDebugUtilsMessengerEXT(&messengerInfo, null, &messenger), "vkCreateDebugUtilsMessengerEXT");
            debugMessenger = messenger;
        }

        surface = VkSurfaceKHR.Null;

        try
        {
            if (windowSurface != null)
                surface = windowSurface.CreateSurface(Instance.Handle);

            var candidate = choosePhysicalDevice(InstanceApi, surface, windowSurface != null, apiVersion);

            PhysicalDevice = candidate.Device;
            Properties = candidate.Properties;
            DeviceName = candidate.Name;
            DriverDescription = candidate.DriverDescription;
            AvailableExtensions = candidate.Extensions;
            GraphicsQueueFamily = candidate.GraphicsFamily;
            PresentQueueFamily = candidate.PresentFamily;

            Handle = createDevice(candidate, windowSurface != null);
            Api = GetApi(Instance, Handle);

            VkQueue queue;
            Api.vkGetDeviceQueue(GraphicsQueueFamily, 0, &queue);
            GraphicsQueue = queue;

            Api.vkGetDeviceQueue(PresentQueueFamily, 0, &queue);
            PresentQueue = queue;
        }
        catch
        {
            if (surface.IsNotNull)
                windowSurface!.DestroySurface(Instance.Handle, surface);

            destroyInstance();
            throw;
        }

        logDeviceInfo(apiVersion, syncValidation);
    }

    /// <summary>
    /// Names <paramref name="handle"/> for debuggers and validation messages. Does nothing without
    /// <c>VK_EXT_debug_utils</c>.
    /// </summary>
    public void SetName(VkObjectType type, ulong handle, string name)
    {
        if (!HasDebugUtils || handle == 0)
            return;

        int length = Encoding.UTF8.GetByteCount(name);
        byte* bytes = stackalloc byte[length + 1];
        Encoding.UTF8.GetBytes(name, new Span<byte>(bytes, length));
        bytes[length] = 0;

        var info = new VkDebugUtilsObjectNameInfoEXT
        {
            objectType = type,
            objectHandle = handle,
            pObjectName = bytes,
        };

        InstanceApi.vkSetDebugUtilsObjectNameEXT(Handle, &info);
    }

    public void WaitIdle() => Check(Api.vkDeviceWaitIdle(), "vkDeviceWaitIdle");

    public void Dispose()
    {
        if (Handle.IsNotNull)
        {
            Api.vkDeviceWaitIdle();
            Api.vkDestroyDevice(null);
        }

        destroyInstance();
    }

    private void destroyInstance()
    {
        if (debugMessenger.IsNotNull)
            InstanceApi.vkDestroyDebugUtilsMessengerEXT(debugMessenger, null);

        if (Instance.IsNotNull)
            InstanceApi.vkDestroyInstance(null);
    }

    /// <summary>
    /// Throws for a failed <see cref="VkResult"/>, naming the call that produced it.
    /// </summary>
    public static void Check(VkResult result, string call)
    {
        if (result < VkResult.Success)
            throw new InvalidOperationException($"{call} failed: {result}");
    }

    #region Instance

    private static bool shouldEnableValidation()
    {
        string? setting = Environment.GetEnvironmentVariable(validation_variable);
        bool wanted = setting switch
        {
            "1" => true,
            "0" => false,
            _ => DebugUtils.IsDebugBuild,
        };

        if (!wanted)
            return false;

        if (enumerateInstanceLayers().Contains(validation_layer))
            return true;

        Logger.Verbose($"Vulkan: {validation_layer} is not installed, so validation is off. The LunarG Vulkan SDK provides it.");
        return false;
    }

    /// <summary>
    /// The renderer is written against Vulkan 1.0 plus extensions, so it asks for 1.1 only to reach the
    /// properties2 queries without an extension. Asking for more than the loader has would fail on a
    /// 1.0 loader.
    /// </summary>
    private static VkVersion requestedApiVersion()
        => vkEnumerateInstanceVersion().Value >= version_1_1.Value ? version_1_1 : version_1_0;

    private static VkInstance createInstance(List<string> extensions, List<string> layers, VkInstanceCreateFlags flags, VkVersion apiVersion, bool debugMessenger, bool syncValidation)
    {
        using var extensionNames = new NativeStringArray(extensions);
        using var layerNames = new NativeStringArray(layers);

        byte* applicationName = stackalloc byte[] { (byte)'S', (byte)'a', (byte)'k', (byte)'u', (byte)'r', (byte)'a', 0 };

        var applicationInfo = new VkApplicationInfo
        {
            pApplicationName = applicationName,
            applicationVersion = new VkVersion(1, 0, 0),
            pEngineName = applicationName,
            engineVersion = new VkVersion(1, 0, 0),
            apiVersion = apiVersion,
        };

        var createInfo = new VkInstanceCreateInfo
        {
            flags = flags,
            pApplicationInfo = &applicationInfo,
            enabledExtensionCount = extensionNames.Length,
            ppEnabledExtensionNames = extensionNames.Pointer,
            enabledLayerCount = layerNames.Length,
            ppEnabledLayerNames = layerNames.Pointer,
        };

        // Chained in so instance creation and destruction are validated too, not just what happens
        // between them.
        var messengerInfo = VulkanDebug.CreateMessengerInfo();

        if (debugMessenger)
            createInfo.pNext = &messengerInfo;

        VkValidationFeatureEnableEXT* enabledFeatures = stackalloc VkValidationFeatureEnableEXT[] { VkValidationFeatureEnableEXT.SynchronizationValidation };

        var validationFeatures = new VkValidationFeaturesEXT
        {
            enabledValidationFeatureCount = 1,
            pEnabledValidationFeatures = enabledFeatures,
        };

        if (syncValidation)
        {
            validationFeatures.pNext = createInfo.pNext;
            createInfo.pNext = &validationFeatures;
        }

        VkInstance instance;
        Check(vkCreateInstance(&createInfo, null, &instance), "vkCreateInstance");
        return instance;
    }

    private static HashSet<string> enumerateInstanceExtensions(string? layer)
    {
        using var layerName = new NativeStringArray(layer != null ? [layer] : []);
        byte* name = layer != null ? layerName.Pointer[0] : null;

        uint count;
        Check(vkEnumerateInstanceExtensionProperties(name, &count, null), "vkEnumerateInstanceExtensionProperties");

        var properties = new VkExtensionProperties[count];

        fixed (VkExtensionProperties* p = properties)
        {
            Check(vkEnumerateInstanceExtensionProperties(name, &count, p), "vkEnumerateInstanceExtensionProperties");

            var result = new HashSet<string>();

            for (int i = 0; i < count; i++)
                result.Add(readString(p[i].extensionName));

            return result;
        }
    }

    private static HashSet<string> enumerateInstanceLayers()
    {
        uint count;
        Check(vkEnumerateInstanceLayerProperties(&count, null), "vkEnumerateInstanceLayerProperties");

        var properties = new VkLayerProperties[count];

        fixed (VkLayerProperties* p = properties)
        {
            Check(vkEnumerateInstanceLayerProperties(&count, p), "vkEnumerateInstanceLayerProperties");

            var result = new HashSet<string>();

            for (int i = 0; i < count; i++)
                result.Add(readString(p[i].layerName));

            return result;
        }
    }

    #endregion

    #region Physical device

    private sealed class Candidate
    {
        public VkPhysicalDevice Device;
        public VkPhysicalDeviceProperties Properties;
        public string Name = string.Empty;
        public string DriverDescription = string.Empty;
        public VkDriverId? DriverId;
        public HashSet<string> Extensions = [];
        public uint GraphicsFamily;
        public uint PresentFamily;
        public ulong DeviceLocalMemory;
    }

    private static Candidate choosePhysicalDevice(VkInstanceApi api, VkSurfaceKHR surface, bool presents, VkVersion instanceApiVersion)
    {
        var candidates = new List<Candidate>();
        var rejections = new List<string>();

        foreach (var device in enumeratePhysicalDevices(api))
        {
            var candidate = describe(api, device, instanceApiVersion);

            if (!isUsable(api, candidate, surface, presents, out string? reason))
            {
                rejections.Add($"{candidate.Name}: {reason}");
                continue;
            }

            candidates.Add(candidate);
        }

        if (candidates.Count == 0)
        {
            string detail = rejections.Count > 0 ? string.Join("; ", rejections) : "no physical devices";
            throw new InvalidOperationException($"No Vulkan device can be used here ({detail}).");
        }

        string? requested = Environment.GetEnvironmentVariable(device_override_variable);

        if (!string.IsNullOrEmpty(requested))
        {
            var match = candidates.FirstOrDefault(c => c.Name.Contains(requested, StringComparison.OrdinalIgnoreCase)
                                                       || c.DriverDescription.Contains(requested, StringComparison.OrdinalIgnoreCase));

            if (match != null)
                return match;

            Logger.Warning($"Vulkan: no usable device matches {device_override_variable}='{requested}'; choosing automatically from {string.Join(", ", candidates.Select(c => $"'{c.Name}' ({c.DriverDescription})"))}.");
        }

        return candidates.OrderByDescending(typeRank)
                         .ThenByDescending(driverRank)
                         .ThenByDescending(c => c.Properties.apiVersion.Value)
                         .ThenByDescending(c => c.DeviceLocalMemory)
                         .First();

        static int typeRank(Candidate c) => c.Properties.deviceType switch
        {
            VkPhysicalDeviceType.DiscreteGpu => 4,
            VkPhysicalDeviceType.IntegratedGpu => 3,
            VkPhysicalDeviceType.VirtualGpu => 2,
            VkPhysicalDeviceType.Cpu => 1,
            _ => 0,
        };

        // MoltenVK and a native driver can both enumerate the same Apple GPU. MoltenVK is a portability
        // layer that Khronos has deprecated, so it only wins when it is the only choice.
        static int driverRank(Candidate c) => c.DriverId == VkDriverId.Moltenvk ? 0 : 1;
    }

    private static VkPhysicalDevice[] enumeratePhysicalDevices(VkInstanceApi api)
    {
        uint count;
        Check(api.vkEnumeratePhysicalDevices(&count, null), "vkEnumeratePhysicalDevices");

        var devices = new VkPhysicalDevice[count];

        fixed (VkPhysicalDevice* p = devices)
            Check(api.vkEnumeratePhysicalDevices(&count, p), "vkEnumeratePhysicalDevices");

        return devices;
    }

    private static Candidate describe(VkInstanceApi api, VkPhysicalDevice device, VkVersion instanceApiVersion)
    {
        VkPhysicalDeviceProperties properties;
        api.vkGetPhysicalDeviceProperties(device, &properties);

        var candidate = new Candidate
        {
            Device = device,
            Properties = properties,
            Name = readString(properties.deviceName),
            Extensions = enumerateDeviceExtensions(api, device),
        };

        candidate.DriverDescription = candidate.Name;

        // Driver properties come through properties2: core when both the instance and the device are
        // 1.1, otherwise the KHR entry point (which the instance only has with the extension enabled).
        bool hasProperties2 = (instanceApiVersion.Minor >= 1 && properties.apiVersion.Minor >= 1) || api.vkGetPhysicalDeviceProperties2KHR_ptr != default;

        if (hasProperties2 && (candidate.Extensions.Contains(driver_properties_extension) || properties.apiVersion.Value >= version_1_2.Value))
        {
            var driverProperties = new VkPhysicalDeviceDriverProperties();
            var properties2 = new VkPhysicalDeviceProperties2 { pNext = &driverProperties };

            if (instanceApiVersion.Minor >= 1 && properties.apiVersion.Minor >= 1)
                api.vkGetPhysicalDeviceProperties2(device, &properties2);
            else
                api.vkGetPhysicalDeviceProperties2KHR(device, &properties2);

            candidate.DriverId = driverProperties.driverID;

            string driverName = readString(driverProperties.driverName);
            string driverInfo = readString(driverProperties.driverInfo);
            candidate.DriverDescription = string.IsNullOrEmpty(driverInfo) ? driverName : $"{driverName} {driverInfo}";
        }

        VkPhysicalDeviceMemoryProperties memory;
        api.vkGetPhysicalDeviceMemoryProperties(device, &memory);

        for (int i = 0; i < memory.memoryHeapCount; i++)
        {
            var heap = memory.memoryHeaps[i];

            if ((heap.flags & VkMemoryHeapFlags.DeviceLocal) != 0)
                candidate.DeviceLocalMemory += heap.size;
        }

        return candidate;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> can be used, and which queue families it would use. A device
    /// that <paramref name="presents"/> to a window also needs the swapchain extension and, when
    /// <paramref name="surface"/> is given, a queue that can present to it.
    /// </summary>
    private static bool isUsable(VkInstanceApi api, Candidate candidate, VkSurfaceKHR surface, bool presents, out string? reason)
    {
        if (!meetsDevicePolicy(candidate.Properties, out reason))
            return false;

        if (presents && !candidate.Extensions.Contains(swapchain_extension))
        {
            reason = $"no {swapchain_extension}";
            return false;
        }

        uint familyCount;
        api.vkGetPhysicalDeviceQueueFamilyProperties(candidate.Device, &familyCount, null);
        var families = stackalloc VkQueueFamilyProperties[(int)familyCount];
        api.vkGetPhysicalDeviceQueueFamilyProperties(candidate.Device, &familyCount, families);

        uint? graphics = null;
        uint? present = null;

        for (uint i = 0; i < familyCount; i++)
        {
            bool canDraw = (families[i].queueFlags & VkQueueFlags.Graphics) != 0;
            bool canPresent = surface.IsNull || supportsPresent(api, candidate.Device, i, surface);

            // One family that does both is what nearly every device has, and it keeps everything on one
            // queue.
            if (canDraw && canPresent)
            {
                graphics = present = i;
                break;
            }

            if (canDraw)
                graphics ??= i;
            if (canPresent)
                present ??= i;
        }

        if (graphics == null || present == null)
        {
            reason = graphics == null ? "no graphics queue" : "can't present to this window";
            return false;
        }

        candidate.GraphicsFamily = graphics.Value;
        candidate.PresentFamily = present.Value;
        reason = null;
        return true;
    }

    private static bool supportsPresent(VkInstanceApi api, VkPhysicalDevice device, uint family, VkSurfaceKHR surface)
    {
        VkBool32 supported;
        return api.vkGetPhysicalDeviceSurfaceSupportKHR(device, family, surface, &supported) == VkResult.Success && supported;
    }

    /// <summary>
    /// Devices ruled out no matter what they report as supported. Android requires 1.1, which every
    /// device launched with Android 10 or later has and which keeps the oldest drivers out.
    /// </summary>
    private static bool meetsDevicePolicy(VkPhysicalDeviceProperties properties, out string? reason)
    {
        if (OperatingSystem.IsAndroid() && properties.apiVersion.Value < version_1_1.Value)
        {
            reason = $"Vulkan {format(properties.apiVersion)} is older than the 1.1 Android requires";
            return false;
        }

        reason = null;
        return true;
    }

    private static HashSet<string> enumerateDeviceExtensions(VkInstanceApi api, VkPhysicalDevice device)
    {
        uint count;
        Check(api.vkEnumerateDeviceExtensionProperties(device, null, &count, null), "vkEnumerateDeviceExtensionProperties");

        var properties = new VkExtensionProperties[count];

        fixed (VkExtensionProperties* p = properties)
        {
            Check(api.vkEnumerateDeviceExtensionProperties(device, null, &count, p), "vkEnumerateDeviceExtensionProperties");

            var result = new HashSet<string>();

            for (int i = 0; i < count; i++)
                result.Add(readString(p[i].extensionName));

            return result;
        }
    }

    #endregion

    #region Logical device

    private VkDevice createDevice(Candidate candidate, bool presents)
    {
        var extensions = new List<string>();

        if (presents)
            extensions.Add(swapchain_extension);

        // The spec requires enabling this whenever a device offers it: it is how a portability driver
        // says which parts of Vulkan it doesn't implement.
        if (candidate.Extensions.Contains(portability_subset_extension))
            extensions.Add(portability_subset_extension);

        using var extensionNames = new NativeStringArray(extensions);

        float priority = 1f;
        var queues = stackalloc VkDeviceQueueCreateInfo[2];

        queues[0] = new VkDeviceQueueCreateInfo
        {
            queueFamilyIndex = candidate.GraphicsFamily,
            queueCount = 1,
            pQueuePriorities = &priority,
        };

        queues[1] = new VkDeviceQueueCreateInfo
        {
            queueFamilyIndex = candidate.PresentFamily,
            queueCount = 1,
            pQueuePriorities = &priority,
        };

        var createInfo = new VkDeviceCreateInfo
        {
            queueCreateInfoCount = candidate.GraphicsFamily == candidate.PresentFamily ? 1u : 2u,
            pQueueCreateInfos = queues,
            enabledExtensionCount = extensionNames.Length,
            ppEnabledExtensionNames = extensionNames.Pointer,
        };

        VkDevice device;
        Check(InstanceApi.vkCreateDevice(candidate.Device, &createInfo, null, &device), "vkCreateDevice");
        return device;
    }

    private void logDeviceInfo(VkVersion instanceApiVersion, bool syncValidation)
    {
        var properties = Properties;

        VkPhysicalDeviceMemoryProperties memory;
        InstanceApi.vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, &memory);

        var heaps = new List<string>();

        for (int i = 0; i < memory.memoryHeapCount; i++)
        {
            var heap = memory.memoryHeaps[i];
            bool local = (heap.flags & VkMemoryHeapFlags.DeviceLocal) != 0;

            heaps.Add($"{heap.size / (1024 * 1024)} MB{(local ? " device-local" : string.Empty)}");
        }

        string optional = string.Join(", ", optional_device_extensions.Where(AvailableExtensions.Contains));

        Logger.Verbose("🌋 Vulkan renderer initialized");
        Logger.Verbose($"Vulkan device: {DeviceName} ({properties.deviceType}), driver: {DriverDescription}");
        Logger.Verbose($"Vulkan API: device {format(properties.apiVersion)}, instance {format(instanceApiVersion)}; hardware IDs: vendor 0x{properties.vendorID:X4}, device 0x{properties.deviceID:X4}");
        Logger.Verbose($"Vulkan memory heaps: {string.Join(", ", heaps)}");
        Logger.Verbose($"Vulkan queues: graphics family {GraphicsQueueFamily}, present family {PresentQueueFamily}");
        Logger.Verbose($"Vulkan optional extensions available: {(optional.Length > 0 ? optional : "none")}");
        Logger.Verbose($"Vulkan validation: {(ValidationEnabled ? syncValidation ? "on, with synchronization validation" : "on" : "off")}");
    }

    #endregion

    /// <summary>
    /// Answers <see cref="IsSupported"/> and <see cref="IsSupportedOffscreen"/>: a usable loader and at
    /// least one device with a graphics queue, plus, for a window, a window-system surface extension and
    /// swapchain support. Presentation to the real window can't be checked without one, so that is left
    /// to device creation.
    /// </summary>
    /// <param name="window">Whether rendering to a window has to work too: a window-system surface
    /// extension and swapchain support.</param>
    private static bool probe(bool window)
    {
        // SDL3-CS binds VkSurfaceKHR as a pointer, but it is a 64-bit handle everywhere.
        if (!Environment.Is64BitProcess)
        {
            Logger.Verbose("Vulkan: unavailable in a 32-bit process.");
            return false;
        }

        try
        {
            if (!loader_available.Value)
            {
                Logger.Verbose("Vulkan: no Vulkan loader found.");
                return false;
            }

            var instanceExtensions = enumerateInstanceExtensions(null);

            if (window && (!instanceExtensions.Contains(surface_extension) || !platform_surface_extensions.Any(instanceExtensions.Contains)))
            {
                Logger.Verbose("Vulkan: the loader offers no window-system surface extension (no driver installed?).");
                return false;
            }

            var extensions = new List<string>();
            var flags = VkInstanceCreateFlags.None;

            if (instanceExtensions.Contains(portability_enumeration_extension))
            {
                extensions.Add(portability_enumeration_extension);
                flags |= VkInstanceCreateFlags.EnumeratePortabilityKHR;
            }

            VkVersion apiVersion = requestedApiVersion();

            if (apiVersion.Minor == 0 && instanceExtensions.Contains(properties2_extension))
                extensions.Add(properties2_extension);

            var instance = createInstance(extensions, [], flags, apiVersion, false, false);
            var api = GetApi(instance);

            try
            {
                foreach (var device in enumeratePhysicalDevices(api))
                {
                    var candidate = describe(api, device, apiVersion);

                    if (isUsable(api, candidate, VkSurfaceKHR.Null, window, out string? reason))
                        return true;

                    Logger.Verbose($"Vulkan: {candidate.Name} can't be used ({reason}).");
                }

                Logger.Verbose("Vulkan: no usable physical device.");
                return false;
            }
            finally
            {
                api.vkDestroyInstance(null);
            }
        }
        catch (Exception e)
        {
            Logger.Verbose($"Vulkan: support check failed ({e.Message}).");
            return false;
        }
    }

    private static string format(VkVersion version) => $"{version.Major}.{version.Minor}.{version.Patch}";

    private static string readString(byte* utf8) => Marshal.PtrToStringUTF8((nint)utf8) ?? string.Empty;

    /// <summary>
    /// A native array of NUL-terminated UTF-8 strings, for the <c>ppEnabled*Names</c> fields.
    /// </summary>
    private readonly struct NativeStringArray : IDisposable
    {
        public readonly byte** Pointer;
        public readonly uint Length;

        public NativeStringArray(IReadOnlyList<string> strings)
        {
            Length = (uint)strings.Count;
            Pointer = Length == 0 ? null : (byte**)NativeMemory.AllocZeroed((nuint)strings.Count, (nuint)sizeof(byte*));

            for (int i = 0; i < strings.Count; i++)
            {
                int length = Encoding.UTF8.GetByteCount(strings[i]);
                byte* bytes = (byte*)NativeMemory.Alloc((nuint)length + 1);
                Encoding.UTF8.GetBytes(strings[i], new Span<byte>(bytes, length));
                bytes[length] = 0;
                Pointer[i] = bytes;
            }
        }

        public void Dispose()
        {
            if (Pointer == null)
                return;

            for (int i = 0; i < Length; i++)
                NativeMemory.Free(Pointer[i]);

            NativeMemory.Free(Pointer);
        }
    }
}
