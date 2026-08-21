// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Audio.SdlEngine;

/// <summary>
/// Safe managed wrapper over libsakura-audio that owns the native engine's lifetime and turns its
/// handles, pointers and result codes into ordinary C#.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class SakuraAudioEngine : IDisposable
{
    /// <summary>
    /// Whether the native library is present and matches this build's ABI. When false the SDL backend
    /// mixes in managed code instead, which is a degraded backend rather than a broken one.
    /// </summary>
    public static bool IsAvailable => SakuraAudioNative.IsAvailable;

    private nint handle;

    /// <summary>
    /// The engine pointer, for use as the userdata of <see cref="StreamCallback"/>.
    /// </summary>
    public nint Handle => handle;

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>
    /// Frames the engine mixes at a time. Not the device buffer — SDL asks for whatever its buffer
    /// needs and this is how finely that request is chopped up.
    /// </summary>
    public int MixBlockFrames { get; }

    /// <summary>
    /// The mixer every other node hangs off.
    /// </summary>
    public uint Root { get; }

    private bool isDisposed;

    private SakuraAudioEngine(nint handle, SakuraAudioConfig config)
    {
        this.handle = handle;

        SampleRate = config.SampleRate;
        Channels = config.Channels;
        MixBlockFrames = config.MixBlockFrames;
        Root = SakuraAudioNative.sakura_audio_engine_root(handle);
    }

    /// <summary>
    /// Creates the engine, or returns null when the native library is unavailable or refused the
    /// configuration. A null return is a supported outcome, not an error.
    /// </summary>
    /// <param name="sampleRate">Output rate in Hz. Every buffer handed to the engine is already at it.</param>
    /// <param name="channels">Output channel count.</param>
    /// <param name="mixBlockFrames">Mix granularity, or 0 for the library's default.</param>
    /// <param name="maxNodes">Voice and mixer budget, preallocated, or 0 for the library's default.</param>
    public static unsafe SakuraAudioEngine? Create(int sampleRate, int channels, int mixBlockFrames = 0, int maxNodes = 0)
    {
        if (!IsAvailable)
            return null;

        SakuraAudioConfig config;
        SakuraAudioNative.sakura_audio_config_defaults(&config);

        config.SampleRate = sampleRate;
        config.Channels = channels;

        if (mixBlockFrames > 0)
            config.MixBlockFrames = mixBlockFrames;

        if (maxNodes > 0)
        {
            config.MaxNodes = maxNodes;
            config.MaxBuffers = maxNodes;
        }

        nint handle = SakuraAudioNative.sakura_audio_engine_create(&config);

        if (handle == nint.Zero)
        {
            Logger.Error($"libsakura-audio refused a {sampleRate}Hz {channels}ch engine; falling back to the managed mixer.");
            return null;
        }

        return new SakuraAudioEngine(handle, config);
    }

    /// <summary>
    /// The <c>SDL_AudioStreamCallback</c>-compatible function pointer to open the device with,
    /// passing <see cref="Handle"/> as its userdata.
    /// </summary>
    public static nint StreamCallback => SakuraAudioNative.sakura_audio_get_stream_callback();

    /// <summary>
    /// Hands the library the address of <c>SDL_PutAudioStreamData</c>, the only thing it needs from
    /// SDL, and the one call it makes from inside the device callback.
    /// </summary>
    /// <remarks>
    /// Resolved by export lookup rather than linked, so the native library needs no SDL3 SDK per
    /// target and cannot end up bound to a different SDL3 than this process loaded. Returns false if
    /// the export could not be found, in which case the native engine has no way to reach the device
    /// and the caller must not use it.
    /// </remarks>
    public static bool TrySetSdlPut()
    {
        if (!IsAvailable)
            return false;

        if (!tryResolveSdlExport("SDL_PutAudioStreamData", out nint address))
        {
            Logger.Error("Could not resolve SDL_PutAudioStreamData; the native mix engine cannot reach the device.");
            return false;
        }

        SakuraAudioNative.sakura_audio_set_sdl_put(address);
        return true;
    }

    private static bool tryResolveSdlExport(string name, out nint address)
    {
        address = nint.Zero;

        // The same set of names the SDL3 bindings themselves are resolved under, in the same order,
        // so this finds whichever one the platform actually loaded.
        foreach (string library in new[] { "SDL3", "libSDL3.so.0", "libSDL3.dylib", "SDL3.dll" })
        {
            if (!NativeLibrary.TryLoad(library, typeof(SDL.SDL3).Assembly, null, out nint loaded))
                continue;

            if (NativeLibrary.TryGetExport(loaded, name, out address))
                return true;
        }

        return false;
    }

    #region Graph

    /// <summary>
    /// Creates a mixer node: sums its children, then applies its own gain, filter and metering.
    /// </summary>
    /// <returns>The handle, or 0 when the preallocated pool is exhausted.</returns>
    public uint CreateMixer() => isDisposed ? 0 : SakuraAudioNative.sakura_audio_create_mixer(handle);

    /// <summary>
    /// Creates a voice: one playing channel.
    /// </summary>
    /// <returns>The handle, or 0 when the preallocated pool is exhausted.</returns>
    public uint CreateVoice() => isDisposed ? 0 : SakuraAudioNative.sakura_audio_create_voice(handle);

    /// <summary>
    /// Retires a node. Its slot and memory come back on the next <see cref="Maintain"/>.
    /// </summary>
    public void DestroyNode(uint node)
    {
        if (!isDisposed && node != 0)
            SakuraAudioNative.sakura_audio_destroy_node(handle, node);
    }

    public bool AddChild(uint parent, uint child) =>
        !isDisposed && SakuraAudioNative.sakura_audio_add_child(handle, parent, child) == SakuraAudioNative.OK;

    public bool RemoveChild(uint parent, uint child) =>
        !isDisposed && SakuraAudioNative.sakura_audio_remove_child(handle, parent, child) == SakuraAudioNative.OK;

    #endregion

    #region Parameters and transport

    public void SetGain(uint node, float volume, float panLeft, float panRight)
    {
        if (!isDisposed)
            SakuraAudioNative.sakura_audio_node_set_gain(handle, node, volume, panLeft, panRight);
    }

    public void SetRate(uint node, double ratio)
    {
        if (!isDisposed)
            SakuraAudioNative.sakura_audio_node_set_rate(handle, node, ratio);
    }

    public void SetLoop(uint node, bool looping, long restartFrame)
    {
        if (!isDisposed)
            SakuraAudioNative.sakura_audio_node_set_loop(handle, node, looping ? 1 : 0, restartFrame);
    }

    /// <summary>
    /// Publishes normalised biquad coefficients for this node's low-pass insert.
    /// </summary>
    public void SetFilter(uint node, bool enabled, float b0, float b1, float b2, float a1, float a2)
    {
        if (!isDisposed)
            SakuraAudioNative.sakura_audio_node_set_filter(handle, node, enabled ? 1 : 0, b0, b1, b2, a1, a2);
    }

    public void Play(uint node)
    {
        if (!isDisposed)
            SakuraAudioNative.sakura_audio_node_play(handle, node);
    }

    public void Pause(uint node)
    {
        if (!isDisposed)
            SakuraAudioNative.sakura_audio_node_pause(handle, node);
    }

    public void Stop(uint node)
    {
        if (!isDisposed)
            SakuraAudioNative.sakura_audio_node_stop(handle, node);
    }

    /// <summary>
    /// Moves a static voice's cursor, and in every case clears the interpolation window, the filter's
    /// delay line and the metering capture. For a streaming voice the decoder seek and the ring flush
    /// are the caller's, and belong after this.
    /// </summary>
    public void Seek(uint node, long frame)
    {
        if (!isDisposed)
            SakuraAudioNative.sakura_audio_node_seek(handle, node, frame);
    }

    #endregion

    #region Sources

    /// <summary>
    /// Copies fully decoded PCM into a buffer every voice playing that sample shares.
    /// </summary>
    /// <returns>The handle, or 0 when the pool is exhausted or the copy failed.</returns>
    public unsafe uint CreateBuffer(ReadOnlySpan<float> interleaved)
    {
        if (isDisposed || interleaved.IsEmpty)
            return 0;

        long frames = interleaved.Length / Channels;

        if (frames <= 0)
            return 0;

        fixed (float* pointer = interleaved)
            return SakuraAudioNative.sakura_audio_buffer_create(handle, pointer, frames);
    }

    /// <summary>
    /// Drops this caller's reference. The PCM survives until the last voice using it is gone.
    /// </summary>
    public void ReleaseBuffer(uint buffer)
    {
        if (!isDisposed && buffer != 0)
            SakuraAudioNative.sakura_audio_buffer_release(handle, buffer);
    }

    public bool SetVoiceBuffer(uint voice, uint buffer) =>
        !isDisposed && SakuraAudioNative.sakura_audio_voice_set_buffer(handle, voice, buffer) == SakuraAudioNative.OK;

    /// <summary>
    /// Gives a voice a ring buffer for a decode thread to fill. Its size is decode-ahead depth, which
    /// has nothing to do with output latency.
    /// </summary>
    public bool SetVoiceStream(uint voice, int capacityFrames) =>
        !isDisposed && SakuraAudioNative.sakura_audio_voice_set_stream(handle, voice, capacityFrames) == SakuraAudioNative.OK;

    #endregion

    #region Streaming, writer side

    /// <summary>
    /// Appends decoded frames. A short return means the ring filled and the caller must retain the
    /// remainder — dropping it would put a gap in the audio.
    /// </summary>
    public unsafe int StreamWrite(uint voice, ReadOnlySpan<float> interleaved)
    {
        if (isDisposed || interleaved.IsEmpty)
            return 0;

        fixed (float* pointer = interleaved)
        {
            int written = SakuraAudioNative.sakura_audio_stream_write(handle, voice, pointer, interleaved.Length / Channels);
            return Math.Max(written, 0);
        }
    }

    public int StreamSpace(uint voice) => isDisposed ? 0 : Math.Max(SakuraAudioNative.sakura_audio_stream_space(handle, voice), 0);

    public int StreamBuffered(uint voice) => isDisposed ? 0 : Math.Max(SakuraAudioNative.sakura_audio_stream_buffered(handle, voice), 0);

    /// <summary>
    /// Tells the engine the decoder has no more audio. A streaming voice is only ended once it is
    /// drained <em>and</em> its ring is empty.
    /// </summary>
    public void StreamSetDrained(uint voice, bool drained)
    {
        if (!isDisposed)
            SakuraAudioNative.sakura_audio_stream_set_drained(handle, voice, drained ? 1 : 0);
    }

    /// <summary>
    /// Posts a discard of everything buffered, for a seek.
    /// </summary>
    public void StreamFlushBegin(uint voice)
    {
        if (!isDisposed)
            SakuraAudioNative.sakura_audio_stream_flush_begin(handle, voice);
    }

    /// <summary>
    /// Whether a posted discard is still waiting on the audio thread. The writer must not write until
    /// this is false, or the new position's audio is thrown away along with the old.
    /// </summary>
    public bool StreamFlushPending(uint voice) =>
        !isDisposed && SakuraAudioNative.sakura_audio_stream_flush_pending(handle, voice) != 0;

    #endregion

    #region Read-back

    public unsafe bool TryGetState(uint node, out SakuraAudioNodeState state)
    {
        state = default;

        if (isDisposed)
            return false;

        fixed (SakuraAudioNodeState* pointer = &state)
            return SakuraAudioNative.sakura_audio_node_get_state(handle, node, pointer) == SakuraAudioNative.OK;
    }

    /// <summary>
    /// Transforms this node's most recent window of mixed output into magnitudes, on the calling
    /// thread.
    /// </summary>
    /// <returns>Bins written, or 0 when no audio has passed through this node yet.</returns>
    public unsafe int ReadSpectrum(uint node, Span<float> bins)
    {
        if (isDisposed || bins.IsEmpty)
            return 0;

        fixed (float* pointer = bins)
        {
            int written = SakuraAudioNative.sakura_audio_node_read_spectrum(handle, node, pointer, bins.Length);
            return Math.Max(written, 0);
        }
    }

    public unsafe SakuraAudioStats GetStats()
    {
        SakuraAudioStats stats = default;

        if (isDisposed)
            return stats;

        SakuraAudioNative.sakura_audio_engine_get_stats(handle, &stats);
        return stats;
    }

    #endregion

    /// <summary>
    /// Reclaims nodes and buffers the audio thread has finished with. The only place the native side
    /// frees anything, so it has to be pumped once a frame.
    /// </summary>
    public void Maintain()
    {
        if (!isDisposed)
            SakuraAudioNative.sakura_audio_engine_maintain(handle);
    }

    /// <summary>
    /// Mixes one block exactly as the device callback would. For tests, parity checks against the
    /// managed mixer, and offline rendering — never while a device is running, which would race the
    /// callback.
    /// </summary>
    /// <returns>Frames written.</returns>
    public unsafe int Mix(Span<float> destination)
    {
        if (isDisposed || destination.IsEmpty)
            return 0;

        fixed (float* pointer = destination)
            return Math.Max(SakuraAudioNative.sakura_audio_engine_mix(handle, pointer, destination.Length / Channels), 0);
    }

    /// <summary>
    /// Frees the engine. The caller must have stopped the device first: the native side does not
    /// synchronise with a callback that is already running.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;

        SakuraAudioNative.sakura_audio_engine_destroy(handle);
        handle = nint.Zero;
    }
}
