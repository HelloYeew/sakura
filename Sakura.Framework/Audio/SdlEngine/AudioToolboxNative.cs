// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The parts of Apple's AudioToolbox (its C audio unit API) that <see cref="RemoteIOOutput"/> uses. Every call is made
/// off the audio callback.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static unsafe class AudioToolboxNative
{
    private const string lib_name = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    public const int NO_ERR = 0;

    public const uint TYPE_OUTPUT = 0x61756f75; // 'auou'
    public const uint SUBTYPE_REMOTE_IO = 0x72696f63; // 'rioc'
    public const uint MANUFACTURER_APPLE = 0x6170706c; // 'appl'

    public const uint PROPERTY_STREAM_FORMAT = 8;
    public const uint PROPERTY_MAXIMUM_FRAMES_PER_SLICE = 14;
    public const uint PROPERTY_SET_RENDER_CALLBACK = 23;

    public const uint SCOPE_GLOBAL = 0;
    public const uint SCOPE_INPUT = 1;

    // The output unit's speaker side: what's rendered into its input scope goes out through it.
    public const uint OUTPUT_ELEMENT = 0;

    public const uint FORMAT_LINEAR_PCM = 0x6c70636d; // 'lpcm'
    public const uint FORMAT_FLAG_IS_FLOAT = 1 << 0;
    public const uint FORMAT_FLAG_IS_PACKED = 1 << 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioComponentDescription
    {
        public uint ComponentType;
        public uint ComponentSubType;
        public uint ComponentManufacturer;
        public uint ComponentFlags;
        public uint ComponentFlagsMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatID;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AURenderCallbackStruct
    {
        public nint InputProc;
        public nint InputProcRefCon;
    }

    [DllImport(lib_name)]
    public static extern nint AudioComponentFindNext(nint component, AudioComponentDescription* description);

    [DllImport(lib_name)]
    public static extern int AudioComponentInstanceNew(nint component, out nint instance);

    [DllImport(lib_name)]
    public static extern int AudioComponentInstanceDispose(nint instance);

    [DllImport(lib_name)]
    public static extern int AudioUnitSetProperty(nint unit, uint propertyId, uint scope, uint element, void* data, uint dataSize);

    [DllImport(lib_name)]
    public static extern int AudioUnitInitialize(nint unit);

    [DllImport(lib_name)]
    public static extern int AudioUnitUninitialize(nint unit);

    [DllImport(lib_name)]
    public static extern int AudioOutputUnitStart(nint unit);

    [DllImport(lib_name)]
    public static extern int AudioOutputUnitStop(nint unit);

    /// <summary>
    /// An OSStatus as Apple's tools print it: its four characters where it's a four-character code, the number
    /// otherwise.
    /// </summary>
    public static string DescribeStatus(int status)
    {
        uint value = (uint)status;
        char[] code = [(char)(value >> 24), (char)((value >> 16) & 0xff), (char)((value >> 8) & 0xff), (char)(value & 0xff)];

        foreach (char c in code)
        {
            if (c < 0x20 || c > 0x7e)
                return status.ToString();
        }

        return $"'{new string(code)}' ({status})";
    }
}
