// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// NVIDIA Reflex on a Direct3D 11 device, through NVAPI: the sleep that starts each frame just in time for the GPU, and the
/// markers that let NVIDIA's overlay and FrameView measure the PC's latency.
/// </summary>
/// <remarks>
/// NVAPI comes with NVIDIA's driver as <c>nvapi64.dll</c>, which exports one function that hands out every other by an ID.
/// The IDs, structures and values here are those of NVIDIA's MIT-licensed headers (<c>nvapi_interface.h</c>,
/// <c>nvapi.h</c>, <c>nvapi_lite_common.h</c>). Every call is made on the draw thread.
/// </remarks>
internal sealed unsafe class NvapiReflex
{
    private const string library_name = "nvapi64.dll";
    private const string query_interface_export = "nvapi_QueryInterface";

    private const uint initialize_id = 0x0150E828;
    private const uint set_sleep_mode_id = 0xAC1CA9E0;
    private const uint sleep_id = 0x852CD1D2;
    private const uint set_latency_marker_id = 0xD9984C05;

    /// <summary>
    /// NVAPI's <c>NVAPI_OK</c>.
    /// </summary>
    public const int STATUS_OK = 0;

    private readonly nint device;
    private readonly delegate* unmanaged[Cdecl]<nint, SleepModeParams*, int> setSleepMode;
    private readonly delegate* unmanaged[Cdecl]<nint, int> sleep;
    private readonly delegate* unmanaged[Cdecl]<nint, LatencyMarkerParams*, int> setLatencyMarker;

    private NvapiReflex(nint device, nint setSleepMode, nint sleep, nint setLatencyMarker)
    {
        this.device = device;
        this.setSleepMode = (delegate* unmanaged[Cdecl]<nint, SleepModeParams*, int>)setSleepMode;
        this.sleep = (delegate* unmanaged[Cdecl]<nint, int>)sleep;
        this.setLatencyMarker = (delegate* unmanaged[Cdecl]<nint, LatencyMarkerParams*, int>)setLatencyMarker;
    }

    /// <summary>
    /// Loads NVAPI and sets <paramref name="mode"/> on <paramref name="device"/>, which is how it's known that Reflex takes
    /// the device: a card from another vendor, or an NVIDIA one without Reflex, refuses it.
    /// </summary>
    /// <param name="device">The Direct3D 11 device's <c>IUnknown</c>.</param>
    /// <param name="mode">The mode to start in.</param>
    /// <param name="reason">Why Reflex can't be used, when it returns null.</param>
    /// <returns>Reflex on the device, or null where it can't be used.</returns>
    public static NvapiReflex? TryCreate(nint device, LowLatencyMode mode, out string reason)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            reason = "NVAPI is only on 64-bit x86 Windows";
            return null;
        }

        // Left loaded for the life of the process: NVAPI keeps its own state once initialized.
        if (!NativeLibrary.TryLoad(library_name, out nint library))
        {
            reason = $"no {library_name}, so no NVIDIA driver";
            return null;
        }

        if (!NativeLibrary.TryGetExport(library, query_interface_export, out nint queryAddress))
        {
            reason = $"{library_name} has no {query_interface_export}";
            return null;
        }

        var query = (delegate* unmanaged[Cdecl]<uint, nint>)queryAddress;
        nint initialize = query(initialize_id);
        nint setSleepMode = query(set_sleep_mode_id);
        nint sleep = query(sleep_id);
        nint setLatencyMarker = query(set_latency_marker_id);

        if (initialize == 0 || setSleepMode == 0 || sleep == 0 || setLatencyMarker == 0)
        {
            reason = "the NVIDIA driver is too old for Reflex";
            return null;
        }

        int status = ((delegate* unmanaged[Cdecl]<int>)initialize)();

        if (status != STATUS_OK)
        {
            reason = $"NvAPI_Initialize returned {DescribeStatus(status)}";
            return null;
        }

        var reflex = new NvapiReflex(device, setSleepMode, sleep, setLatencyMarker);
        status = reflex.SetMode(mode);

        if (status != STATUS_OK)
        {
            reason = $"NvAPI_D3D_SetSleepMode returned {DescribeStatus(status)}";
            return null;
        }

        reason = string.Empty;
        return reflex;
    }

    /// <summary>
    /// Sets whether the driver paces frames for low latency, and Boost. Only when the mode changes: the setting stays with
    /// the device, and NVIDIA advises against setting it every frame.
    /// </summary>
    /// <returns>NVAPI's status.</returns>
    public int SetMode(LowLatencyMode mode)
    {
        var parameters = CreateSleepModeParams(mode);
        return setSleepMode(device, &parameters);
    }

    /// <summary>
    /// Sleeps until the frame can start just in time for the GPU. Exactly once a frame, first in it, while it's in use:
    /// NVAPI falls back to sleeping somewhere worse after a few frames without it.
    /// </summary>
    /// <returns>NVAPI's status.</returns>
    public int Sleep() => sleep(device);

    /// <summary>
    /// Marks the moment the frame reaches <paramref name="marker"/>.
    /// </summary>
    /// <param name="frameId">The frame's ID, which only ever goes up.</param>
    /// <param name="marker">The stage.</param>
    /// <returns>NVAPI's status.</returns>
    public int Mark(ulong frameId, NvLatencyMarker marker)
    {
        var parameters = new LatencyMarkerParams
        {
            Version = VersionOf<LatencyMarkerParams>(1),
            FrameId = frameId,
            MarkerType = marker,
        };

        return setLatencyMarker(device, &parameters);
    }

    /// <summary>
    /// The parameters <see cref="SetMode"/> hands the driver for <paramref name="mode"/>. No frame rate limit (Sakura's
    /// own limiter paces frames), and markers aren't used to optimize: Sakura's draw takes an update another thread made,
    /// so the simulation markers mark that take, not a simulation the driver could reschedule.
    /// </summary>
    internal static SleepModeParams CreateSleepModeParams(LowLatencyMode mode) => new SleepModeParams
    {
        Version = VersionOf<SleepModeParams>(1),
        LowLatencyMode = mode != LowLatencyMode.Off ? (byte)1 : (byte)0,
        LowLatencyBoost = mode == LowLatencyMode.Boost ? (byte)1 : (byte)0,
        MinimumIntervalUs = 0,
        UseMarkersToOptimize = 0,
    };

    /// <summary>
    /// NVAPI's structure version: the structure's size, with the version number above it (<c>MAKE_NVAPI_VERSION</c>).
    /// </summary>
    internal static uint VersionOf<T>(int version) where T : unmanaged => (uint)sizeof(T) | (uint)version << 16;

    /// <summary>
    /// An NVAPI status with its name, where it's one that says why Reflex isn't there.
    /// </summary>
    internal static string DescribeStatus(int status) => status switch
    {
        STATUS_OK => "OK",
        -1 => "-1 (error)",
        -2 => "-2 (library not found)",
        -3 => "-3 (not implemented in this driver)",
        -4 => "-4 (not initialized)",
        -5 => "-5 (invalid argument)",
        -6 => "-6 (no NVIDIA device)",
        -9 => "-9 (incompatible structure version)",
        -104 => "-104 (not supported by this GPU)",
        _ => status.ToString()
    };

    /// <summary>
    /// <c>NV_SET_SLEEP_MODE_PARAMS_V1</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SleepModeParams
    {
        public uint Version;
        public byte LowLatencyMode;
        public byte LowLatencyBoost;
        public uint MinimumIntervalUs;
        public byte UseMarkersToOptimize;
        public byte UseMinQueueTime;
        public fixed byte Reserved[30];
    }

    /// <summary>
    /// <c>NV_LATENCY_MARKER_PARAMS_V1</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LatencyMarkerParams
    {
        public uint Version;
        public ulong FrameId;
        public NvLatencyMarker MarkerType;
        public ulong Reserved0;
        public fixed byte Reserved[56];
    }
}

/// <summary>
/// The stages of a frame Reflex is told about (<c>NV_LATENCY_MARKER_TYPE</c>).
/// </summary>
internal enum NvLatencyMarker
{
    SimulationStart = 0,
    SimulationEnd = 1,
    RenderSubmitStart = 2,
    RenderSubmitEnd = 3,
    PresentStart = 4,
    PresentEnd = 5
}
