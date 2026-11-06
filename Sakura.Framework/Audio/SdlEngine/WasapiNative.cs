// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The parts of Windows' Core Audio (the MMDevice and WASAPI COM interfaces) that read an output endpoint's figures.
/// Called through each interface's table of methods, as the system lays it out, so nothing goes through .NET's COM
/// support. Every object is made, used and released within one call, on the thread that makes it.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static unsafe class WasapiNative
{
    private const string ole32 = "ole32.dll";

    private const int rpc_e_changed_mode = unchecked((int)0x80010106);
    private const uint coinit_multithreaded = 0;
    private const uint clsctx_all = 0x17;

    private const int e_render = 0;
    private const int e_console = 0;
    private const uint device_state_active = 1;
    private const uint stgm_read = 0;
    private const ushort vt_lpwstr = 31;

    private static readonly Guid clsid_mm_device_enumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid iid_imm_device_enumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid iid_iaudio_client = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid iid_iaudio_client3 = new Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42");

    // PKEY_Device_FriendlyName: the name SDL lists an endpoint under.
    private static readonly PropertyKey friendly_name = new PropertyKey
    {
        FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        PropertyId = 14
    };

    [ThreadStatic]
    private static bool comInitialized;

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    /// <summary>
    /// PROPVARIANT: a type, then a union whose widest members are two pointers wide (16 bytes on x86, 24 on 64-bit).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Value;
        public nint Value2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [DllImport(ole32)]
    private static extern int CoInitializeEx(void* reserved, uint coInit);

    [DllImport(ole32)]
    private static extern int CoCreateInstance(Guid* classId, nint outer, uint context, Guid* interfaceId, nint* instance);

    [DllImport(ole32)]
    private static extern void CoTaskMemFree(void* memory);

    [DllImport(ole32)]
    private static extern int PropVariantClear(PropVariant* value);

    /// <summary>
    /// Makes a device enumerator, joining the thread to COM's multithreaded apartment first if it's in none. A thread
    /// already in a single-threaded one stays there, which works as well, since nothing made here leaves the call.
    /// </summary>
    /// <returns>The enumerator, to <see cref="Release"/>, or 0 where it can't be made.</returns>
    public static nint CreateEnumerator()
    {
        if (!comInitialized)
        {
            int initialized = CoInitializeEx(null, coinit_multithreaded);

            if (initialized < 0 && initialized != rpc_e_changed_mode)
                return 0;

            comInitialized = true;
        }

        Guid classId = clsid_mm_device_enumerator;
        Guid interfaceId = iid_imm_device_enumerator;
        nint enumerator;

        return CoCreateInstance(&classId, 0, clsctx_all, &interfaceId, &enumerator) >= 0 ? enumerator : 0;
    }

    /// <summary>
    /// The system's default output, for games and music (eConsole, as SDL's default is).
    /// </summary>
    /// <returns>The endpoint, to <see cref="Release"/>, or 0 where there's none.</returns>
    public static nint GetDefaultOutput(nint enumerator)
    {
        nint device;

        // IMMDeviceEnumerator::GetDefaultAudioEndpoint
        int result = ((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)method(enumerator, 4))(enumerator, e_render, e_console, &device);
        return result >= 0 ? device : 0;
    }

    /// <summary>
    /// Finds the output that's plugged in under the name SDL lists it as.
    /// </summary>
    /// <returns>The endpoint, to <see cref="Release"/>, or 0 where no output has the name.</returns>
    public static nint FindOutput(nint enumerator, string name)
    {
        nint collection;

        // IMMDeviceEnumerator::EnumAudioEndpoints
        if (((delegate* unmanaged[Stdcall]<nint, int, uint, nint*, int>)method(enumerator, 3))(enumerator, e_render, device_state_active, &collection) < 0)
            return 0;

        try
        {
            uint count;

            // IMMDeviceCollection::GetCount
            if (((delegate* unmanaged[Stdcall]<nint, uint*, int>)method(collection, 3))(collection, &count) < 0)
                return 0;

            for (uint i = 0; i < count; i++)
            {
                nint device;

                // IMMDeviceCollection::Item
                if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)method(collection, 4))(collection, i, &device) < 0)
                    continue;

                if (GetName(device) == name)
                    return device;

                Release(device);
            }

            return 0;
        }
        finally
        {
            Release(collection);
        }
    }

    /// <summary>
    /// The endpoint's friendly name ("Speakers (Realtek(R) Audio)"), as SDL lists it.
    /// </summary>
    /// <returns>The name, or null where it can't be read.</returns>
    public static string? GetName(nint device)
    {
        nint store;

        // IMMDevice::OpenPropertyStore
        if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)method(device, 4))(device, stgm_read, &store) < 0)
            return null;

        try
        {
            PropertyKey key = friendly_name;
            PropVariant value = default;

            // IPropertyStore::GetValue
            if (((delegate* unmanaged[Stdcall]<nint, PropertyKey*, PropVariant*, int>)method(store, 5))(store, &key, &value) < 0)
                return null;

            string? name = value.Type == vt_lpwstr && value.Value != 0 ? Marshal.PtrToStringUni(value.Value) : null;
            PropVariantClear(&value);
            return name;
        }
        finally
        {
            Release(store);
        }
    }

    /// <summary>
    /// The shared audio engine's period on the endpoint: how often it takes a period from every app and mixes it for
    /// the device. The current one where the system says (Windows 10 on), as an app asking for a short one shortens it
    /// for everyone; the default one otherwise.
    /// </summary>
    /// <returns>Whether the endpoint said.</returns>
    public static bool TryGetEnginePeriod(nint device, out uint frames, out uint sampleRate, out bool current)
    {
        frames = 0;
        sampleRate = 0;
        current = false;

        nint client = activate(device, iid_iaudio_client3);

        if (client != 0)
        {
            try
            {
                WaveFormatEx* format;
                uint currentFrames;

                // IAudioClient3::GetCurrentSharedModeEnginePeriod
                if (((delegate* unmanaged[Stdcall]<nint, WaveFormatEx**, uint*, int>)method(client, 19))(client, &format, &currentFrames) >= 0)
                {
                    frames = currentFrames;
                    sampleRate = format->SamplesPerSecond;
                    CoTaskMemFree(format);
                    current = true;
                    return frames > 0 && sampleRate > 0;
                }
            }
            finally
            {
                Release(client);
            }
        }

        client = activate(device, iid_iaudio_client);

        if (client == 0)
            return false;

        try
        {
            WaveFormatEx* format;
            long defaultPeriod;

            // IAudioClient::GetMixFormat, then IAudioClient::GetDevicePeriod (in 100 ns units)
            if (((delegate* unmanaged[Stdcall]<nint, WaveFormatEx**, int>)method(client, 8))(client, &format) < 0)
                return false;

            sampleRate = format->SamplesPerSecond;
            CoTaskMemFree(format);

            if (((delegate* unmanaged[Stdcall]<nint, long*, long*, int>)method(client, 9))(client, &defaultPeriod, null) < 0)
                return false;

            frames = (uint)Math.Round(defaultPeriod / 10_000_000.0 * sampleRate);
            return frames > 0 && sampleRate > 0;
        }
        finally
        {
            Release(client);
        }
    }

    /// <summary>
    /// IUnknown::Release.
    /// </summary>
    public static void Release(nint unknown)
    {
        if (unknown != 0)
            ((delegate* unmanaged[Stdcall]<nint, uint>)method(unknown, 2))(unknown);
    }

    /// <summary>
    /// IMMDevice::Activate, for an interface on the endpoint: 0 where it has none.
    /// </summary>
    private static nint activate(nint device, Guid interfaceId)
    {
        nint client;
        return ((delegate* unmanaged[Stdcall]<nint, Guid*, uint, void*, nint*, int>)method(device, 3))(device, &interfaceId, clsctx_all, null, &client) >= 0 ? client : 0;
    }

    /// <summary>
    /// The object's method at <paramref name="slot"/> in its table, counting IUnknown's three.
    /// </summary>
    private static void* method(nint instance, int slot) => (*(void***)instance)[slot];
}
