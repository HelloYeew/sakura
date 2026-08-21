// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// The engine's fixed-size allocation budget and output format, passed to
/// <see cref="SakuraAudioNative.sakura_audio_engine_create"/>. Layout must match the native
/// <c>SakuraAudioConfig</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SakuraAudioConfig
{
    public int SampleRate;
    public int Channels;
    public int MaxNodes;
    public int MaxBuffers;
    public int MaxCommands;
    public int MixBlockFrames;
}

/// <summary>
/// Engine-wide counters, filled by <see cref="SakuraAudioNative.sakura_audio_engine_get_stats"/>.
/// Layout must match the native <c>SakuraAudioStats</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SakuraAudioStats
{
    public long Callbacks;
    public long FramesMixed;
    public long Starvations;
    public long PutFailures;
    public long CommandsDropped;
    public long CallbackMicroseconds;
    public int ActiveVoices;
}

/// <summary>
/// One node's published state, filled by <see cref="SakuraAudioNative.sakura_audio_node_get_state"/>.
/// Layout must match the native <c>SakuraAudioNodeState</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SakuraAudioNodeState
{
    /// <summary>
    /// The source cursor in input frames: absolute for a static buffer, and frames since the last
    /// seek or flush for a stream, whose ring buffer has no idea where in the song it sits.
    /// </summary>
    public long SourceFrames;

    /// <summary>
    /// Bumped once each time the source runs out. Watched rather than subscribed to, because the
    /// audio thread cannot raise a managed event.
    /// </summary>
    public long EndEpoch;

    public int Running;
    public int Ended;

    public float AmplitudeLeft;
    public float AmplitudeRight;
}

/// <summary>
/// P/Invoke bindings for <c>libsakura-audio</c>, the real-time mix engine behind the SDL audio
/// backend (see <c>native/sakura-audio/sakura_audio.h</c>).
/// </summary>
internal static class SakuraAudioNative
{
    private const string lib_name = "libsakura-audio";

    /// <summary>
    /// The ABI this assembly was built against. Must match <c>SAKURA_AUDIO_ABI_VERSION</c> in
    /// <c>sakura_audio.h</c>, a shipped library that disagrees is refused rather than trusted to have
    /// the same struct layouts.
    /// </summary>
    public const int ABI_VERSION = 1;

    /// <summary>
    /// Transform size and bin count of the native spectrum, fixed to line up with
    /// <see cref="ChannelAmplitudes.AMPLITUDES_SIZE"/>.
    /// </summary>
    public const int FFT_SIZE = 512;

    public const int BIN_COUNT = 256;

    public const int OK = 0;
    public const int ERROR = -1;
    public const int INVALID = -2;
    public const int FULL = -3;
    public const int TIMEOUT = -4;

    private static bool? available;

    /// <summary>
    /// Whether the native library loaded and reports the ABI this assembly expects.
    /// </summary>
    /// <remarks>
    /// Probed once, by calling the cheapest entry point there is. Anything thrown by the loader is a
    /// missing or unloadable library, which is a supported state rather than an error.
    /// </remarks>
    public static bool IsAvailable
    {
        get
        {
            if (available.HasValue)
                return available.Value;

            try
            {
                SetupLibraryResolvers();

                int version = sakura_audio_abi_version();

                if (version != ABI_VERSION)
                {
                    Logger.Warning($"libsakura-audio reports ABI {version}, but this build expects {ABI_VERSION}. " +
                                   "The SDL audio backend will use its managed mixer.");
                    available = false;
                }
                else
                {
                    available = true;
                }
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                Logger.Verbose($"libsakura-audio is not available ({e.GetType().Name}); the SDL audio backend will use its managed mixer.");
                available = false;
            }

            return available.Value;
        }
    }

    private static bool resolversInstalled;

    /// <summary>
    /// Installs the iOS DLL import resolver (the library is embedded as
    /// <c>sakura-audio.framework</c> under <c>@rpath</c>). No-op on other platforms, where the
    /// dylib/so/dll is found by name. Call once before any other entry point.
    /// </summary>
    public static void SetupLibraryResolvers()
    {
        if (resolversInstalled)
            return;

        resolversInstalled = true;

        if (OperatingSystem.IsIOS())
        {
            NativeLibrary.SetDllImportResolver(
                typeof(SakuraAudioNative).Assembly,
                (_, assembly, path) =>
                    NativeLibrary.Load("@rpath/sakura-audio.framework/sakura-audio", assembly, path));
        }
    }

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_abi_version();

    #region SDL handshake

    /// <summary>
    /// Hands the library <c>SDL_PutAudioStreamData</c>, the only thing it wants from SDL. Injected
    /// rather than linked so the native library needs no SDL3 SDK per target, and so the SDL3 it was
    /// built against cannot differ from the one this process loaded.
    /// </summary>
    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_audio_set_sdl_put(nint function);

    /// <summary>
    /// The <c>SDL_AudioStreamCallback</c>-compatible function pointer to hand to
    /// <c>SDL_OpenAudioDeviceStream</c>, with the engine pointer as its userdata.
    /// </summary>
    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint sakura_audio_get_stream_callback();

    #endregion

    #region Engine

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe void sakura_audio_config_defaults(SakuraAudioConfig* config);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe nint sakura_audio_engine_create(SakuraAudioConfig* config);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_audio_engine_destroy(nint engine);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint sakura_audio_engine_root(nint engine);

    /// <summary>
    /// Reclaims nodes and buffers the audio thread has finished with. The only place the native side
    /// frees anything, so this has to be pumped — once a frame, from the update thread.
    /// </summary>
    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern void sakura_audio_engine_maintain(nint engine);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe int sakura_audio_engine_get_stats(nint engine, SakuraAudioStats* stats);

    /// <summary>
    /// Mixes one block exactly as the device callback would, for tests and offline rendering. Racing
    /// this against a running device is a caller bug.
    /// </summary>
    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe int sakura_audio_engine_mix(nint engine, float* destination, int frames);

    #endregion

    #region Graph

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint sakura_audio_create_mixer(nint engine);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint sakura_audio_create_voice(nint engine);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_destroy_node(nint engine, uint node);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_add_child(nint engine, uint parent, uint child);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_remove_child(nint engine, uint parent, uint child);

    #endregion

    #region Parameters

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_node_set_gain(nint engine, uint node, float volume, float panLeft, float panRight);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_node_set_rate(nint engine, uint node, double ratio);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_node_set_loop(nint engine, uint node, int looping, long restartFrame);

    /// <summary>
    /// Publishes normalised biquad coefficients. Computed managed-side by
    /// <see cref="SDLLowPassFilter"/> so the cutoff maths has one home and one set of tests.
    /// </summary>
    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_node_set_filter(nint engine, uint node, int enabled, float b0, float b1, float b2, float a1, float a2);

    #endregion

    #region Transport

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_node_play(nint engine, uint node);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_node_pause(nint engine, uint node);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_node_stop(nint engine, uint node);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_node_seek(nint engine, uint node, long frame);

    #endregion

    #region Published state

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe int sakura_audio_node_get_state(nint engine, uint node, SakuraAudioNodeState* state);

    /// <summary>
    /// Transforms the node's most recent window of mixed output. Runs the FFT on the calling thread
    /// by design, it must never end up on the audio callback.
    /// </summary>
    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe int sakura_audio_node_read_spectrum(nint engine, uint node, float* bins, int binCount);

    #endregion

    #region Sources

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe uint sakura_audio_buffer_create(nint engine, float* interleaved, long frames);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_buffer_release(nint engine, uint buffer);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_voice_set_buffer(nint engine, uint voice, uint buffer);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_voice_set_stream(nint engine, uint voice, int capacityFrames);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe int sakura_audio_stream_write(nint engine, uint voice, float* interleaved, int frames);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_stream_space(nint engine, uint voice);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_stream_buffered(nint engine, uint voice);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_stream_set_drained(nint engine, uint voice, int drained);

    /// <summary>
    /// Posts a discard of everything buffered, for a seek. The writer must then wait for
    /// <see cref="sakura_audio_stream_flush_pending"/> to go false before writing audio from the new
    /// position, or the new audio is thrown away with the old.
    /// </summary>
    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_stream_flush_begin(nint engine, uint voice);

    [DllImport(lib_name, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sakura_audio_stream_flush_pending(nint engine, uint voice);

    #endregion
}
