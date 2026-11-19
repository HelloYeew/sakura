// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// AMD Anti-Lag 2 on Direct3D 11, through the driver extension AMD's Anti-Lag 2 SDK uses: a call just before input is taken
/// that delays the frame until it can start just in time for the GPU.
/// </summary>
/// <remarks>
/// A port of the SDK's MIT-licensed <c>ffx_antilag2_dx11.h</c>. AMD's Direct3D 11 driver is loaded into the process as
/// <c>amdxx64.dll</c> only when the device is on an AMD card, so finding it is how the card is known. Its
/// <c>AmdDxExtCreate11</c> hands out the Anti-Lag interface when the out pointer already holds the request's identifier,
/// and the interface is a C++ object whose fourth virtual function sets and applies the state. Every call is made on the
/// draw thread.
/// </remarks>
internal sealed unsafe class AmdAntiLag2 : IDisposable
{
    private const string library_name = "amdxx64.dll";
    private const string create_export = "AmdDxExtCreate11";

    // What the header puts in the out pointer to ask for the Anti-Lag interface.
    private const ulong anti_lag_request = 0xbf380ebc5ab4d0a6;

    private const uint mode_on = 1;
    private const uint mode_off = 2;

    // The interface's virtual functions, in order: AddRef, Release, the destructor, UpdateAntiLagStateDx11.
    private const int release_slot = 1;
    private const int update_slot = 3;

    private const int s_ok = 0;
    private const int s_false = 1;

    private nint api;
    private bool enabled;

    /// <summary>
    /// What the header passes with every change of state.
    /// </summary>
    private static ReadOnlySpan<byte> controlString => "delag_next_osd_supported_in_dxxp = 1"u8;

    private AmdAntiLag2(nint api)
    {
        this.api = api;
    }

    /// <summary>
    /// Gets the Anti-Lag interface from AMD's Direct3D 11 driver, starting disabled, as the header does.
    /// </summary>
    /// <param name="reason">Why Anti-Lag 2 can't be used, when it returns null.</param>
    /// <returns>Anti-Lag 2, or null where it can't be used.</returns>
    public static AmdAntiLag2? TryCreate(out string reason)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            reason = "AMD's driver extension is only on 64-bit x86 Windows";
            return null;
        }

        nint module = GetModuleHandleW(library_name);

        if (module == 0)
        {
            reason = $"{library_name} isn't loaded, so the device isn't on an AMD card";
            return null;
        }

        if (!NativeLibrary.TryGetExport(module, create_export, out nint createAddress))
        {
            reason = $"{library_name} has no {create_export}";
            return null;
        }

        nint api = unchecked((nint)anti_lag_request);
        int result = ((delegate* unmanaged[Cdecl]<nint, nint*, int>)createAddress)(0, &api);

        if (result != s_ok || api == 0)
        {
            reason = $"{create_export} returned 0x{result:X8}: the driver is too old for Anti-Lag 2";
            return null;
        }

        var antiLag = new AmdAntiLag2(api);
        var data = CreateApiData(false, null, 0);
        result = antiLag.update(&data);

        if (result != s_ok)
        {
            antiLag.Dispose();
            reason = $"UpdateAntiLagStateDx11 returned 0x{result:X8}";
            return null;
        }

        reason = string.Empty;
        return antiLag;
    }

    /// <summary>
    /// Turns Anti-Lag 2 on or off, when that changes. AMD's header sets the state only on a change, never every frame.
    /// </summary>
    /// <returns>The driver's result.</returns>
    public int SetEnabled(bool enabled)
    {
        if (api == 0 || enabled == this.enabled)
            return s_ok;

        this.enabled = enabled;

        fixed (byte* control = controlString)
        {
            var data = CreateApiData(enabled, control, (uint)controlString.Length);
            return update(&data);
        }
    }

    /// <summary>
    /// Delays the frame until it can start just in time for the GPU. Once a frame, just before input is taken, while it's
    /// enabled.
    /// </summary>
    /// <returns>Whether the driver took it.</returns>
    public bool Update()
    {
        if (api == 0)
            return false;

        int result = update(null);
        return result is s_ok or s_false;
    }

    /// <summary>
    /// AMD's <c>APIData_v1</c> for a change of state.
    /// </summary>
    internal static ApiData CreateApiData(bool enabled, byte* control, uint controlLength) => new ApiData
    {
        Size = (uint)sizeof(ApiData),
        Version = 1,
        Mode = enabled ? mode_on : mode_off,
        ControlString = control,
        ControlStringLength = controlLength,
        MaxFps = 0,
    };

    /// <summary>
    /// Calls the interface's <c>UpdateAntiLagStateDx11</c>: with data to set the state, with null to delay the frame.
    /// </summary>
    private int update(ApiData* data)
    {
        nint* vtable = *(nint**)api;
        return ((delegate* unmanaged[Thiscall]<nint, ApiData*, int>)vtable[update_slot])(api, data);
    }

    /// <summary>
    /// Releases the driver's interface.
    /// </summary>
    public void Dispose()
    {
        if (api == 0)
            return;

        nint* vtable = *(nint**)api;
        ((delegate* unmanaged[Thiscall]<nint, uint>)vtable[release_slot])(api);
        api = 0;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint GetModuleHandleW(string moduleName);

    /// <summary>
    /// <c>APIData_v1</c>, 32 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ApiData
    {
        public uint Size;
        public uint Version;
        public uint Mode;
        public byte* ControlString;
        public uint ControlStringLength;
        public uint MaxFps;
    }
}
