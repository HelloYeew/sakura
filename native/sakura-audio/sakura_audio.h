// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#ifndef SAKURA_AUDIO_H
#define SAKURA_AUDIO_H

#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// libsakura-audio: the real-time mix engine behind the framework's SDL audio backend.
//
// The managed side (Sakura.Framework/Audio/SdlEngine) decodes, resamples to the device format, and
// fills this library's ring buffers from an ordinary background thread. This library owns everything
// that runs on the audio device's callback: the mixer graph, per-voice rate conversion, gain and
// pan, the low-pass inserts, peak metering and the drain of those ring buffers. Nothing on that path
// allocates, takes a lock, or calls back into managed code, so a GC pause cannot underrun the device
// no matter how small its buffer is.
//
// It touches no platform API at all. SDL is reached through one injected function pointer (see
// sakura_audio_set_sdl_put), the only link dependency is libm, and everything else is arithmetic on
// buffers -- so this builds for every RID the framework ships to, and must keep doing so.
//
// Threading, which is the whole design:
//
//   * one *control* thread calls the create/destroy/parameter/transport entry points. All of them
//     are non-blocking; the ones that change the graph or the transport post a command that the
//     audio callback picks up. Calls from more than one thread are tolerated but not free -- see
//     the note on the command queue in sakura_audio.c.
//   * any number of *writer* threads call sakura_audio_stream_* , at most one per voice. That is the
//     decode thread, and it is the only producer for that voice's ring.
//   * the *audio* thread is SDL's, and only ever enters through the callback returned by
//     sakura_audio_get_stream_callback.
//   * reads of published state -- sakura_audio_node_get_state, sakura_audio_node_read_spectrum,
//     sakura_audio_engine_get_stats -- are safe from any thread and never block the audio thread.
//     The FFT behind read_spectrum runs on the calling thread by design; it must never be moved
//     onto the callback.

#define SAKURA_AUDIO_ABI_VERSION 1

// Transform size and the number of magnitude bins it produces. Fixed rather than configurable
// because they have to line up with ChannelAmplitudes.AMPLITUDES_SIZE on the managed side, which is
// itself matched to BASS's FFT512.
#define SAKURA_AUDIO_FFT_SIZE 512
#define SAKURA_AUDIO_BINS 256

// Interleaved channel counts this engine will mix. The framework outputs stereo; the ceiling exists
// so that per-voice DSP state can be a fixed-size array rather than an allocation.
#define SAKURA_AUDIO_MAX_CHANNELS 8

// How deep the mixer graph may nest. The framework uses two levels (a master mixer holding voices);
// the cap is what keeps the recursive mix from being an unbounded loop on the audio thread.
#define SAKURA_AUDIO_MAX_DEPTH 8

typedef struct SakuraAudioEngine SakuraAudioEngine;

// Handles rather than pointers: every node and buffer lives in a preallocated pool, and a handle
// carries the slot's generation alongside its index so that a stale handle is rejected instead of
// landing on whatever now occupies the slot. Zero is never valid.
typedef uint32_t SakuraAudioHandle;

#define SAKURA_AUDIO_INVALID_HANDLE ((SakuraAudioHandle)0)

// Result codes. Every int-returning entry point returns SAKURA_AUDIO_OK or one of these, except
// where documented as returning a count.
#define SAKURA_AUDIO_OK 0
#define SAKURA_AUDIO_ERROR (-1)
#define SAKURA_AUDIO_INVALID (-2)  // null engine, unknown handle, or an out-of-range argument
#define SAKURA_AUDIO_FULL (-3)     // a preallocated pool or the command queue is exhausted
#define SAKURA_AUDIO_TIMEOUT (-4)  // the audio thread did not acknowledge in the time allowed

// The ABI version this library was built with. Managed checks it before anything else: a mismatch
// means the shipped native library and the assembly disagree about these structs.
int sakura_audio_abi_version(void);

// --- SDL injection -------------------------------------------------------------------------------

// SDL_PutAudioStreamData, as the only thing this library needs from SDL. Injecting it rather than
// linking SDL3 is what keeps the CI story to "compile one C file per RID": no SDL3 headers or import
// libraries per target, and no way for the SDL3 this linked against to differ from the SDL3 the
// managed side loaded. Any future need for another SDL call should be injected the same way.
// Returns bool, not int, and that matters: SDL3's bool is one byte, and a C caller reading a full
// int back from it would be reading whatever the upper bits of the return register happened to hold.
typedef bool (*sakura_sdl_put_fn)(void *stream, const void *buffer, int lengthBytes);

// Installs the put function. Must be called before the first callback; it is global rather than
// per-engine because there is exactly one SDL3 in the process.
void sakura_audio_set_sdl_put(sakura_sdl_put_fn function);

// The SDL_AudioStreamCallback-compatible entry point to hand to SDL_OpenAudioDeviceStream, with an
// engine pointer as its userdata. Returned as void* so that no SDL type appears in this header.
void *sakura_audio_get_stream_callback(void);

// --- Engine --------------------------------------------------------------------------------------

typedef struct SakuraAudioConfig
{
    int sampleRate;     // output rate in Hz; every buffer handed to this library is already at it
    int channels;       // 1..SAKURA_AUDIO_MAX_CHANNELS
    int maxNodes;       // voices and mixers together, preallocated at create time
    int maxBuffers;     // shared PCM buffers (one per loaded sample), preallocated
    int maxCommands;    // command queue slots; rounded up to a power of two
    int mixBlockFrames; // internal mix granularity, independent of the device buffer size
} SakuraAudioConfig;

// Fills in the defaults: 44100 Hz stereo, 512 nodes, 512 buffers, 4096 commands, 128-frame blocks.
void sakura_audio_config_defaults(SakuraAudioConfig *config);

// Allocates everything the engine will ever use. Returns NULL if the config is invalid or an
// allocation failed.
SakuraAudioEngine *sakura_audio_engine_create(const SakuraAudioConfig *config);

// Frees the engine. The caller must have stopped the device first: this does not synchronise with a
// callback that is already running.
void sakura_audio_engine_destroy(SakuraAudioEngine *engine);

// The mixer every other node hangs off. Created with the engine and never destroyed.
SakuraAudioHandle sakura_audio_engine_root(SakuraAudioEngine *engine);

// Reclaims nodes and buffers the audio thread has finished with, freeing their ring buffers and PCM
// data. Call from the control thread, once a frame; nothing else frees anything.
void sakura_audio_engine_maintain(SakuraAudioEngine *engine);

typedef struct SakuraAudioStats
{
    int64_t callbacks;            // device callbacks served
    int64_t framesMixed;          // output frames produced
    int64_t starvations;          // blocks where a running voice had less audio than was asked for
    int64_t putFailures;          // sakura_sdl_put_fn rejected a block
    int64_t commandsDropped;      // control commands lost to a full queue; should always be zero
    int64_t callbackMicroseconds; // duration of the most recent callback
    int32_t activeVoices;         // voices that produced audio in the most recent block
} SakuraAudioStats;

int sakura_audio_engine_get_stats(SakuraAudioEngine *engine, SakuraAudioStats *stats);

// Mixes one block of `frames` output frames into `destination` (interleaved, config.channels wide),
// exactly as the device callback would, and returns the frames written. For tests, for offline
// rendering, and for parity checks against the managed mixer -- not for use while a device is
// running, which would race the callback.
int sakura_audio_engine_mix(SakuraAudioEngine *engine, float *destination, int frames);

// --- Graph ---------------------------------------------------------------------------------------

// A mixer node: sums its children, then applies its own gain, filter and metering. This is what
// IAudioMixer maps onto, and applying its own inserts to the sum is what makes a mixer's volume and
// filter behave like a channel's rather than degrading to bookkeeping.
SakuraAudioHandle sakura_audio_create_mixer(SakuraAudioEngine *engine);

// A voice: one playing channel, pulling from a static buffer or a streaming ring.
SakuraAudioHandle sakura_audio_create_voice(SakuraAudioEngine *engine);

// Retires a node. The audio thread unlinks it from the graph and drops its source; the slot and its
// memory come back on the next sakura_audio_engine_maintain.
int sakura_audio_destroy_node(SakuraAudioEngine *engine, SakuraAudioHandle node);

// Routes `child` into `parent`. A node has at most one parent; adding it to a second moves it.
int sakura_audio_add_child(SakuraAudioEngine *engine, SakuraAudioHandle parent, SakuraAudioHandle child);
int sakura_audio_remove_child(SakuraAudioEngine *engine, SakuraAudioHandle parent, SakuraAudioHandle child);

// --- Parameters ----------------------------------------------------------------------------------
//
// These take effect on the next block without going through the command queue: each is a single
// atomic store that the audio thread reads once per block.

// Linear volume, and the per-side pan gains. Split rather than passing a balance so that the pan law
// stays on the managed side next to the BASS backend's, where it can be compared.
int sakura_audio_node_set_gain(SakuraAudioEngine *engine, SakuraAudioHandle node, float volume, float panLeft, float panRight);

// Input frames consumed per output frame. 1.0 is unity and is bit-exact, not merely close.
int sakura_audio_node_set_rate(SakuraAudioEngine *engine, SakuraAudioHandle node, double ratio);

int sakura_audio_node_set_loop(SakuraAudioEngine *engine, SakuraAudioHandle node, int looping, int64_t restartFrame);

// Normalised biquad coefficients, transposed direct form II. Computed on the managed side so that
// the cutoff-to-coefficient maths has one home and one set of tests; this library only applies them.
int sakura_audio_node_set_filter(SakuraAudioEngine *engine, SakuraAudioHandle node, int enabled,
                                 float b0, float b1, float b2, float a1, float a2);

// --- Transport -----------------------------------------------------------------------------------
//
// Posted as commands, so that a play and a seek issued in that order are applied in that order.

int sakura_audio_node_play(SakuraAudioEngine *engine, SakuraAudioHandle node);

// Stops producing audio and leaves the cursor where it is.
int sakura_audio_node_pause(SakuraAudioEngine *engine, SakuraAudioHandle node);

// Stops and rewinds, matching the BASS backend where Stop rewinds and Pause does not.
int sakura_audio_node_stop(SakuraAudioEngine *engine, SakuraAudioHandle node);

// Moves a static voice's cursor, and in every case clears the interpolation window, the filter's
// delay line and the metering capture -- carrying those across a discontinuity smears audio from
// the old position into the new one. For a streaming voice the caller is responsible for seeking its
// decoder and flushing its ring; this only resets what lives on this side.
int sakura_audio_node_seek(SakuraAudioEngine *engine, SakuraAudioHandle node, int64_t frame);

// --- Published state -----------------------------------------------------------------------------

typedef struct SakuraAudioNodeState
{
    // Where the source cursor is, in input frames.
    //
    // For a static buffer that is absolute: the position in the sample. For a stream it is frames
    // pulled since the last seek or flush, because a ring buffer has no idea where in the song it
    // sits -- the caller adds the base its seek established. Both are the position handed to the
    // mixer, so both still run ahead of what is audible by whatever the device has buffered.
    int64_t sourceFrames;

    // Bumped once each time the source runs out. The managed side watches it to fire OnEnd, and to
    // re-seek the decoder behind a looping streaming voice.
    int64_t endEpoch;

    int32_t running;
    int32_t ended;

    float amplitudeLeft;
    float amplitudeRight;
} SakuraAudioNodeState;

int sakura_audio_node_get_state(SakuraAudioEngine *engine, SakuraAudioHandle node, SakuraAudioNodeState *state);

// Computes the magnitude spectrum of the most recently published window at this node and writes
// `binCount` (at most SAKURA_AUDIO_BINS) magnitudes. Returns the bins written, or 0 when nothing has
// passed through this node yet. Runs the transform on the calling thread -- never call it from the
// audio callback.
int sakura_audio_node_read_spectrum(SakuraAudioEngine *engine, SakuraAudioHandle node, float *bins, int binCount);

// --- Sources -------------------------------------------------------------------------------------

// Copies fully decoded interleaved PCM into a buffer every voice playing that sample can share.
// This is the sample path: decode once at load, and starting a hitsound becomes a lock-free command
// with no decode-ahead and no GC exposure at all.
SakuraAudioHandle sakura_audio_buffer_create(SakuraAudioEngine *engine, const float *interleaved, int64_t frames);

// Drops the caller's reference. The PCM survives until the last voice using it is gone.
int sakura_audio_buffer_release(SakuraAudioEngine *engine, SakuraAudioHandle buffer);

int sakura_audio_voice_set_buffer(SakuraAudioEngine *engine, SakuraAudioHandle voice, SakuraAudioHandle buffer);

// Gives a voice a ring buffer of `capacityFrames` for a writer thread to fill. Sized by the caller
// from how far ahead it wants to decode, which has nothing to do with output latency.
int sakura_audio_voice_set_stream(SakuraAudioEngine *engine, SakuraAudioHandle voice, int capacityFrames);

// --- Streaming, writer side ----------------------------------------------------------------------
//
// One writer thread per voice. Appends are wait-free against the audio thread.

// Returns frames written, which is short of `frames` when the ring filled: the caller must retain
// the remainder rather than dropping it, or there will be a gap in the audio.
int sakura_audio_stream_write(SakuraAudioEngine *engine, SakuraAudioHandle voice, const float *interleaved, int frames);

int sakura_audio_stream_space(SakuraAudioEngine *engine, SakuraAudioHandle voice);
int sakura_audio_stream_buffered(SakuraAudioEngine *engine, SakuraAudioHandle voice);

// Tells the engine the decoder has no more audio. A streaming voice is only ended once it is drained
// *and* its ring is empty.
int sakura_audio_stream_set_drained(SakuraAudioEngine *engine, SakuraAudioHandle voice, int drained);

// Asks the engine to discard everything buffered, for a seek, and clears the drained flag.
//
// A discard cannot be done by the writer: it does not know where the audio thread's read cursor is,
// and moving it from underneath a callback that is mid-copy is how a seek turns into a click. So the
// audio thread performs it, and this call only posts the request -- after which the writer must wait
// for sakura_audio_stream_flush_pending to go false before writing audio from the new position, or
// the new audio will be discarded along with the old.
//
// The wait is the caller's to do, and deliberately so: it is the one place this library would
// otherwise need a sleep primitive, and there isn't a portable one. The managed side already has a
// decode thread that is allowed to block.
int sakura_audio_stream_flush_begin(SakuraAudioEngine *engine, SakuraAudioHandle voice);

// Non-zero while a posted flush has not yet been acknowledged by the audio thread. Rings are
// acknowledged whether or not their voice is playing, so this completes even for a paused voice --
// as long as the device is running at all.
int sakura_audio_stream_flush_pending(SakuraAudioEngine *engine, SakuraAudioHandle voice);

#ifdef __cplusplus
}
#endif

#endif // SAKURA_AUDIO_H
