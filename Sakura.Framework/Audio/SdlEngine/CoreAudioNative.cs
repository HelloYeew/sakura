// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The parts of Apple's Core Audio hardware API (and the Core Foundation calls its strings need) that read a macOS
/// output device's properties. None of them is called on the audio callback.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static unsafe class CoreAudioNative
{
    private const string core_audio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string core_foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public const int NO_ERR = 0;

    public const uint SYSTEM_OBJECT = 1;

    public const uint PROPERTY_DEFAULT_OUTPUT_DEVICE = 0x644f7574; // 'dOut'
    public const uint PROPERTY_DEVICES = 0x64657623; // 'dev#'
    public const uint PROPERTY_NAME = 0x6c6e616d; // 'lnam'
    public const uint PROPERTY_TRANSPORT_TYPE = 0x7472616e; // 'tran'
    public const uint PROPERTY_NOMINAL_SAMPLE_RATE = 0x6e737274; // 'nsrt'
    public const uint PROPERTY_BUFFER_FRAME_SIZE = 0x6673697a; // 'fsiz'
    public const uint PROPERTY_BUFFER_FRAME_SIZE_RANGE = 0x66737a23; // 'fsz#'
    public const uint PROPERTY_SAFETY_OFFSET = 0x73616674; // 'saft'
    public const uint PROPERTY_STREAMS = 0x73746d23; // 'stm#'

    // A device's latency and a stream's share the selector.
    public const uint PROPERTY_LATENCY = 0x6c746e63; // 'ltnc'

    public const uint SCOPE_GLOBAL = 0x676c6f62; // 'glob'
    public const uint SCOPE_OUTPUT = 0x6f757470; // 'outp'

    public const uint ELEMENT_MAIN = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioObjectPropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioValueRange
    {
        public double Minimum;
        public double Maximum;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CFRange
    {
        public nint Location;
        public nint Length;
    }

    [DllImport(core_audio)]
    public static extern int AudioObjectGetPropertyDataSize(uint objectId, AudioObjectPropertyAddress* address, uint qualifierDataSize, void* qualifierData,
                                                            uint* dataSize);

    [DllImport(core_audio)]
    public static extern int AudioObjectGetPropertyData(uint objectId, AudioObjectPropertyAddress* address, uint qualifierDataSize, void* qualifierData,
                                                        uint* dataSize, void* data);

    [DllImport(core_audio)]
    public static extern int AudioObjectSetPropertyData(uint objectId, AudioObjectPropertyAddress* address, uint qualifierDataSize, void* qualifierData,
                                                        uint dataSize, void* data);

    [DllImport(core_foundation)]
    public static extern nint CFStringGetLength(nint text);

    [DllImport(core_foundation)]
    public static extern void CFStringGetCharacters(nint text, CFRange range, char* buffer);

    [DllImport(core_foundation)]
    public static extern void CFRelease(nint value);

    /// <summary>
    /// Reads a property that's one fixed-size value.
    /// </summary>
    /// <returns>Whether the object has the property and gave a value of that size.</returns>
    public static bool TryGet<T>(uint objectId, uint selector, uint scope, out T value)
        where T : unmanaged
    {
        var address = new AudioObjectPropertyAddress
        {
            Selector = selector,
            Scope = scope,
            Element = ELEMENT_MAIN
        };

        T result;
        uint size = (uint)sizeof(T);

        bool read = AudioObjectGetPropertyData(objectId, &address, 0, null, &size, &result) == NO_ERR && size == sizeof(T);

        value = read ? result : default;
        return read;
    }

    /// <summary>
    /// Sets a property that's one fixed-size value.
    /// </summary>
    /// <returns>The status Core Audio gave, <see cref="NO_ERR"/> where it took it.</returns>
    public static int Set<T>(uint objectId, uint selector, uint scope, T value)
        where T : unmanaged
    {
        var address = new AudioObjectPropertyAddress
        {
            Selector = selector,
            Scope = scope,
            Element = ELEMENT_MAIN
        };

        return AudioObjectSetPropertyData(objectId, &address, 0, null, (uint)sizeof(T), &value);
    }

    /// <summary>
    /// The system's default output device, which follows what's picked in the menu bar and in System Settings.
    /// </summary>
    /// <returns>The device, or 0 where there's none.</returns>
    public static uint DefaultOutputDevice() => TryGet(SYSTEM_OBJECT, PROPERTY_DEFAULT_OUTPUT_DEVICE, SCOPE_GLOBAL, out uint device) ? device : 0;

    /// <summary>
    /// Finds the output device SDL lists under <paramref name="name"/>.
    /// </summary>
    /// <returns>The device, or 0 where no output device has the name.</returns>
    public static uint FindOutputDevice(string name)
    {
        foreach (uint device in GetObjects(SYSTEM_OBJECT, PROPERTY_DEVICES, SCOPE_GLOBAL))
        {
            // A headset is two devices of the same name, the microphone and the speakers.
            if (GetObjects(device, PROPERTY_STREAMS, SCOPE_OUTPUT).Length == 0)
                continue;

            if (GetDeviceName(device) == name)
                return device;
        }

        return 0;
    }

    /// <summary>
    /// An output device's name, as SDL lists it: Core Audio's, less the spaces some devices' names end in.
    /// </summary>
    /// <returns>The name, or null where the device can't say.</returns>
    public static string? GetDeviceName(uint device) => GetString(device, PROPERTY_NAME, SCOPE_OUTPUT)?.TrimEnd(' ');

    /// <summary>
    /// Reads a property that's a list of objects: the system's devices, or a device's streams.
    /// </summary>
    /// <returns>The objects, or none where the property can't be read.</returns>
    public static uint[] GetObjects(uint objectId, uint selector, uint scope)
    {
        var address = new AudioObjectPropertyAddress
        {
            Selector = selector,
            Scope = scope,
            Element = ELEMENT_MAIN
        };

        uint size;

        if (AudioObjectGetPropertyDataSize(objectId, &address, 0, null, &size) != NO_ERR || size < sizeof(uint))
            return Array.Empty<uint>();

        uint[] objects = new uint[size / sizeof(uint)];

        fixed (uint* data = objects)
        {
            if (AudioObjectGetPropertyData(objectId, &address, 0, null, &size, data) != NO_ERR)
                return Array.Empty<uint>();
        }

        // Fewer than there was room for, where one went in between the two reads.
        return objects.AsSpan(0, (int)(size / sizeof(uint))).ToArray();
    }

    /// <summary>
    /// Reads a property that's a string, such as a device's name.
    /// </summary>
    /// <returns>The string, or null where the property can't be read.</returns>
    public static string? GetString(uint objectId, uint selector, uint scope)
    {
        if (!TryGet(objectId, selector, scope, out nint text) || text == 0)
            return null;

        try
        {
            int length = (int)CFStringGetLength(text);

            if (length == 0)
                return string.Empty;

            char[] characters = new char[length];

            fixed (char* buffer = characters)
            {
                CFStringGetCharacters(text, new CFRange
                {
                    Location = 0,
                    Length = length
                }, buffer);
            }

            return new string(characters);
        }
        finally
        {
            // A string property is handed over with a reference the reader owns.
            CFRelease(text);
        }
    }
}
