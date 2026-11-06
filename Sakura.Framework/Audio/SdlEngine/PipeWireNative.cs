// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The parts of libpipewire that <see cref="PipeWireOutput"/> plays a stream through, and the one SPA object it needs,
/// its format, written out by hand the way <c>spa_format_audio_raw_build</c> writes it. Every call on the stream is made
/// with the thread loop locked, as libpipewire asks, and none is made on its real-time thread.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static unsafe class PipeWireNative
{
    private const string lib_name = "libpipewire-0.3.so.0";

    public const int DIRECTION_OUTPUT = 1;
    public const uint ID_ANY = 0xffffffff;

    public const uint STREAM_FLAG_AUTOCONNECT = 1 << 0;
    public const uint STREAM_FLAG_INACTIVE = 1 << 1;
    public const uint STREAM_FLAG_MAP_BUFFERS = 1 << 2;
    public const uint STREAM_FLAG_RT_PROCESS = 1 << 4;

    public const int STREAM_STATE_ERROR = -1;
    public const int STREAM_STATE_UNCONNECTED = 0;
    public const int STREAM_STATE_STREAMING = 3;

    // PW_VERSION_STREAM_EVENTS: the events up to trigger_done.
    public const uint STREAM_EVENTS_VERSION = 2;

    // struct spa_hook: a list link and callbacks (two pointers each), then removed and priv.
    public static readonly int HOOK_SIZE = 6 * IntPtr.Size;

    // The SPA types, ids and keys the format is written with (spa/utils/type.h, spa/param/param.h, spa/param/format.h,
    // spa/param/audio/raw.h).
    private const uint spa_type_id = 3;
    private const uint spa_type_int = 4;
    private const uint spa_type_array = 13;
    private const uint spa_type_object = 15;
    private const uint spa_type_object_format = 0x40003;
    private const uint spa_param_enum_format = 3;
    private const uint spa_format_media_type = 1;
    private const uint spa_format_media_subtype = 2;
    private const uint spa_format_audio_format = 0x10001;
    private const uint spa_format_audio_rate = 0x10003;
    private const uint spa_format_audio_channels = 0x10004;
    private const uint spa_format_audio_position = 0x10005;
    private const uint spa_media_type_audio = 1;
    private const uint spa_media_subtype_raw = 1;
    private const uint spa_audio_format_f32_le = 0x11b;
    private const uint spa_audio_channel_mono = 2;
    private const uint spa_audio_channel_fl = 3;
    private const uint spa_audio_channel_fr = 4;

    /// <summary>
    /// struct pw_stream_events, as of version 2.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StreamEvents
    {
        public uint Version;
        public nint Destroy;
        public nint StateChanged;
        public nint ControlInfo;
        public nint IoChanged;
        public nint ParamChanged;
        public nint AddBuffer;
        public nint RemoveBuffer;
        public nint Process;
        public nint Drained;
        public nint Command;
        public nint TriggerDone;
    }

    /// <summary>
    /// struct pw_time, as of 1.0. An older libpipewire fills in fewer of its fields (pw_stream_get_time_n takes the
    /// size), and leaves the rest 0.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Time
    {
        public long Now;
        public uint RateNumerator;
        public uint RateDenominator;
        public ulong Ticks;
        public long Delay;
        public ulong Queued;
        public ulong Buffered;
        public uint QueuedBuffers;
        public uint AvailableBuffers;
        public ulong Size;
    }

    [DllImport(lib_name)]
    public static extern void pw_init(nint argc, nint argv);

    [DllImport(lib_name)]
    public static extern nint pw_get_library_version();

    [DllImport(lib_name)]
    public static extern nint pw_thread_loop_new([MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint properties);

    [DllImport(lib_name)]
    public static extern nint pw_thread_loop_get_loop(nint loop);

    [DllImport(lib_name)]
    public static extern int pw_thread_loop_start(nint loop);

    [DllImport(lib_name)]
    public static extern void pw_thread_loop_stop(nint loop);

    [DllImport(lib_name)]
    public static extern void pw_thread_loop_lock(nint loop);

    [DllImport(lib_name)]
    public static extern void pw_thread_loop_unlock(nint loop);

    [DllImport(lib_name)]
    public static extern void pw_thread_loop_destroy(nint loop);

    [DllImport(lib_name)]
    public static extern nint pw_context_new(nint mainLoop, nint properties, nuint userDataSize);

    [DllImport(lib_name)]
    public static extern void pw_context_destroy(nint context);

    [DllImport(lib_name)]
    public static extern nint pw_context_connect(nint context, nint properties, nuint userDataSize);

    [DllImport(lib_name)]
    public static extern int pw_core_disconnect(nint core);

    [DllImport(lib_name)]
    public static extern nint pw_properties_new_string([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [DllImport(lib_name)]
    public static extern int pw_properties_set(nint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(lib_name)]
    public static extern nint pw_stream_new(nint core, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint properties);

    [DllImport(lib_name)]
    public static extern void pw_stream_add_listener(nint stream, nint listener, StreamEvents* events, nint data);

    [DllImport(lib_name)]
    public static extern int pw_stream_connect(nint stream, int direction, uint targetId, uint flags, nint* parameters, uint parameterCount);

    [DllImport(lib_name)]
    public static extern int pw_stream_set_active(nint stream, [MarshalAs(UnmanagedType.U1)] bool active);

    [DllImport(lib_name)]
    public static extern int pw_stream_get_state(nint stream, nint* error);

    [DllImport(lib_name)]
    public static extern int pw_stream_get_time_n(nint stream, Time* time, nuint size);

    [DllImport(lib_name)]
    public static extern int pw_stream_disconnect(nint stream);

    [DllImport(lib_name)]
    public static extern void pw_stream_destroy(nint stream);

    private static nint library;

    /// <summary>
    /// The address of one of libpipewire's functions, for the native process callback to call: pw_stream_dequeue_buffer
    /// and pw_stream_queue_buffer.
    /// </summary>
    public static nint GetExport(string name)
    {
        if (library == 0)
            library = NativeLibrary.Load(lib_name);

        return NativeLibrary.GetExport(library, name);
    }

    /// <summary>
    /// Whether libpipewire's version, as <c>pw_get_library_version</c> gives it ("1.2.7"), is at least the one given.
    /// </summary>
    public static bool IsAtLeast(string? version, int major, int minor, int micro)
    {
        if (string.IsNullOrEmpty(version))
            return false;

        string[] parts = version.Split('.');
        int[] numbers = new int[3];

        for (int i = 0; i < 3 && i < parts.Length; i++)
        {
            // "1.2.7-1" or "0.3.49git": the leading digits.
            int digits = 0;

            while (digits < parts[i].Length && char.IsAsciiDigit(parts[i][digits]))
                digits++;

            if (digits == 0 || !int.TryParse(parts[i].AsSpan(0, digits), out numbers[i]))
                return false;
        }

        return (numbers[0], numbers[1], numbers[2]).CompareTo((major, minor, micro)) >= 0;
    }

    /// <summary>
    /// The EnumFormat object for interleaved 32-bit floats at <paramref name="sampleRate"/>: an SPA_TYPE_Object of type
    /// SPA_TYPE_OBJECT_Format, with the media type, subtype, sample format, rate, channels and their positions, each
    /// value padded to 8 bytes, as spa_format_audio_raw_build writes it.
    /// </summary>
    public static byte[] AudioFormat(int sampleRate, int channels)
    {
        if (channels is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "The engine mixes mono or stereo.");

        uint[] positions = channels == 1 ? new[] { spa_audio_channel_mono } : new[] { spa_audio_channel_fl, spa_audio_channel_fr };

        int arrayBody = 8 + 4 * positions.Length;
        int bodySize = 8 + 5 * 24 + 16 + pad(arrayBody);
        byte[] pod = new byte[8 + bodySize];
        var writer = new PodWriter(pod);

        writer.Word((uint)bodySize);
        writer.Word(spa_type_object);
        writer.Word(spa_type_object_format);
        writer.Word(spa_param_enum_format);

        writer.Property(spa_format_media_type, spa_type_id, spa_media_type_audio);
        writer.Property(spa_format_media_subtype, spa_type_id, spa_media_subtype_raw);
        writer.Property(spa_format_audio_format, spa_type_id, spa_audio_format_f32_le);
        writer.Property(spa_format_audio_rate, spa_type_int, (uint)sampleRate);
        writer.Property(spa_format_audio_channels, spa_type_int, (uint)channels);

        // The positions: an array pod whose child is an Id pod, then the ids, padded to 8.
        writer.Word(spa_format_audio_position);
        writer.Word(0);
        writer.Word((uint)arrayBody);
        writer.Word(spa_type_array);
        writer.Word(4);
        writer.Word(spa_type_id);

        foreach (uint position in positions)
            writer.Word(position);

        return pod;
    }

    private static int pad(int size) => (size + 7) & ~7;

    /// <summary>
    /// Writes an SPA pod's 32-bit words in the machine's order, which is little-endian on every target libpipewire
    /// plays on here.
    /// </summary>
    private ref struct PodWriter
    {
        private readonly Span<byte> pod;
        private int offset;

        public PodWriter(Span<byte> pod)
        {
            this.pod = pod;
            offset = 0;
        }

        public void Word(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(pod.Slice(offset, 4), value);
            offset += 4;
        }

        /// <summary>
        /// A property whose value is one 32-bit Id or Int, padded to 8 bytes: key, flags, the value's pod, the value.
        /// </summary>
        public void Property(uint key, uint type, uint value)
        {
            Word(key);
            Word(0);
            Word(4);
            Word(type);
            Word(value);
            Word(0);
        }
    }
}
