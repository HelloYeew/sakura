// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
    private const string video_queue_extension = "VK_KHR_video_queue";
    private const string video_decode_queue_extension = "VK_KHR_video_decode_queue";
    private const string video_maintenance1_extension = "VK_KHR_video_maintenance1";
    private const string present_id_extension = "VK_KHR_present_id";
    private const string present_wait_extension = "VK_KHR_present_wait";
    private const string surface_capabilities2_extension = "VK_KHR_get_surface_capabilities2";

    /// <summary>
    /// The KHR name first: the EXT one is the same extension before it was promoted, with identical
    /// structures.
    /// </summary>
    private static readonly string[] surface_maintenance1_extensions = ["VK_KHR_surface_maintenance1", "VK_EXT_surface_maintenance1"];

    private static readonly string[] swapchain_maintenance1_extensions = ["VK_KHR_swapchain_maintenance1", "VK_EXT_swapchain_maintenance1"];

    /// <summary>
    /// The decode extension for each codec operation a video queue can report.
    /// </summary>
    private static readonly (string Extension, VkVideoCodecOperationFlagsKHR Operation)[] video_decode_codecs =
    [
        ("VK_KHR_video_decode_h264", VkVideoCodecOperationFlagsKHR.DecodeH264),
        ("VK_KHR_video_decode_h265", VkVideoCodecOperationFlagsKHR.DecodeH265),
        ("VK_KHR_video_decode_vp9", VkVideoCodecOperationFlagsKHR.DecodeVP9),
        ("VK_KHR_video_decode_av1", VkVideoCodecOperationFlagsKHR.DecodeAV1),
    ];

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
    /// Device extensions logged at startup when available. The present and swapchain ones are enabled
    /// whenever the device has them (present pacing, switching present modes without a new swapchain);
    /// memory budgets aren't used yet.
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
    private static readonly VkVersion version_1_3 = new VkVersion(1, 3, 0);

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
    public VkVersion InstanceApiVersion { get; }
    public IReadOnlyList<string> EnabledInstanceExtensions { get; }
    public IReadOnlyList<string> EnabledDeviceExtensions { get; private set; } = [];
    public VkPhysicalDevice PhysicalDevice { get; }
    public VkDevice Handle { get; }
    public VkDeviceApi Api { get; }

    public uint GraphicsQueueFamily { get; }

    /// <summary>
    /// What the graphics queue family reports it can do. Transfers are always possible on it whether or
    /// not it says so.
    /// </summary>
    public VkQueueFlags GraphicsQueueFlags { get; }

    /// <summary>
    /// How many bits of a timestamp written on the graphics queue are meaningful; 0 when it can't write
    /// timestamps at all.
    /// </summary>
    public uint GraphicsTimestampBits { get; }
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

    /// <summary>
    /// Whether presents can carry an ID and the CPU can wait for one to reach the screen
    /// (<c>VK_KHR_present_id</c> and <c>VK_KHR_present_wait</c>).
    /// </summary>
    public bool SupportsPresentWait { get; private set; }

    /// <summary>
    /// Whether a swapchain can be created to switch between compatible present modes from one present to
    /// the next (<c>VK_KHR_swapchain_maintenance1</c>), so turning vsync on or off needs no new swapchain.
    /// </summary>
    public bool SupportsPresentModeSwitching { get; private set; }

    /// <summary>
    /// Whether the instance can query a surface per present mode (<c>VK_KHR_surface_maintenance1</c>),
    /// which present-mode switching needs.
    /// </summary>
    private readonly bool hasSurfaceMaintenance;

    /// <summary>
    /// Whether frames a hardware decoder produces on this device can be sampled where they are: the
    /// instance and device are Vulkan 1.3, with timeline semaphores and synchronization2 enabled, which
    /// is what FFmpeg needs to share the device.
    /// </summary>
    public bool SharesVideoFrames { get; private set; }

    /// <summary>
    /// The features the device was created with, in a chain that lives as long as the device, for handing
    /// to FFmpeg along with the device. Null unless <see cref="SharesVideoFrames"/>.
    /// </summary>
    public VkPhysicalDeviceFeatures2* EnabledFeatures => enabledFeatures;

    private VkPhysicalDeviceFeatures2* enabledFeatures;

    /// <summary>
    /// The queue family video is decoded on, or null when the device has none, or when
    /// <see cref="SharesVideoFrames"/> is false and its frames couldn't be sampled anyway.
    /// </summary>
    public uint? DecodeQueueFamily { get; private set; }

    /// <summary>
    /// The codecs <see cref="DecodeQueueFamily"/> decodes and whose extensions were enabled.
    /// </summary>
    public VkVideoCodecOperationFlagsKHR DecodeOperations { get; private set; }

    /// <summary>
    /// One lock per queue the device was created with. Vulkan requires a queue's submissions, presents
    /// and waits never to overlap, and a hardware decoder sharing the device submits from its own
    /// thread, so everything that uses a queue holds its lock.
    /// </summary>
    private readonly Dictionary<(uint Family, uint Index), Lock> queueLocks = new Dictionary<(uint, uint), Lock>();

    /// <summary>
    /// What <see cref="GetQueueLock"/> hands out for a queue the device doesn't have, so a caller never
    /// gets nothing to lock.
    /// </summary>
    private readonly Lock unknownQueueLock = new Lock();

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

        // Only of use to a window: they describe what a surface can do per present mode.
        string? surfaceMaintenance = surface_maintenance1_extensions.FirstOrDefault(instanceExtensions.Contains);

        if (windowSurface != null && surfaceMaintenance != null && instanceExtensions.Contains(surface_capabilities2_extension))
        {
            extensions.Add(surface_capabilities2_extension);
            extensions.Add(surfaceMaintenance);
            hasSurfaceMaintenance = true;
        }

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
        InstanceApiVersion = apiVersion;
        EnabledInstanceExtensions = extensions;

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
            GraphicsQueueFlags = candidate.GraphicsFlags;
            GraphicsTimestampBits = candidate.GraphicsTimestampBits;
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

            freeEnabledFeatures();
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

    /// <summary>
    /// The lock every use of queue <paramref name="index"/> of <paramref name="family"/> holds: submits,
    /// presents and waits, the renderer's and a sharing decoder's alike.
    /// </summary>
    public Lock GetQueueLock(uint family, uint index) => queueLocks.GetValueOrDefault((family, index), unknownQueueLock);

    public Lock GraphicsQueueLock => GetQueueLock(GraphicsQueueFamily, 0);

    public Lock PresentQueueLock => GetQueueLock(PresentQueueFamily, 0);

    /// <summary>
    /// Waits for every queue to go idle, holding all their locks: the wait is a use of every queue.
    /// </summary>
    public void WaitIdle() => Check(waitIdle(), "vkDeviceWaitIdle");

    private VkResult waitIdle()
    {
        var locks = queueLocks.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToArray();

        foreach (var queueLock in locks)
            queueLock.Enter();

        try
        {
            return Api.vkDeviceWaitIdle();
        }
        finally
        {
            foreach (var queueLock in locks)
                queueLock.Exit();
        }
    }

    public void Dispose()
    {
        if (Handle.IsNotNull)
        {
            waitIdle();
            Api.vkDestroyDevice(null);
        }

        freeEnabledFeatures();
        destroyInstance();
    }

    private void freeEnabledFeatures()
    {
        NativeMemory.Free(enabledFeatures);
        enabledFeatures = null;
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
    /// The renderer is written against Vulkan 1.0 plus extensions. It asks for 1.3 where the loader has
    /// it, because that is what FFmpeg needs to decode into the renderer's own device, and otherwise for
    /// 1.1 to reach the properties2 queries without an extension. Asking for more than the loader has
    /// would fail on a 1.0 loader; on later ones a device older than the version asked for still works,
    /// it just can't be used past its own version.
    /// </summary>
    private static VkVersion requestedApiVersion()
    {
        uint loader = vkEnumerateInstanceVersion().Value;

        if (loader >= version_1_3.Value)
            return version_1_3;

        return loader >= version_1_1.Value ? version_1_1 : version_1_0;
    }

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
        public VkQueueFlags GraphicsFlags;
        public uint GraphicsTimestampBits;
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
        candidate.GraphicsFlags = families[graphics.Value].queueFlags;
        candidate.GraphicsTimestampBits = families[graphics.Value].timestampValidBits;
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

        var features = chooseVideoFeatures(candidate);
        uint? decodeFamily = features != null ? chooseDecodeQueue(candidate, extensions) : null;

        // Presentation features go in front of the video ones, which are kept as their own chain for
        // FFmpeg. These only have to live until the device is created.
        var presentId = new VkPhysicalDevicePresentIdFeaturesKHR();
        var presentWait = new VkPhysicalDevicePresentWaitFeaturesKHR();
        var maintenance = new VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR();
        void* chain = presents ? choosePresentFeatures(candidate, extensions, &presentId, &presentWait, &maintenance, features) : features;

        var families = new List<uint> { candidate.GraphicsFamily };

        if (!families.Contains(candidate.PresentFamily))
            families.Add(candidate.PresentFamily);

        if (decodeFamily is uint decode && !families.Contains(decode))
            families.Add(decode);

        using var extensionNames = new NativeStringArray(extensions);

        float priority = 1f;
        var queues = stackalloc VkDeviceQueueCreateInfo[families.Count];

        for (int i = 0; i < families.Count; i++)
        {
            queues[i] = new VkDeviceQueueCreateInfo
            {
                queueFamilyIndex = families[i],
                queueCount = 1,
                pQueuePriorities = &priority,
            };
        }

        var createInfo = new VkDeviceCreateInfo
        {
            pNext = chain,
            queueCreateInfoCount = (uint)families.Count,
            pQueueCreateInfos = queues,
            enabledExtensionCount = extensionNames.Length,
            ppEnabledExtensionNames = extensionNames.Pointer,
        };

        VkDevice device;

        try
        {
            Check(InstanceApi.vkCreateDevice(candidate.Device, &createInfo, null, &device), "vkCreateDevice");
        }
        catch
        {
            NativeMemory.Free(features);
            throw;
        }

        foreach (uint family in families)
            queueLocks[(family, 0)] = new Lock();

        EnabledDeviceExtensions = extensions;
        enabledFeatures = features;
        SharesVideoFrames = features != null;
        DecodeQueueFamily = decodeFamily;

        return device;
    }

    /// <summary>
    /// The features a hardware decoder needs to share the device, in a chain allocated for the device's
    /// lifetime: timeline semaphores and synchronization2, which FFmpeg can't work without, plus YCbCr
    /// conversion and host query resets where the device has them. Null when the instance or device is
    /// older than 1.3 or lacks either required feature, and then nothing beyond the renderer's own needs
    /// is enabled.
    /// </summary>
    private VkPhysicalDeviceFeatures2* chooseVideoFeatures(Candidate candidate)
    {
        if (InstanceApiVersion.Value < version_1_3.Value || candidate.Properties.apiVersion.Value < version_1_3.Value)
            return null;

        var supported11 = new VkPhysicalDeviceVulkan11Features();
        var supported12 = new VkPhysicalDeviceVulkan12Features { pNext = &supported11 };
        var supported13 = new VkPhysicalDeviceVulkan13Features { pNext = &supported12 };
        var supported = new VkPhysicalDeviceFeatures2 { pNext = &supported13 };
        InstanceApi.vkGetPhysicalDeviceFeatures2(candidate.Device, &supported);

        if (!supported12.timelineSemaphore || !supported13.synchronization2)
            return null;

        // One block for the whole chain, freed with the device. Every struct's size is a multiple of 8, so
        // each one after the first stays aligned.
        byte* block = (byte*)NativeMemory.AllocZeroed((nuint)(sizeof(VkPhysicalDeviceFeatures2) + sizeof(VkPhysicalDeviceVulkan11Features)
                                                             + sizeof(VkPhysicalDeviceVulkan12Features) + sizeof(VkPhysicalDeviceVulkan13Features)));

        var features = (VkPhysicalDeviceFeatures2*)block;
        var features11 = (VkPhysicalDeviceVulkan11Features*)(features + 1);
        var features12 = (VkPhysicalDeviceVulkan12Features*)(features11 + 1);
        var features13 = (VkPhysicalDeviceVulkan13Features*)(features12 + 1);

        *features11 = new VkPhysicalDeviceVulkan11Features { samplerYcbcrConversion = supported11.samplerYcbcrConversion };
        *features12 = new VkPhysicalDeviceVulkan12Features { pNext = features11, timelineSemaphore = true, hostQueryReset = supported12.hostQueryReset };
        *features13 = new VkPhysicalDeviceVulkan13Features { pNext = features12, synchronization2 = true };
        *features = new VkPhysicalDeviceFeatures2 { pNext = features13 };

        return features;
    }

    /// <summary>
    /// Enables present IDs with present waits, and swapchain present-mode switching, where the device has
    /// them, adding their extensions to <paramref name="extensions"/>. Returns the head of the feature chain
    /// with the enabled ones linked in front of <paramref name="next"/>.
    /// </summary>
    private void* choosePresentFeatures(Candidate candidate, List<string> extensions, VkPhysicalDevicePresentIdFeaturesKHR* presentId,
                                        VkPhysicalDevicePresentWaitFeaturesKHR* presentWait, VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR* maintenance, void* next)
    {
        // Feature support is only known through the properties2 queries.
        if (InstanceApiVersion.Minor < 1 || candidate.Properties.apiVersion.Minor < 1)
            return next;

        bool offersWait = candidate.Extensions.Contains(present_id_extension) && candidate.Extensions.Contains(present_wait_extension);
        string? swapchainMaintenance = hasSurfaceMaintenance ? swapchain_maintenance1_extensions.FirstOrDefault(candidate.Extensions.Contains) : null;

        // Only structures of extensions the device has go into the query.
        var supportedId = new VkPhysicalDevicePresentIdFeaturesKHR();
        var supportedWait = new VkPhysicalDevicePresentWaitFeaturesKHR();
        var supportedMaintenance = new VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR();
        var supported = new VkPhysicalDeviceFeatures2();

        if (offersWait)
        {
            supportedWait.pNext = &supportedId;
            supported.pNext = &supportedWait;
        }

        if (swapchainMaintenance != null)
        {
            supportedMaintenance.pNext = supported.pNext;
            supported.pNext = &supportedMaintenance;
        }

        if (supported.pNext == null)
            return next;

        InstanceApi.vkGetPhysicalDeviceFeatures2(candidate.Device, &supported);

        void* head = next;

        if (swapchainMaintenance != null && supportedMaintenance.swapchainMaintenance1)
        {
            extensions.Add(swapchainMaintenance);
            maintenance->swapchainMaintenance1 = true;
            maintenance->pNext = head;
            head = maintenance;
            SupportsPresentModeSwitching = true;
        }

        if (offersWait && supportedId.presentId && supportedWait.presentWait)
        {
            extensions.Add(present_id_extension);
            extensions.Add(present_wait_extension);
            presentWait->presentWait = true;
            presentWait->pNext = head;
            presentId->presentId = true;
            presentId->pNext = presentWait;
            head = presentId;
            SupportsPresentWait = true;
        }

        return head;
    }

    /// <summary>
    /// The queue family to decode video on, adding the extensions that takes to
    /// <paramref name="extensions"/>: the video queue extensions, and the decode extension of every codec
    /// the family reports. Null when the device has no video decode queue, or decodes no codec it has an
    /// extension for.
    /// </summary>
    private uint? chooseDecodeQueue(Candidate candidate, List<string> extensions)
    {
        if (!candidate.Extensions.Contains(video_queue_extension) || !candidate.Extensions.Contains(video_decode_queue_extension))
            return null;

        uint count;
        InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties2(candidate.Device, &count, null);

        var video = new VkQueueFamilyVideoPropertiesKHR[count];
        var families = new VkQueueFamilyProperties2[count];

        fixed (VkQueueFamilyVideoPropertiesKHR* videoProperties = video)
        fixed (VkQueueFamilyProperties2* properties = families)
        {
            for (int i = 0; i < count; i++)
            {
                videoProperties[i] = new VkQueueFamilyVideoPropertiesKHR();
                properties[i] = new VkQueueFamilyProperties2 { pNext = &videoProperties[i] };
            }

            InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties2(candidate.Device, &count, properties);
        }

        for (uint i = 0; i < count; i++)
        {
            if ((families[i].queueFamilyProperties.queueFlags & VkQueueFlags.VideoDecodeKHR) == 0)
                continue;

            VkVideoCodecOperationFlagsKHR usable = default;

            foreach (var (extension, operation) in video_decode_codecs)
            {
                if ((video[i].videoCodecOperations & operation) != 0 && candidate.Extensions.Contains(extension))
                    usable |= operation;
            }

            if (usable == default)
                continue;

            extensions.Add(video_queue_extension);
            extensions.Add(video_decode_queue_extension);

            foreach (var (extension, operation) in video_decode_codecs)
            {
                if ((usable & operation) != 0)
                    extensions.Add(extension);
            }

            if (candidate.Extensions.Contains(video_maintenance1_extension))
                extensions.Add(video_maintenance1_extension);

            DecodeOperations = usable;
            return i;
        }

        return null;
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
        Logger.Verbose(DecodeQueueFamily is uint decodeFamily
            ? $"Vulkan video: decodes {DecodeOperations} on queue family {decodeFamily}, into frames the renderer samples in place"
            : $"Vulkan video: no decode queue{(SharesVideoFrames ? string.Empty : ", and decoded frames couldn't be shared (needs Vulkan 1.3 with timeline semaphores and synchronization2)")}");
        Logger.Verbose($"Vulkan optional extensions available: {(optional.Length > 0 ? optional : "none")}");
        Logger.Verbose($"Vulkan presentation: present wait {(SupportsPresentWait ? "on" : "unavailable")}, present-mode switching {(SupportsPresentModeSwitching ? "on" : "unavailable")}, GPU timestamps {(GraphicsTimestampBits > 0 ? $"{GraphicsTimestampBits} bits" : "unavailable")}");
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
