// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The parts of Windows' Core Audio (the MMDevice and WASAPI COM interfaces) that read an output endpoint's figures and
/// open a stream on it. Called through each interface's table of methods, as the system lays it out, so nothing goes
/// through .NET's COM support. Every object is made, used and released on one thread: within one call for the route,
/// and on its own render thread for <see cref="WasapiOutput"/>.
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
    private static readonly Guid iid_iaudio_render_client = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
    private static readonly Guid iid_iaudio_clock = new Guid("CD63314F-3FBA-4A1B-812C-EF96358728E7");

    public const int SHARE_MODE_SHARED = 0;
    public const int SHARE_MODE_EXCLUSIVE = 1;

    public const uint STREAM_FLAG_EVENT_CALLBACK = 0x00040000;
    public const uint STREAM_FLAG_AUTO_CONVERT_PCM = 0x80000000;
    public const uint STREAM_FLAG_SRC_DEFAULT_QUALITY = 0x08000000;

    public const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);
    public const int AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED = unchecked((int)0x88890019);

    public const ushort WAVE_FORMAT_PCM = 1;
    public const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
    public const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;

    // The size of WAVEFORMATEXTENSIBLE's part after WAVEFORMATEX, which its cbSize says.
    public const ushort EXTENSIBLE_EXTRA_SIZE = 22;

    public static readonly Guid SUBTYPE_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");
    public static readonly Guid SUBTYPE_IEEE_FLOAT = new Guid("00000003-0000-0010-8000-00AA00389B71");

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

    /// <summary>
    /// WAVEFORMATEX, packed as the system declares it (18 bytes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    /// <summary>
    /// WAVEFORMATEXTENSIBLE (40 bytes): the format, the bits that carry the sample, which speakers the channels are, and
    /// what the samples are.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WaveFormatExtensible
    {
        public WaveFormatEx Format;
        public ushort ValidBitsPerSample;
        public uint ChannelMask;
        public Guid SubFormat;
    }

    [DllImport("kernel32.dll")]
    public static extern nint CreateEventW(void* attributes, int manualReset, int initialState, char* name);

    [DllImport("kernel32.dll")]
    public static extern int SetEvent(nint handle);

    [DllImport("kernel32.dll")]
    public static extern int CloseHandle(nint handle);

    private static nint waitForSingleObject;

    /// <summary>
    /// The address of kernel32's WaitForSingleObject, which the native render loop waits on the stream's event with.
    /// </summary>
    public static nint WaitForSingleObject
    {
        get
        {
            if (waitForSingleObject == 0)
                waitForSingleObject = NativeLibrary.GetExport(NativeLibrary.Load("kernel32.dll"), "WaitForSingleObject");

            return waitForSingleObject;
        }
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
    /// The endpoint's ID, which stays the same while it's plugged in and tells two of the same name apart.
    /// </summary>
    /// <returns>The ID, or null where it can't be read.</returns>
    public static string? GetId(nint device)
    {
        char* id;

        // IMMDevice::GetId
        if (((delegate* unmanaged[Stdcall]<nint, char**, int>)method(device, 5))(device, &id) < 0 || id == null)
            return null;

        string value = new string(id);
        CoTaskMemFree(id);
        return value;
    }

    /// <summary>
    /// An IAudioClient on the endpoint, as IAudioClient3 where the system has it (Windows 10 on): its methods come after
    /// IAudioClient's, so one pointer serves both.
    /// </summary>
    /// <param name="device">The endpoint.</param>
    /// <param name="client3">Whether it's an IAudioClient3.</param>
    /// <returns>The client, to <see cref="Release"/>, or 0 where the endpoint has none.</returns>
    public static nint ActivateClient(nint device, out bool client3)
    {
        nint client = activate(device, iid_iaudio_client3);
        client3 = client != 0;
        return client3 ? client : activate(device, iid_iaudio_client);
    }

    /// <summary>
    /// IAudioClient::GetMixFormat: the shared engine's format on the endpoint, as WAVEFORMATEXTENSIBLE whatever form it
    /// came in.
    /// </summary>
    public static int GetMixFormat(nint client, out WaveFormatExtensible format)
    {
        format = default;
        WaveFormatEx* mix;

        int result = ((delegate* unmanaged[Stdcall]<nint, WaveFormatEx**, int>)method(client, 8))(client, &mix);

        if (result < 0)
            return result;

        if (mix->FormatTag == WAVE_FORMAT_EXTENSIBLE && mix->ExtraSize >= EXTENSIBLE_EXTRA_SIZE)
        {
            format = *(WaveFormatExtensible*)mix;
        }
        else
        {
            format.Format = *mix;
            format.ValidBitsPerSample = mix->BitsPerSample;
            format.SubFormat = mix->FormatTag == WAVE_FORMAT_IEEE_FLOAT ? SUBTYPE_IEEE_FLOAT : SUBTYPE_PCM;
        }

        CoTaskMemFree(mix);
        return result;
    }

    /// <summary>
    /// IAudioClient::IsFormatSupported, for exclusive mode: S_OK where the device plays the format as it is.
    /// </summary>
    public static int IsFormatSupported(nint client, int shareMode, WaveFormatExtensible* format)
    {
        WaveFormatEx* closest = null;

        // Exclusive mode takes no closest match to fill in.
        WaveFormatEx** closestMatch = shareMode == SHARE_MODE_EXCLUSIVE ? null : &closest;

        int result = ((delegate* unmanaged[Stdcall]<nint, int, WaveFormatExtensible*, WaveFormatEx**, int>)method(client, 7))(client, shareMode, format, closestMatch);

        if (closest != null)
            CoTaskMemFree(closest);

        return result;
    }

    /// <summary>
    /// IAudioClient::Initialize. The durations are in 100 ns units.
    /// </summary>
    public static int Initialize(nint client, int shareMode, uint flags, long bufferDuration, long periodicity, WaveFormatExtensible* format) =>
        ((delegate* unmanaged[Stdcall]<nint, int, uint, long, long, WaveFormatExtensible*, Guid*, int>)method(client, 3))(client, shareMode, flags, bufferDuration, periodicity, format, null);

    /// <summary>
    /// IAudioClient::GetDevicePeriod, in 100 ns units: the shared engine's default period, and the shortest an exclusive
    /// stream can have.
    /// </summary>
    public static int GetDevicePeriod(nint client, out long defaultPeriod, out long minimumPeriod)
    {
        long defaultValue, minimumValue;
        int result = ((delegate* unmanaged[Stdcall]<nint, long*, long*, int>)method(client, 9))(client, &defaultValue, &minimumValue);

        defaultPeriod = defaultValue;
        minimumPeriod = minimumValue;
        return result;
    }

    /// <summary>
    /// IAudioClient3::GetSharedModeEnginePeriod: the periods the shared engine can run a stream of this format at, in
    /// frames. A low-latency stream's must be a multiple of the fundamental one, from the minimum to the maximum.
    /// </summary>
    public static int GetSharedModeEnginePeriod(nint client3, WaveFormatExtensible* format, out uint defaultPeriod, out uint fundamentalPeriod,
                                                out uint minimumPeriod, out uint maximumPeriod)
    {
        uint defaultValue, fundamentalValue, minimumValue, maximumValue;

        int result = ((delegate* unmanaged[Stdcall]<nint, WaveFormatExtensible*, uint*, uint*, uint*, uint*, int>)method(client3, 18))(
            client3, format, &defaultValue, &fundamentalValue, &minimumValue, &maximumValue);

        defaultPeriod = defaultValue;
        fundamentalPeriod = fundamentalValue;
        minimumPeriod = minimumValue;
        maximumPeriod = maximumValue;
        return result;
    }

    /// <summary>
    /// IAudioClient3::InitializeSharedAudioStream: a shared stream at a period shorter than the engine's default, which
    /// shortens the engine's own on the endpoint while it plays.
    /// </summary>
    public static int InitializeSharedAudioStream(nint client3, uint flags, uint periodFrames, WaveFormatExtensible* format) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, WaveFormatExtensible*, Guid*, int>)method(client3, 20))(client3, flags, periodFrames, format, null);

    /// <summary>
    /// IAudioClient::GetBufferSize, in frames.
    /// </summary>
    public static int GetBufferSize(nint client, out uint frames)
    {
        uint value;
        int result = ((delegate* unmanaged[Stdcall]<nint, uint*, int>)method(client, 4))(client, &value);

        frames = value;
        return result;
    }

    /// <summary>
    /// IAudioClient::SetEventHandle: the event the stream signals each time it wants more.
    /// </summary>
    public static int SetEventHandle(nint client, nint handle) => ((delegate* unmanaged[Stdcall]<nint, nint, int>)method(client, 13))(client, handle);

    /// <summary>
    /// IAudioClient::Reset, once stopped: drops what's left in the buffer.
    /// </summary>
    public static int Reset(nint client) => ((delegate* unmanaged[Stdcall]<nint, int>)method(client, 12))(client);

    /// <summary>
    /// IAudioClient::GetService for the stream's IAudioRenderClient.
    /// </summary>
    /// <returns>It, to <see cref="Release"/>, or 0.</returns>
    public static nint GetRenderClient(nint client) => getService(client, iid_iaudio_render_client);

    /// <summary>
    /// IAudioClient::GetService for the stream's IAudioClock.
    /// </summary>
    /// <returns>It, to <see cref="Release"/>, or 0.</returns>
    public static nint GetClock(nint client) => getService(client, iid_iaudio_clock);

    /// <summary>
    /// IAudioClock::GetFrequency: the device position's units a second.
    /// </summary>
    public static int GetClockFrequency(nint clock, out ulong frequency)
    {
        ulong value;
        int result = ((delegate* unmanaged[Stdcall]<nint, ulong*, int>)method(clock, 3))(clock, &value);

        frequency = value;
        return result;
    }

    private static nint getService(nint client, Guid interfaceId)
    {
        nint service;

        // IAudioClient::GetService
        return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)method(client, 14))(client, &interfaceId, &service) >= 0 ? service : 0;
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
