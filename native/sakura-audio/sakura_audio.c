// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#include "sakura_audio.h"
#include "sakura_atomic.h"

#include <math.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

// One translation unit, on purpose: every RID builds this file and links libm, and there is no
// per-platform source list to get wrong. See README.md.

// ---------------------------------------------------------------------------------------------
// Handles
//
// A handle is a slot index plus the generation the slot was on when it was handed out, so a stale
// handle is rejected rather than landing on whatever now occupies the slot. Managed code holds these
// across garbage collections and across a channel's whole life, which is exactly where a bare index
// would eventually alias.

#define HANDLE_INDEX_BITS 20
#define HANDLE_INDEX_MASK ((1u << HANDLE_INDEX_BITS) - 1u)
#define HANDLE_GENERATION_MASK 0xFFFu

static SakuraAudioHandle handle_make(uint32_t index, uint32_t generation)
{
    return (SakuraAudioHandle)(((generation & HANDLE_GENERATION_MASK) << HANDLE_INDEX_BITS) | (index & HANDLE_INDEX_MASK));
}

static uint32_t handle_index(SakuraAudioHandle handle) { return (uint32_t)handle & HANDLE_INDEX_MASK; }
static uint32_t handle_generation(SakuraAudioHandle handle) { return ((uint32_t)handle >> HANDLE_INDEX_BITS) & HANDLE_GENERATION_MASK; }

// Slot lifecycle. RETIRED means the audio thread is finished with it and
// sakura_audio_engine_maintain may free what it owns; nothing else ever frees.
#define SLOT_FREE 0u
#define SLOT_LIVE 1u
#define SLOT_RETIRED 2u
#define SLOT_CLAIMED 3u // taken, still being initialised; never resolvable

#define NODE_MIXER 0
#define NODE_VOICE 1

#define SOURCE_NONE 0u
#define SOURCE_STATIC 1u
#define SOURCE_STREAM 2u

// ---------------------------------------------------------------------------------------------
// Metering constants, matched to the managed AmplitudeTap so both backends' visualisers behave the
// same. 64 frames folded into one peak-hold segment, 16 segments in the window: ~21 ms at 48 kHz,
// the same order as the fixed window BASS_ChannelGetLevel reports over. A peak computed per callback
// into an atomic instead would report zero whenever the reader lands between callbacks, which is the
// bug the managed tap already had and had fixed.

#define PEAK_SEGMENT_FRAMES 64
#define PEAK_SEGMENT_COUNT 16

// Windows of mono audio kept for the spectrum. Three, so the writer is always two windows away from
// the one a reader could be copying.
#define CAPTURE_SLOTS 3

// Resampler window: the frame either side of the one being produced, so four frames wide.
#define RESAMPLER_WINDOW_FRAMES 4

#define RATIO_MIN (1.0 / 64.0)
#define RATIO_MAX 64.0

// Commands drained per callback. Bounded, like every other loop on this thread.
#define COMMANDS_PER_CALLBACK 256

// ---------------------------------------------------------------------------------------------
// Ring buffer
//
// SPSC, one writer thread per voice, drained by the audio thread. Positions are monotonic 64-bit
// counters masked into the storage, so neither side ever has to reason about wraparound.
//
// The discard-on-seek protocol is the only interesting part. The writer cannot move the read cursor
// -- it does not know where the audio thread is, and moving it under a callback that is mid-copy is
// how a seek becomes a click -- so the writer posts a flush (a target position and an epoch bump)
// and the audio thread performs it, jumping its read cursor to the target. Until it has, the writer
// must not write: sakura_audio_stream_flush_pending is what it waits on.

typedef struct Ring
{
    float *data;
    uint64_t capacity; // floats, a power of two

    sakura_atomic_u64 writePosition; // writer-owned
    sakura_atomic_u64 readPosition;  // audio-thread-owned
    sakura_atomic_u64 flushTarget;
    sakura_atomic_u32 writeEpoch; // writer-owned
    sakura_atomic_u32 readEpoch;  // audio-thread-owned
    sakura_atomic_u32 drained;
} Ring;

static uint64_t ring_available(const Ring *ring)
{
    uint64_t write = sakura_atomic_load_u64(&ring->writePosition);
    uint64_t read = sakura_atomic_load_u64(&ring->readPosition);
    return write - read;
}

static int ring_flush_pending(const Ring *ring)
{
    return sakura_atomic_load_u32(&ring->writeEpoch) != sakura_atomic_load_u32(&ring->readEpoch);
}

// Audio thread: applies any posted discard. Run for every live streaming voice at the top of a
// callback, playing or not -- a paused voice's seek has to complete too.
static void ring_sync(Ring *ring)
{
    uint32_t writeEpoch = sakura_atomic_load_u32(&ring->writeEpoch);

    if (writeEpoch == sakura_atomic_load_u32(&ring->readEpoch))
        return;

    sakura_atomic_store_u64(&ring->readPosition, sakura_atomic_load_u64(&ring->flushTarget));
    sakura_atomic_store_u32(&ring->readEpoch, writeEpoch);
}

static int ring_write(Ring *ring, const float *source, int count)
{
    if (count <= 0 || ring_flush_pending(ring))
        return 0;

    uint64_t write = sakura_atomic_load_u64(&ring->writePosition);
    uint64_t read = sakura_atomic_load_u64(&ring->readPosition);
    uint64_t free = ring->capacity - (write - read);

    int writable = (uint64_t)count < free ? count : (int)free;

    if (writable <= 0)
        return 0;

    uint64_t offset = write & (ring->capacity - 1);
    uint64_t firstChunk = ring->capacity - offset;

    if (firstChunk > (uint64_t)writable)
        firstChunk = (uint64_t)writable;

    memcpy(ring->data + offset, source, (size_t)firstChunk * sizeof(float));

    if ((uint64_t)writable > firstChunk)
        memcpy(ring->data, source + firstChunk, (size_t)((uint64_t)writable - firstChunk) * sizeof(float));

    sakura_atomic_store_u64(&ring->writePosition, write + (uint64_t)writable);
    return writable;
}

static int ring_read(Ring *ring, float *destination, int count)
{
    if (count <= 0)
        return 0;

    uint64_t read = sakura_atomic_load_u64(&ring->readPosition);
    uint64_t available = sakura_atomic_load_u64(&ring->writePosition) - read;

    int readable = (uint64_t)count < available ? count : (int)available;

    if (readable <= 0)
        return 0;

    uint64_t offset = read & (ring->capacity - 1);
    uint64_t firstChunk = ring->capacity - offset;

    if (firstChunk > (uint64_t)readable)
        firstChunk = (uint64_t)readable;

    memcpy(destination, ring->data + offset, (size_t)firstChunk * sizeof(float));

    if ((uint64_t)readable > firstChunk)
        memcpy(destination + firstChunk, ring->data, (size_t)((uint64_t)readable - firstChunk) * sizeof(float));

    sakura_atomic_store_u64(&ring->readPosition, read + (uint64_t)readable);
    return readable;
}

// ---------------------------------------------------------------------------------------------
// Nodes

typedef struct Node
{
    sakura_atomic_u32 slotState;
    uint32_t generation;
    int kind;

    // Graph links, owned by the audio thread and only ever mutated by a command.
    SakuraAudioHandle firstChild;
    SakuraAudioHandle nextSibling;
    SakuraAudioHandle parent;

    // Parameters. Written by control threads as single atomic stores and read once per block, so
    // the audio path never reads anything that could be half-updated.
    sakura_atomic_u32 volume;   // float bits
    sakura_atomic_u32 panLeft;  // float bits
    sakura_atomic_u32 panRight; // float bits
    sakura_atomic_u64 rate;     // double bits
    sakura_atomic_u32 looping;
    sakura_atomic_u64 restartFrame;
    sakura_atomic_u32 filterEnabled;

    // Two coefficient sets and the index of the live one, so publishing five floats is one visible
    // store rather than five. b0, b1, b2, a1, a2 as float bits.
    sakura_atomic_u32 filterCoefficients[2][5];
    sakura_atomic_u32 filterActiveSet;

    sakura_atomic_u32 running;

    // Source.
    sakura_atomic_u32 sourceKind;
    SakuraAudioHandle buffer;
    Ring ring;
    int64_t cursor; // static sources only, in frames; audio-thread-owned

    // DSP state, audio-thread-owned.
    float resamplerWindow[RESAMPLER_WINDOW_FRAMES * SAKURA_AUDIO_MAX_CHANNELS];
    double resamplerPosition;
    int resamplerPrimed;
    int resamplerDrainedFrames;
    double filterState[SAKURA_AUDIO_MAX_CHANNELS * 2];
    int endHandled;

    // Published state.
    sakura_atomic_u64 sourceFrames;
    sakura_atomic_u64 endEpoch;
    sakura_atomic_u32 ended;

    // Metering.
    float capture[CAPTURE_SLOTS][SAKURA_AUDIO_FFT_SIZE];
    int captureSlot;
    int captureWrite;
    sakura_atomic_u32 capturePublished; // slot index + 1; 0 until a full window exists
    sakura_atomic_u32 segmentPeakLeft[PEAK_SEGMENT_COUNT];
    sakura_atomic_u32 segmentPeakRight[PEAK_SEGMENT_COUNT];
    int segmentIndex;
    int segmentFrames;
    float segmentCurrentLeft;
    float segmentCurrentRight;

    float *scratch; // one mix block of this node's own output, before it is summed into its parent
} Node;

typedef struct Buffer
{
    sakura_atomic_u32 slotState;
    uint32_t generation;
    sakura_atomic_u64 references;
    float *data;
    int64_t frames;
} Buffer;

// ---------------------------------------------------------------------------------------------
// Command queue
//
// Bounded MPSC. The reservation is a fetch-add, so several control threads can post without a lock;
// in practice the framework posts from one update thread, and the multi-producer tolerance is there
// so that a sample played from a loader thread is not a data race.
//
// The audio thread consumes without ever waiting: a slot that is reserved but not yet marked ready
// stops the drain for this callback and is picked up on the next one.

#define CMD_NOP 0
#define CMD_ADD_CHILD 1
#define CMD_REMOVE_CHILD 2
#define CMD_DESTROY 3
#define CMD_PLAY 4
#define CMD_PAUSE 5
#define CMD_STOP 6
#define CMD_SEEK 7
#define CMD_SET_BUFFER 8

typedef struct Command
{
    uint32_t type;
    SakuraAudioHandle target;
    SakuraAudioHandle aux;
    int64_t value;
} Command;

typedef struct CommandQueue
{
    Command *slots;
    sakura_atomic_u32 *ready;
    uint64_t capacity; // a power of two
    sakura_atomic_u64 writeSequence;
    sakura_atomic_u64 readSequence;
} CommandQueue;

// ---------------------------------------------------------------------------------------------
// Engine

struct SakuraAudioEngine
{
    SakuraAudioConfig config;

    Node *nodes;
    int nodeCount;
    Buffer *buffers;
    int bufferCount;

    CommandQueue commands;
    SakuraAudioHandle root;

    float *nodeScratch; // nodeCount blocks, carved up across the nodes
    float *mixBuffer;   // the block handed to SDL

    // FFT tables, built once at create time so a spectrum read does no trigonometry.
    float *fftWindow;
    int *fftReversal;
    float *twiddleReal;
    float *twiddleImaginary;

    sakura_atomic_u64 statCallbacks;
    sakura_atomic_u64 statFramesMixed;
    sakura_atomic_u64 statStarvations;
    sakura_atomic_u64 statPutFailures;
    sakura_atomic_u64 statCommandsDropped;
    sakura_atomic_u64 statCallbackMicroseconds;
    sakura_atomic_u32 statActiveVoices;

    int activeVoicesThisBlock; // audio-thread-owned accumulator
};

static sakura_sdl_put_fn sdl_put = NULL;

int sakura_audio_abi_version(void) { return SAKURA_AUDIO_ABI_VERSION; }

void sakura_audio_set_sdl_put(sakura_sdl_put_fn function) { sdl_put = function; }

// ---------------------------------------------------------------------------------------------
// Helpers

static uint64_t round_up_power_of_two(uint64_t value)
{
    uint64_t result = 1;

    while (result < value)
        result <<= 1;

    return result;
}

static int64_t now_microseconds(void)
{
    // timespec_get is C11 and needs no platform header, which is the whole reason it is used here
    // rather than clock_gettime or QueryPerformanceCounter. It is the one thing on the audio path
    // that is not pure arithmetic; it exists because a callback whose cost is not measured is a
    // callback whose latency budget is guesswork.
    struct timespec time;

    if (timespec_get(&time, TIME_UTC) == 0)
        return 0;

    return (int64_t)time.tv_sec * 1000000 + time.tv_nsec / 1000;
}

static Node *resolve_node(SakuraAudioEngine *engine, SakuraAudioHandle handle)
{
    if (engine == NULL || handle == SAKURA_AUDIO_INVALID_HANDLE)
        return NULL;

    uint32_t index = handle_index(handle);

    if (index >= (uint32_t)engine->nodeCount)
        return NULL;

    Node *node = &engine->nodes[index];

    if (sakura_atomic_load_u32(&node->slotState) != SLOT_LIVE)
        return NULL;

    return node->generation == handle_generation(handle) ? node : NULL;
}

static Buffer *resolve_buffer(SakuraAudioEngine *engine, SakuraAudioHandle handle)
{
    if (engine == NULL || handle == SAKURA_AUDIO_INVALID_HANDLE)
        return NULL;

    uint32_t index = handle_index(handle);

    if (index >= (uint32_t)engine->bufferCount)
        return NULL;

    Buffer *buffer = &engine->buffers[index];

    if (sakura_atomic_load_u32(&buffer->slotState) != SLOT_LIVE)
        return NULL;

    return buffer->generation == handle_generation(handle) ? buffer : NULL;
}

static SakuraAudioHandle node_handle(SakuraAudioEngine *engine, Node *node)
{
    return handle_make((uint32_t)(node - engine->nodes), node->generation);
}

// Drops one reference. Callable from the audio thread: it never frees, it only marks the slot for
// sakura_audio_engine_maintain to collect.
static void buffer_release_reference(Buffer *buffer)
{
    if (buffer == NULL)
        return;

    if (sakura_atomic_add_i64(&buffer->references, -1) <= 0)
        sakura_atomic_store_u32(&buffer->slotState, SLOT_RETIRED);
}

// ---------------------------------------------------------------------------------------------
// Metering
//
// The port of the managed AmplitudeTap, minus the FFT: peaks are trivial and are computed here, the
// transform is deferred to whoever reads the spectrum. An FFT on this thread would be the single
// most expensive thing in the callback and nothing needs it at audio rate.

static void tap_reset(Node *node)
{
    memset(node->capture, 0, sizeof(node->capture));
    node->captureSlot = 0;
    node->captureWrite = 0;
    sakura_atomic_store_u32(&node->capturePublished, 0);

    for (int i = 0; i < PEAK_SEGMENT_COUNT; i++)
    {
        sakura_atomic_store_f32(&node->segmentPeakLeft[i], 0.0f);
        sakura_atomic_store_f32(&node->segmentPeakRight[i], 0.0f);
    }

    node->segmentIndex = 0;
    node->segmentFrames = 0;
    node->segmentCurrentLeft = 0.0f;
    node->segmentCurrentRight = 0.0f;
}

static void tap_feed(Node *node, const float *block, int frames, int channels)
{
    if (frames <= 0)
        return;

    int rightOffset = channels > 1 ? 1 : 0;
    int segment = node->segmentIndex;
    int framesInSegment = node->segmentFrames;
    float peakLeft = node->segmentCurrentLeft;
    float peakRight = node->segmentCurrentRight;
    int slot = node->captureSlot;
    int write = node->captureWrite;

    for (int frame = 0; frame < frames; frame++)
    {
        float left = block[frame * channels];
        float right = block[frame * channels + rightOffset];

        float absoluteLeft = fabsf(left);
        float absoluteRight = fabsf(right);

        if (absoluteLeft > peakLeft) peakLeft = absoluteLeft;
        if (absoluteRight > peakRight) peakRight = absoluteRight;

        if (++framesInSegment == PEAK_SEGMENT_FRAMES)
        {
            sakura_atomic_store_f32(&node->segmentPeakLeft[segment], peakLeft);
            sakura_atomic_store_f32(&node->segmentPeakRight[segment], peakRight);

            segment = segment + 1 == PEAK_SEGMENT_COUNT ? 0 : segment + 1;
            framesInSegment = 0;
            peakLeft = 0.0f;
            peakRight = 0.0f;
        }

        // Both channels folded into one spectrum, as BASS does unless asked for individual FFTs.
        node->capture[slot][write] = (left + right) * 0.5f;

        if (++write == SAKURA_AUDIO_FFT_SIZE)
        {
            // Publish the finished window and move on to the next slot. Three slots means a reader
            // that started copying the window we just published has two more windows -- about 23 ms
            // at 44.1 kHz -- before the writer could come back around to it.
            sakura_atomic_store_u32(&node->capturePublished, (uint32_t)slot + 1u);
            slot = slot + 1 == CAPTURE_SLOTS ? 0 : slot + 1;
            write = 0;
        }
    }

    // The in-progress segment is published too, so a reader sees a peak that is at most one segment
    // stale rather than waiting 64 frames for the boundary.
    sakura_atomic_store_f32(&node->segmentPeakLeft[segment], peakLeft);
    sakura_atomic_store_f32(&node->segmentPeakRight[segment], peakRight);

    node->segmentIndex = segment;
    node->segmentFrames = framesInSegment;
    node->segmentCurrentLeft = peakLeft;
    node->segmentCurrentRight = peakRight;
    node->captureSlot = slot;
    node->captureWrite = write;
}

// ---------------------------------------------------------------------------------------------
// Sources

static int source_pull_frame(SakuraAudioEngine *engine, Node *node, float *destination)
{
    int channels = engine->config.channels;
    uint32_t kind = sakura_atomic_load_u32(&node->sourceKind);

    if (kind == SOURCE_STATIC)
    {
        Buffer *buffer = resolve_buffer(engine, node->buffer);

        if (buffer == NULL || node->cursor >= buffer->frames)
            return 0;

        memcpy(destination, buffer->data + node->cursor * channels, (size_t)channels * sizeof(float));
        node->cursor++;
        sakura_atomic_store_i64(&node->sourceFrames, node->cursor);
        return 1;
    }

    if (kind == SOURCE_STREAM)
    {
        if (node->ring.data == NULL || ring_available(&node->ring) < (uint64_t)channels)
            return 0;

        if (ring_read(&node->ring, destination, channels) != channels)
            return 0;

        sakura_atomic_store_i64(&node->sourceFrames, sakura_atomic_load_i64(&node->sourceFrames) + 1);
        return 1;
    }

    return 0;
}

static int source_ended(SakuraAudioEngine *engine, Node *node)
{
    uint32_t kind = sakura_atomic_load_u32(&node->sourceKind);

    if (kind == SOURCE_STATIC)
    {
        Buffer *buffer = resolve_buffer(engine, node->buffer);
        return buffer == NULL || node->cursor >= buffer->frames;
    }

    if (kind == SOURCE_STREAM)
    {
        // A streaming source is only ended once its decoder is drained *and* its ring is empty.
        // Short of that, a short read is a decoder that has fallen behind, which is a starvation.
        return sakura_atomic_load_u32(&node->ring.drained) != 0
               && ring_available(&node->ring) < (uint64_t)engine->config.channels;
    }

    return 0;
}

// ---------------------------------------------------------------------------------------------
// Per-voice rate conversion: 4-point third-order Hermite, the port of the managed CubicResampler.
// At a ratio of exactly 1.0 the position stays at 0 and the interpolation collapses to w1 -- unity
// playback is bit-exact, not merely close, so normal playback is not quietly low-passed.

static float interpolate(float w0, float w1, float w2, float w3, float t)
{
    float c0 = w1;
    float c1 = 0.5f * (w2 - w0);
    float c2 = w0 - 2.5f * w1 + 2.0f * w2 - 0.5f * w3;
    float c3 = 0.5f * (w3 - w0) + 1.5f * (w1 - w2);

    return ((c3 * t + c2) * t + c1) * t + c0;
}

static void resampler_reset(Node *node)
{
    memset(node->resamplerWindow, 0, sizeof(node->resamplerWindow));
    node->resamplerPosition = 0.0;
    node->resamplerPrimed = 0;
    node->resamplerDrainedFrames = 0;
}

// Fills the window for the first time. w0 is left silent: there is no frame before the start of the
// source, and inventing one would be worse than starting from zero.
static int resampler_prime(SakuraAudioEngine *engine, Node *node)
{
    int channels = engine->config.channels;

    for (int i = 1; i < RESAMPLER_WINDOW_FRAMES; i++)
    {
        if (!source_pull_frame(engine, node, node->resamplerWindow + i * channels))
        {
            // Nothing at all to play yet. Stay unprimed so the next block tries again rather than
            // treating a not-yet-decoded streaming source as an empty one.
            if (i == 1)
            {
                memset(node->resamplerWindow, 0, sizeof(node->resamplerWindow));
                return 0;
            }

            break;
        }
    }

    node->resamplerPrimed = 1;
    node->resamplerPosition = 0.0;
    return 1;
}

static int resampler_advance(SakuraAudioEngine *engine, Node *node)
{
    int channels = engine->config.channels;

    memmove(node->resamplerWindow,
            node->resamplerWindow + channels,
            (size_t)(RESAMPLER_WINDOW_FRAMES - 1) * channels * sizeof(float));

    float *last = node->resamplerWindow + (RESAMPLER_WINDOW_FRAMES - 1) * channels;

    if (source_pull_frame(engine, node, last))
    {
        node->resamplerDrainedFrames = 0;
        return 1;
    }

    // Let the tail of the window play out into silence rather than cutting off mid-sample, but stop
    // once the whole window is zeros -- past that there is genuinely nothing left.
    memset(last, 0, (size_t)channels * sizeof(float));

    return ++node->resamplerDrainedFrames < RESAMPLER_WINDOW_FRAMES;
}

static int resampler_read(SakuraAudioEngine *engine, Node *node, float *destination, int frameCount, double ratio)
{
    if (frameCount <= 0)
        return 0;

    int channels = engine->config.channels;

    if (ratio < RATIO_MIN) ratio = RATIO_MIN;
    if (ratio > RATIO_MAX) ratio = RATIO_MAX;

    if (!node->resamplerPrimed && !resampler_prime(engine, node))
        return 0;

    int produced = 0;

    while (produced < frameCount)
    {
        int offset = produced * channels;

        for (int channel = 0; channel < channels; channel++)
        {
            float w0 = node->resamplerWindow[channel];
            float w1 = node->resamplerWindow[channels + channel];
            float w2 = node->resamplerWindow[2 * channels + channel];
            float w3 = node->resamplerWindow[3 * channels + channel];

            destination[offset + channel] = interpolate(w0, w1, w2, w3, (float)node->resamplerPosition);
        }

        produced++;
        node->resamplerPosition += ratio;

        while (node->resamplerPosition >= 1.0)
        {
            if (!resampler_advance(engine, node))
                return produced;

            node->resamplerPosition -= 1.0;
        }
    }

    return produced;
}

// ---------------------------------------------------------------------------------------------
// Inserts

// Transposed direct form II, two state elements per channel: half the state of DF-I and better
// behaved at low cutoffs, where DF-I's delay line accumulates error against large-magnitude history.
// Coefficients arrive already normalised from the managed side.
static void filter_process(Node *node, float *block, int frames, int channels)
{
    uint32_t set = sakura_atomic_load_u32(&node->filterActiveSet) & 1u;

    float b0 = sakura_atomic_load_f32(&node->filterCoefficients[set][0]);
    float b1 = sakura_atomic_load_f32(&node->filterCoefficients[set][1]);
    float b2 = sakura_atomic_load_f32(&node->filterCoefficients[set][2]);
    float a1 = sakura_atomic_load_f32(&node->filterCoefficients[set][3]);
    float a2 = sakura_atomic_load_f32(&node->filterCoefficients[set][4]);

    for (int frame = 0; frame < frames; frame++)
    {
        for (int channel = 0; channel < channels; channel++)
        {
            int state = channel * 2;
            double input = block[frame * channels + channel];

            double output = b0 * input + node->filterState[state];
            node->filterState[state] = b1 * input - a1 * output + node->filterState[state + 1];
            node->filterState[state + 1] = b2 * input - a2 * output;

            block[frame * channels + channel] = (float)output;
        }
    }
}

// Filter, then gain and pan, then metering, then sum into the parent's block. Filtering before gain
// so that automating volume does not change the filter's behaviour; metering after gain so a
// visualiser shows what is audible.
static void apply_inserts_and_mix(SakuraAudioEngine *engine, Node *node, float *block, float *destination, int frames)
{
    int channels = engine->config.channels;

    if (sakura_atomic_load_u32(&node->filterEnabled))
        filter_process(node, block, frames, channels);

    float volume = sakura_atomic_load_f32(&node->volume);
    float left = volume * sakura_atomic_load_f32(&node->panLeft);
    float right = volume * sakura_atomic_load_f32(&node->panRight);

    if (channels == 2)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            block[frame * 2] *= left;
            block[frame * 2 + 1] *= right;
        }
    }
    else
    {
        // Panning is only meaningful in stereo; anything else just takes the volume.
        for (int i = 0; i < frames * channels; i++)
            block[i] *= volume;
    }

    tap_feed(node, block, frames, channels);

    for (int i = 0; i < frames * channels; i++)
        destination[i] += block[i];
}

// ---------------------------------------------------------------------------------------------
// Transport, applied on the audio thread

// Moves the cursor and clears every piece of state that still holds audio from the old position: the
// interpolation window, the filter's delay line, and the metering capture.
static void seek_internal(SakuraAudioEngine *engine, Node *node, int64_t frame)
{
    uint32_t kind = sakura_atomic_load_u32(&node->sourceKind);

    if (kind == SOURCE_STATIC)
    {
        Buffer *buffer = resolve_buffer(engine, node->buffer);
        int64_t limit = buffer != NULL ? buffer->frames : 0;

        if (frame < 0) frame = 0;
        if (frame > limit) frame = limit;

        node->cursor = frame;
        sakura_atomic_store_i64(&node->sourceFrames, frame);
    }
    else
    {
        // A streaming voice's position is the base the caller established plus what has been pulled
        // since, so seeking resets the count and the caller owns the base. What the decoder does is
        // the caller's business too -- see sakura_audio_node_seek.
        sakura_atomic_store_i64(&node->sourceFrames, 0);
    }

    resampler_reset(node);
    memset(node->filterState, 0, sizeof(node->filterState));
    tap_reset(node);

    node->endHandled = 0;
    sakura_atomic_store_u32(&node->ended, 0);
}

// Called the moment the source runs dry. A looping static voice wraps here, as tightly as the block
// boundary allows; a looping streaming voice cannot, because only the caller can seek its decoder,
// so it publishes the end and waits to be told where to go.
static void handle_end(SakuraAudioEngine *engine, Node *node)
{
    if (node->endHandled)
        return;

    node->endHandled = 1;

    int looping = sakura_atomic_load_u32(&node->looping) != 0;

    if (looping)
    {
        if (sakura_atomic_load_u32(&node->sourceKind) == SOURCE_STATIC)
            seek_internal(engine, node, sakura_atomic_load_i64(&node->restartFrame));
    }
    else
    {
        sakura_atomic_store_u32(&node->running, 0);
    }

    sakura_atomic_add_i64(&node->endEpoch, 1);
}

// ---------------------------------------------------------------------------------------------
// Graph mutation, applied on the audio thread

static void detach_from_parent(SakuraAudioEngine *engine, Node *node)
{
    Node *parent = resolve_node(engine, node->parent);
    SakuraAudioHandle self = node_handle(engine, node);

    if (parent != NULL)
    {
        if (parent->firstChild == self)
        {
            parent->firstChild = node->nextSibling;
        }
        else
        {
            SakuraAudioHandle current = parent->firstChild;

            for (int guard = 0; current != SAKURA_AUDIO_INVALID_HANDLE && guard < engine->nodeCount; guard++)
            {
                Node *sibling = resolve_node(engine, current);

                if (sibling == NULL)
                    break;

                if (sibling->nextSibling == self)
                {
                    sibling->nextSibling = node->nextSibling;
                    break;
                }

                current = sibling->nextSibling;
            }
        }
    }

    node->parent = SAKURA_AUDIO_INVALID_HANDLE;
    node->nextSibling = SAKURA_AUDIO_INVALID_HANDLE;
}

static void apply_command(SakuraAudioEngine *engine, const Command *command)
{
    Node *node = resolve_node(engine, command->target);

    if (node == NULL)
        return;

    switch (command->type)
    {
        case CMD_ADD_CHILD:
        {
            Node *child = resolve_node(engine, command->aux);

            if (child == NULL || child == node)
                break;

            detach_from_parent(engine, child);

            child->parent = command->target;
            child->nextSibling = node->firstChild;
            node->firstChild = command->aux;
            break;
        }

        case CMD_REMOVE_CHILD:
        {
            Node *child = resolve_node(engine, command->aux);

            if (child != NULL && child->parent == command->target)
                detach_from_parent(engine, child);

            break;
        }

        case CMD_DESTROY:
        {
            detach_from_parent(engine, node);

            // Children outlive their parent rather than being destroyed with it, matching the
            // managed mixer, which clears its channel list without disposing what was in it.
            SakuraAudioHandle child = node->firstChild;

            for (int guard = 0; child != SAKURA_AUDIO_INVALID_HANDLE && guard < engine->nodeCount; guard++)
            {
                Node *current = resolve_node(engine, child);

                if (current == NULL)
                    break;

                SakuraAudioHandle next = current->nextSibling;
                current->parent = SAKURA_AUDIO_INVALID_HANDLE;
                current->nextSibling = SAKURA_AUDIO_INVALID_HANDLE;
                child = next;
            }

            node->firstChild = SAKURA_AUDIO_INVALID_HANDLE;

            Buffer *buffer = resolve_buffer(engine, node->buffer);
            node->buffer = SAKURA_AUDIO_INVALID_HANDLE;
            sakura_atomic_store_u32(&node->sourceKind, SOURCE_NONE);
            sakura_atomic_store_u32(&node->running, 0);
            buffer_release_reference(buffer);

            // Nothing is freed here. The slot, its ring and the PCM it referenced come back on the
            // next sakura_audio_engine_maintain, on a thread that is allowed to call free.
            sakura_atomic_store_u32(&node->slotState, SLOT_RETIRED);
            break;
        }

        case CMD_PLAY:
        {
            // Replaying something that already finished should start it over rather than sit at the
            // end producing silence. Only a static source can do that from here: rewinding a stream
            // means rewinding a decoder, which is the caller's.
            if (sakura_atomic_load_u32(&node->sourceKind) == SOURCE_STATIC && source_ended(engine, node))
            {
                int looping = sakura_atomic_load_u32(&node->looping) != 0;
                seek_internal(engine, node, looping ? sakura_atomic_load_i64(&node->restartFrame) : 0);
            }

            node->endHandled = 0;
            sakura_atomic_store_u32(&node->running, 1);
            break;
        }

        case CMD_PAUSE:
            sakura_atomic_store_u32(&node->running, 0);
            break;

        case CMD_STOP:
            sakura_atomic_store_u32(&node->running, 0);
            seek_internal(engine, node, 0);
            break;

        case CMD_SEEK:
            seek_internal(engine, node, command->value);
            break;

        case CMD_SET_BUFFER:
        {
            Buffer *previous = resolve_buffer(engine, node->buffer);

            node->buffer = command->aux;
            node->cursor = 0;
            sakura_atomic_store_u32(&node->sourceKind, command->aux == SAKURA_AUDIO_INVALID_HANDLE ? SOURCE_NONE : SOURCE_STATIC);
            sakura_atomic_store_i64(&node->sourceFrames, 0);

            resampler_reset(node);
            memset(node->filterState, 0, sizeof(node->filterState));
            tap_reset(node);
            node->endHandled = 0;
            sakura_atomic_store_u32(&node->ended, 0);

            buffer_release_reference(previous);
            break;
        }

        default:
            break;
    }
}

static int enqueue_command(SakuraAudioEngine *engine, uint32_t type, SakuraAudioHandle target, SakuraAudioHandle aux, int64_t value)
{
    CommandQueue *queue = &engine->commands;

    uint64_t sequence = sakura_atomic_fetch_add_u64(&queue->writeSequence, 1);
    uint64_t index = sequence & (queue->capacity - 1);

    // Wait for the slot if the queue has wrapped all the way around. Bounded: on giving up the slot
    // is written anyway, which loses whatever unconsumed command was in it, and is counted. With the
    // default 4096 slots drained on every callback this is unreachable short of the audio thread
    // having stopped entirely -- at which point a lost volume change is not the problem.
    int dropped = 0;

    for (uint32_t spin = 0; sequence - sakura_atomic_load_u64(&queue->readSequence) >= queue->capacity; spin++)
    {
        if (spin >= 1000000u)
        {
            dropped = 1;
            break;
        }
    }

    queue->slots[index].type = type;
    queue->slots[index].target = target;
    queue->slots[index].aux = aux;
    queue->slots[index].value = value;

    sakura_atomic_store_u32(&queue->ready[index], 1);

    if (dropped)
    {
        sakura_atomic_add_i64(&engine->statCommandsDropped, 1);
        return SAKURA_AUDIO_FULL;
    }

    return SAKURA_AUDIO_OK;
}

static void process_commands(SakuraAudioEngine *engine)
{
    CommandQueue *queue = &engine->commands;

    for (int i = 0; i < COMMANDS_PER_CALLBACK; i++)
    {
        uint64_t sequence = sakura_atomic_load_u64(&queue->readSequence);
        uint64_t index = sequence & (queue->capacity - 1);

        // Reserved but not yet filled in: stop here rather than spinning, and pick it up next time.
        if (!sakura_atomic_load_u32(&queue->ready[index]))
            break;

        Command command = queue->slots[index];

        sakura_atomic_store_u32(&queue->ready[index], 0);
        sakura_atomic_store_u64(&queue->readSequence, sequence + 1);

        apply_command(engine, &command);
    }
}

// Applies posted ring discards. Every live streaming voice, not just the playing ones: a seek on a
// paused track has to complete, or the writer waits forever.
static void sync_streams(SakuraAudioEngine *engine)
{
    for (int i = 0; i < engine->nodeCount; i++)
    {
        Node *node = &engine->nodes[i];

        if (sakura_atomic_load_u32(&node->slotState) != SLOT_LIVE)
            continue;

        if (sakura_atomic_load_u32(&node->sourceKind) != SOURCE_STREAM || node->ring.data == NULL)
            continue;

        if (!ring_flush_pending(&node->ring))
            continue;

        ring_sync(&node->ring);

        // The window still holds frames from before the seek, and three interpolated frames of the
        // old position bleeding into the new one is audible as a click. Clearing here as well as on
        // the seek command means the two do not have to arrive in a particular order.
        resampler_reset(node);
        memset(node->filterState, 0, sizeof(node->filterState));
        tap_reset(node);
        node->endHandled = 0;
        sakura_atomic_store_u32(&node->ended, 0);
    }
}

// ---------------------------------------------------------------------------------------------
// Mixing

static void mix_node(SakuraAudioEngine *engine, Node *node, float *destination, int frames, int depth)
{
    if (node == NULL || !sakura_atomic_load_u32(&node->running))
        return;

    int channels = engine->config.channels;
    size_t blockBytes = (size_t)frames * channels * sizeof(float);
    float *block = node->scratch;

    if (node->kind == NODE_MIXER)
    {
        if (node->firstChild == SAKURA_AUDIO_INVALID_HANDLE)
            return;

        memset(block, 0, blockBytes);

        if (depth + 1 < SAKURA_AUDIO_MAX_DEPTH)
        {
            SakuraAudioHandle child = node->firstChild;

            for (int guard = 0; child != SAKURA_AUDIO_INVALID_HANDLE && guard < engine->nodeCount; guard++)
            {
                Node *current = resolve_node(engine, child);

                if (current == NULL)
                    break;

                // Children add into the shared block; a stopped or starved one contributes nothing.
                mix_node(engine, current, block, frames, depth + 1);
                child = current->nextSibling;
            }
        }
    }
    else
    {
        if (sakura_atomic_load_u32(&node->sourceKind) == SOURCE_NONE)
            return;

        double ratio = sakura_atomic_load_f64(&node->rate);
        int produced = resampler_read(engine, node, block, frames, ratio);

        if (produced < frames)
        {
            // Nothing more is coming from this source for now. Whether that is the end of the audio
            // or a decoder that has fallen behind is the source's call, not ours.
            memset(block + (size_t)produced * channels, 0, (size_t)(frames - produced) * channels * sizeof(float));

            if (source_ended(engine, node))
                handle_end(engine, node);
            else
                sakura_atomic_add_i64(&engine->statStarvations, 1);
        }

        sakura_atomic_store_u32(&node->ended, source_ended(engine, node) ? 1u : 0u);
        engine->activeVoicesThisBlock++;
    }

    // The whole block, silent tail included: a running voice occupies its slice of the timeline
    // whether or not the decoder filled it, and the metering and the filter both want to see that
    // silence rather than have the gap skipped over.
    apply_inserts_and_mix(engine, node, block, destination, frames);
}

static void mix_block(SakuraAudioEngine *engine, float *destination, int frames)
{
    int channels = engine->config.channels;
    int count = frames * channels;

    memset(destination, 0, (size_t)count * sizeof(float));

    engine->activeVoicesThisBlock = 0;

    mix_node(engine, resolve_node(engine, engine->root), destination, frames, 0);

    // Sources are not normalised -- a lossy decoder reconstructing a hot master genuinely exceeds
    // unity -- and summing several of them exceeds it further. Clamp here, at the one place the audio
    // leaves our control, rather than quietly wrapping in the driver.
    for (int i = 0; i < count; i++)
    {
        float sample = destination[i];
        destination[i] = sample > 1.0f ? 1.0f : (sample < -1.0f ? -1.0f : sample);
    }

    sakura_atomic_store_u32(&engine->statActiveVoices, (uint32_t)engine->activeVoicesThisBlock);
    sakura_atomic_add_i64(&engine->statFramesMixed, frames);
}

// The SDL_AudioStreamCallback SDL calls when the device wants more audio. Everything reachable from
// here is preallocated, lock-free and free of managed code -- see the real-time discipline notes in
// README.md before adding anything to it.
static void stream_callback(void *userdata, void *stream, int additionalAmount, int totalAmount)
{
    (void)totalAmount;

    SakuraAudioEngine *engine = (SakuraAudioEngine *)userdata;

    if (engine == NULL || additionalAmount <= 0)
        return;

    int64_t started = now_microseconds();

    process_commands(engine);
    sync_streams(engine);

    int channels = engine->config.channels;
    int frameBytes = channels * (int)sizeof(float);
    int remaining = additionalAmount / frameBytes;

    while (remaining > 0)
    {
        int frames = remaining < engine->config.mixBlockFrames ? remaining : engine->config.mixBlockFrames;

        mix_block(engine, engine->mixBuffer, frames);

        sakura_sdl_put_fn put = sdl_put;

        if (put == NULL || !put(stream, engine->mixBuffer, frames * frameBytes))
            sakura_atomic_add_i64(&engine->statPutFailures, 1);

        remaining -= frames;
    }

    sakura_atomic_add_i64(&engine->statCallbacks, 1);
    sakura_atomic_store_i64(&engine->statCallbackMicroseconds, now_microseconds() - started);
}

void *sakura_audio_get_stream_callback(void) { return (void *)stream_callback; }

int sakura_audio_engine_mix(SakuraAudioEngine *engine, float *destination, int frames)
{
    if (engine == NULL || destination == NULL || frames <= 0)
        return SAKURA_AUDIO_INVALID;

    process_commands(engine);
    sync_streams(engine);

    int channels = engine->config.channels;
    int written = 0;

    while (written < frames)
    {
        int remaining = frames - written;
        int block = remaining < engine->config.mixBlockFrames ? remaining : engine->config.mixBlockFrames;

        mix_block(engine, destination + (size_t)written * channels, block);
        written += block;
    }

    return written;
}

// ---------------------------------------------------------------------------------------------
// Published state and spectrum

int sakura_audio_node_get_state(SakuraAudioEngine *engine, SakuraAudioHandle handle, SakuraAudioNodeState *state)
{
    Node *node = resolve_node(engine, handle);

    if (node == NULL || state == NULL)
        return SAKURA_AUDIO_INVALID;

    state->sourceFrames = sakura_atomic_load_i64(&node->sourceFrames);
    state->endEpoch = sakura_atomic_load_i64(&node->endEpoch);
    state->running = (int32_t)sakura_atomic_load_u32(&node->running);
    state->ended = (int32_t)sakura_atomic_load_u32(&node->ended);

    float peakLeft = 0.0f;
    float peakRight = 0.0f;

    for (int i = 0; i < PEAK_SEGMENT_COUNT; i++)
    {
        float left = sakura_atomic_load_f32(&node->segmentPeakLeft[i]);
        float right = sakura_atomic_load_f32(&node->segmentPeakRight[i]);

        if (left > peakLeft) peakLeft = left;
        if (right > peakRight) peakRight = right;
    }

    state->amplitudeLeft = peakLeft;
    state->amplitudeRight = peakRight;

    return SAKURA_AUDIO_OK;
}

int sakura_audio_node_read_spectrum(SakuraAudioEngine *engine, SakuraAudioHandle handle, float *bins, int binCount)
{
    Node *node = resolve_node(engine, handle);

    if (node == NULL || bins == NULL || binCount <= 0)
        return SAKURA_AUDIO_INVALID;

    uint32_t published = sakura_atomic_load_u32(&node->capturePublished);

    if (published == 0)
        return 0;

    if (binCount > SAKURA_AUDIO_BINS)
        binCount = SAKURA_AUDIO_BINS;

    // On the stack, so this is reentrant and two visualisers on two threads cannot collide.
    float real[SAKURA_AUDIO_FFT_SIZE];
    float imaginary[SAKURA_AUDIO_FFT_SIZE];

    const float *capture = node->capture[published - 1];

    memset(imaginary, 0, sizeof(imaginary));

    // Window into bit-reversed order in one pass, so the butterflies below run in place.
    for (int i = 0; i < SAKURA_AUDIO_FFT_SIZE; i++)
        real[engine->fftReversal[i]] = capture[i] * engine->fftWindow[i];

    for (int size = 2; size <= SAKURA_AUDIO_FFT_SIZE; size <<= 1)
    {
        int half = size / 2;
        int step = SAKURA_AUDIO_FFT_SIZE / size;

        for (int start = 0; start < SAKURA_AUDIO_FFT_SIZE; start += size)
        {
            for (int k = 0; k < half; k++)
            {
                float twiddleReal = engine->twiddleReal[k * step];
                float twiddleImaginary = engine->twiddleImaginary[k * step];

                int even = start + k;
                int odd = even + half;

                float oddReal = real[odd] * twiddleReal - imaginary[odd] * twiddleImaginary;
                float oddImaginary = real[odd] * twiddleImaginary + imaginary[odd] * twiddleReal;

                real[odd] = real[even] - oddReal;
                imaginary[odd] = imaginary[even] - oddImaginary;
                real[even] += oddReal;
                imaginary[even] += oddImaginary;
            }
        }
    }

    // Scaled so a bin-centred full-scale sine reads back at its own peak amplitude: the
    // 2 / (N * coherentGain) correction, which for Hann is 4 / N. Not clipped to 1.0 -- input above
    // unity is normal, and clamping here would hide it from a visualiser.
    const float scale = 4.0f / (float)SAKURA_AUDIO_FFT_SIZE;

    for (int i = 0; i < binCount; i++)
        bins[i] = sqrtf(real[i] * real[i] + imaginary[i] * imaginary[i]) * scale;

    return binCount;
}

int sakura_audio_engine_get_stats(SakuraAudioEngine *engine, SakuraAudioStats *stats)
{
    if (engine == NULL || stats == NULL)
        return SAKURA_AUDIO_INVALID;

    stats->callbacks = sakura_atomic_load_i64(&engine->statCallbacks);
    stats->framesMixed = sakura_atomic_load_i64(&engine->statFramesMixed);
    stats->starvations = sakura_atomic_load_i64(&engine->statStarvations);
    stats->putFailures = sakura_atomic_load_i64(&engine->statPutFailures);
    stats->commandsDropped = sakura_atomic_load_i64(&engine->statCommandsDropped);
    stats->callbackMicroseconds = sakura_atomic_load_i64(&engine->statCallbackMicroseconds);
    stats->activeVoices = (int32_t)sakura_atomic_load_u32(&engine->statActiveVoices);

    return SAKURA_AUDIO_OK;
}

// ---------------------------------------------------------------------------------------------
// Parameters

int sakura_audio_node_set_gain(SakuraAudioEngine *engine, SakuraAudioHandle handle, float volume, float panLeft, float panRight)
{
    Node *node = resolve_node(engine, handle);

    if (node == NULL)
        return SAKURA_AUDIO_INVALID;

    sakura_atomic_store_f32(&node->volume, volume < 0.0f ? 0.0f : volume);
    sakura_atomic_store_f32(&node->panLeft, panLeft);
    sakura_atomic_store_f32(&node->panRight, panRight);

    return SAKURA_AUDIO_OK;
}

int sakura_audio_node_set_rate(SakuraAudioEngine *engine, SakuraAudioHandle handle, double ratio)
{
    Node *node = resolve_node(engine, handle);

    if (node == NULL)
        return SAKURA_AUDIO_INVALID;

    sakura_atomic_store_f64(&node->rate, ratio);
    return SAKURA_AUDIO_OK;
}

int sakura_audio_node_set_loop(SakuraAudioEngine *engine, SakuraAudioHandle handle, int looping, int64_t restartFrame)
{
    Node *node = resolve_node(engine, handle);

    if (node == NULL)
        return SAKURA_AUDIO_INVALID;

    sakura_atomic_store_i64(&node->restartFrame, restartFrame < 0 ? 0 : restartFrame);
    sakura_atomic_store_u32(&node->looping, looping ? 1u : 0u);

    return SAKURA_AUDIO_OK;
}

int sakura_audio_node_set_filter(SakuraAudioEngine *engine, SakuraAudioHandle handle, int enabled,
                                 float b0, float b1, float b2, float a1, float a2)
{
    Node *node = resolve_node(engine, handle);

    if (node == NULL)
        return SAKURA_AUDIO_INVALID;

    // Written into the set the audio thread is not reading and published by flipping the index, so a
    // block never mixes half of one coefficient set with half of another. The managed filter gets
    // this for free by publishing an immutable record; here it costs a second set of five floats.
    uint32_t target = sakura_atomic_load_u32(&node->filterActiveSet) == 0 ? 1u : 0u;

    sakura_atomic_store_f32(&node->filterCoefficients[target][0], b0);
    sakura_atomic_store_f32(&node->filterCoefficients[target][1], b1);
    sakura_atomic_store_f32(&node->filterCoefficients[target][2], b2);
    sakura_atomic_store_f32(&node->filterCoefficients[target][3], a1);
    sakura_atomic_store_f32(&node->filterCoefficients[target][4], a2);

    sakura_atomic_store_u32(&node->filterActiveSet, target);
    sakura_atomic_store_u32(&node->filterEnabled, enabled ? 1u : 0u);

    return SAKURA_AUDIO_OK;
}

// ---------------------------------------------------------------------------------------------
// Transport

int sakura_audio_node_play(SakuraAudioEngine *engine, SakuraAudioHandle handle)
{
    return resolve_node(engine, handle) == NULL ? SAKURA_AUDIO_INVALID : enqueue_command(engine, CMD_PLAY, handle, 0, 0);
}

int sakura_audio_node_pause(SakuraAudioEngine *engine, SakuraAudioHandle handle)
{
    return resolve_node(engine, handle) == NULL ? SAKURA_AUDIO_INVALID : enqueue_command(engine, CMD_PAUSE, handle, 0, 0);
}

int sakura_audio_node_stop(SakuraAudioEngine *engine, SakuraAudioHandle handle)
{
    return resolve_node(engine, handle) == NULL ? SAKURA_AUDIO_INVALID : enqueue_command(engine, CMD_STOP, handle, 0, 0);
}

int sakura_audio_node_seek(SakuraAudioEngine *engine, SakuraAudioHandle handle, int64_t frame)
{
    return resolve_node(engine, handle) == NULL ? SAKURA_AUDIO_INVALID : enqueue_command(engine, CMD_SEEK, handle, 0, frame);
}

// ---------------------------------------------------------------------------------------------
// Buffers

static Buffer *claim_buffer(SakuraAudioEngine *engine)
{
    for (int i = 0; i < engine->bufferCount; i++)
    {
        Buffer *buffer = &engine->buffers[i];

        if (sakura_atomic_compare_exchange_u32(&buffer->slotState, SLOT_FREE, SLOT_CLAIMED) != SLOT_FREE)
            continue;

        // Generation is bumped before the slot goes live, so a stale handle can never resolve
        // against the reset slot in the window between the two.
        buffer->generation = buffer->generation >= HANDLE_GENERATION_MASK ? 1 : buffer->generation + 1;
        buffer->data = NULL;
        buffer->frames = 0;
        sakura_atomic_store_i64(&buffer->references, 1);

        return buffer;
    }

    return NULL;
}

SakuraAudioHandle sakura_audio_buffer_create(SakuraAudioEngine *engine, const float *interleaved, int64_t frames)
{
    if (engine == NULL || frames <= 0)
        return SAKURA_AUDIO_INVALID_HANDLE;

    Buffer *buffer = claim_buffer(engine);

    if (buffer == NULL)
        return SAKURA_AUDIO_INVALID_HANDLE;

    size_t count = (size_t)frames * (size_t)engine->config.channels;
    buffer->data = (float *)malloc(count * sizeof(float));

    if (buffer->data == NULL)
    {
        sakura_atomic_store_u32(&buffer->slotState, SLOT_FREE);
        return SAKURA_AUDIO_INVALID_HANDLE;
    }

    if (interleaved != NULL)
        memcpy(buffer->data, interleaved, count * sizeof(float));
    else
        memset(buffer->data, 0, count * sizeof(float));

    buffer->frames = frames;
    sakura_atomic_store_u32(&buffer->slotState, SLOT_LIVE);

    return handle_make((uint32_t)(buffer - engine->buffers), buffer->generation);
}

int sakura_audio_buffer_release(SakuraAudioEngine *engine, SakuraAudioHandle handle)
{
    Buffer *buffer = resolve_buffer(engine, handle);

    if (buffer == NULL)
        return SAKURA_AUDIO_INVALID;

    buffer_release_reference(buffer);
    return SAKURA_AUDIO_OK;
}

int sakura_audio_voice_set_buffer(SakuraAudioEngine *engine, SakuraAudioHandle voice, SakuraAudioHandle handle)
{
    Node *node = resolve_node(engine, voice);

    if (node == NULL || node->kind != NODE_VOICE)
        return SAKURA_AUDIO_INVALID;

    if (handle == SAKURA_AUDIO_INVALID_HANDLE)
        return enqueue_command(engine, CMD_SET_BUFFER, voice, SAKURA_AUDIO_INVALID_HANDLE, 0);

    Buffer *buffer = resolve_buffer(engine, handle);

    if (buffer == NULL)
        return SAKURA_AUDIO_INVALID;

    // Claimed here rather than on the audio thread, so the PCM cannot be freed between this call and
    // the command being applied.
    sakura_atomic_add_i64(&buffer->references, 1);

    int result = enqueue_command(engine, CMD_SET_BUFFER, voice, handle, 0);

    if (result != SAKURA_AUDIO_OK)
        buffer_release_reference(buffer);

    return result;
}

// ---------------------------------------------------------------------------------------------
// Streaming

int sakura_audio_voice_set_stream(SakuraAudioEngine *engine, SakuraAudioHandle voice, int capacityFrames)
{
    Node *node = resolve_node(engine, voice);

    if (node == NULL || node->kind != NODE_VOICE || capacityFrames <= 0)
        return SAKURA_AUDIO_INVALID;

    if (node->ring.data != NULL)
        return SAKURA_AUDIO_ERROR;

    uint64_t capacity = round_up_power_of_two((uint64_t)capacityFrames * (uint64_t)engine->config.channels);

    node->ring.data = (float *)calloc((size_t)capacity, sizeof(float));

    if (node->ring.data == NULL)
        return SAKURA_AUDIO_ERROR;

    node->ring.capacity = capacity;
    sakura_atomic_store_u64(&node->ring.writePosition, 0);
    sakura_atomic_store_u64(&node->ring.readPosition, 0);
    sakura_atomic_store_u64(&node->ring.flushTarget, 0);
    sakura_atomic_store_u32(&node->ring.writeEpoch, 0);
    sakura_atomic_store_u32(&node->ring.readEpoch, 0);
    sakura_atomic_store_u32(&node->ring.drained, 0);

    // Published last: until this store the audio thread has no reason to look at the ring at all.
    sakura_atomic_store_u32(&node->sourceKind, SOURCE_STREAM);

    return SAKURA_AUDIO_OK;
}

static Node *resolve_stream_voice(SakuraAudioEngine *engine, SakuraAudioHandle voice)
{
    Node *node = resolve_node(engine, voice);

    if (node == NULL || sakura_atomic_load_u32(&node->sourceKind) != SOURCE_STREAM || node->ring.data == NULL)
        return NULL;

    return node;
}

int sakura_audio_stream_write(SakuraAudioEngine *engine, SakuraAudioHandle voice, const float *interleaved, int frames)
{
    Node *node = resolve_stream_voice(engine, voice);

    if (node == NULL || interleaved == NULL || frames <= 0)
        return SAKURA_AUDIO_INVALID;

    int channels = engine->config.channels;
    return ring_write(&node->ring, interleaved, frames * channels) / channels;
}

int sakura_audio_stream_space(SakuraAudioEngine *engine, SakuraAudioHandle voice)
{
    Node *node = resolve_stream_voice(engine, voice);

    if (node == NULL)
        return SAKURA_AUDIO_INVALID;

    if (ring_flush_pending(&node->ring))
        return 0;

    uint64_t free = node->ring.capacity - ring_available(&node->ring);
    return (int)(free / (uint64_t)engine->config.channels);
}

int sakura_audio_stream_buffered(SakuraAudioEngine *engine, SakuraAudioHandle voice)
{
    Node *node = resolve_stream_voice(engine, voice);

    if (node == NULL)
        return SAKURA_AUDIO_INVALID;

    return (int)(ring_available(&node->ring) / (uint64_t)engine->config.channels);
}

int sakura_audio_stream_set_drained(SakuraAudioEngine *engine, SakuraAudioHandle voice, int drained)
{
    Node *node = resolve_stream_voice(engine, voice);

    if (node == NULL)
        return SAKURA_AUDIO_INVALID;

    sakura_atomic_store_u32(&node->ring.drained, drained ? 1u : 0u);
    return SAKURA_AUDIO_OK;
}

int sakura_audio_stream_flush_begin(SakuraAudioEngine *engine, SakuraAudioHandle voice)
{
    Node *node = resolve_stream_voice(engine, voice);

    if (node == NULL)
        return SAKURA_AUDIO_INVALID;

    sakura_atomic_store_u32(&node->ring.drained, 0);

    // Target first, then the epoch that makes it live: the audio thread only reads the target once it
    // has seen the epoch change.
    sakura_atomic_store_u64(&node->ring.flushTarget, sakura_atomic_load_u64(&node->ring.writePosition));
    sakura_atomic_store_u32(&node->ring.writeEpoch, sakura_atomic_load_u32(&node->ring.writeEpoch) + 1u);

    return SAKURA_AUDIO_OK;
}

int sakura_audio_stream_flush_pending(SakuraAudioEngine *engine, SakuraAudioHandle voice)
{
    Node *node = resolve_stream_voice(engine, voice);
    return node == NULL ? 0 : ring_flush_pending(&node->ring);
}

// ---------------------------------------------------------------------------------------------
// Node lifetime

static void node_reset(SakuraAudioEngine *engine, Node *node, int kind)
{
    node->kind = kind;
    node->firstChild = SAKURA_AUDIO_INVALID_HANDLE;
    node->nextSibling = SAKURA_AUDIO_INVALID_HANDLE;
    node->parent = SAKURA_AUDIO_INVALID_HANDLE;

    sakura_atomic_store_f32(&node->volume, 1.0f);
    sakura_atomic_store_f32(&node->panLeft, 1.0f);
    sakura_atomic_store_f32(&node->panRight, 1.0f);
    sakura_atomic_store_f64(&node->rate, 1.0);
    sakura_atomic_store_u32(&node->looping, 0);
    sakura_atomic_store_i64(&node->restartFrame, 0);
    sakura_atomic_store_u32(&node->filterEnabled, 0);
    sakura_atomic_store_u32(&node->filterActiveSet, 0);

    for (int set = 0; set < 2; set++)
    {
        // A pass-through, for the case where a filter is enabled before any cutoff is published.
        sakura_atomic_store_f32(&node->filterCoefficients[set][0], 1.0f);

        for (int i = 1; i < 5; i++)
            sakura_atomic_store_f32(&node->filterCoefficients[set][i], 0.0f);
    }

    // Mixers mix as soon as they have children; a voice waits to be played. The managed backend
    // starts its two master mixers explicitly, which amounts to the same thing.
    sakura_atomic_store_u32(&node->running, kind == NODE_MIXER ? 1u : 0u);

    sakura_atomic_store_u32(&node->sourceKind, SOURCE_NONE);
    node->buffer = SAKURA_AUDIO_INVALID_HANDLE;
    node->cursor = 0;

    memset(&node->ring, 0, sizeof(node->ring));

    resampler_reset(node);
    memset(node->filterState, 0, sizeof(node->filterState));
    node->endHandled = 0;

    sakura_atomic_store_i64(&node->sourceFrames, 0);
    sakura_atomic_store_i64(&node->endEpoch, 0);
    sakura_atomic_store_u32(&node->ended, 0);

    tap_reset(node);

    node->scratch = engine->nodeScratch
                    + (size_t)(node - engine->nodes) * (size_t)engine->config.mixBlockFrames * (size_t)engine->config.channels;
}

static SakuraAudioHandle claim_node(SakuraAudioEngine *engine, int kind)
{
    if (engine == NULL)
        return SAKURA_AUDIO_INVALID_HANDLE;

    for (int i = 0; i < engine->nodeCount; i++)
    {
        Node *node = &engine->nodes[i];

        if (sakura_atomic_compare_exchange_u32(&node->slotState, SLOT_FREE, SLOT_CLAIMED) != SLOT_FREE)
            continue;

        node->generation = node->generation >= HANDLE_GENERATION_MASK ? 1 : node->generation + 1;
        node_reset(engine, node, kind);

        // Published last. Until this store the audio thread cannot resolve the handle, so everything
        // above is an ordinary single-threaded initialisation.
        sakura_atomic_store_u32(&node->slotState, SLOT_LIVE);

        return handle_make((uint32_t)i, node->generation);
    }

    return SAKURA_AUDIO_INVALID_HANDLE;
}

SakuraAudioHandle sakura_audio_create_mixer(SakuraAudioEngine *engine) { return claim_node(engine, NODE_MIXER); }

SakuraAudioHandle sakura_audio_create_voice(SakuraAudioEngine *engine) { return claim_node(engine, NODE_VOICE); }

int sakura_audio_destroy_node(SakuraAudioEngine *engine, SakuraAudioHandle handle)
{
    if (engine == NULL || handle == engine->root)
        return SAKURA_AUDIO_INVALID;

    return resolve_node(engine, handle) == NULL ? SAKURA_AUDIO_INVALID : enqueue_command(engine, CMD_DESTROY, handle, 0, 0);
}

int sakura_audio_add_child(SakuraAudioEngine *engine, SakuraAudioHandle parent, SakuraAudioHandle child)
{
    Node *parentNode = resolve_node(engine, parent);

    if (parentNode == NULL || parentNode->kind != NODE_MIXER || resolve_node(engine, child) == NULL)
        return SAKURA_AUDIO_INVALID;

    return enqueue_command(engine, CMD_ADD_CHILD, parent, child, 0);
}

int sakura_audio_remove_child(SakuraAudioEngine *engine, SakuraAudioHandle parent, SakuraAudioHandle child)
{
    if (resolve_node(engine, parent) == NULL || resolve_node(engine, child) == NULL)
        return SAKURA_AUDIO_INVALID;

    return enqueue_command(engine, CMD_REMOVE_CHILD, parent, child, 0);
}

// ---------------------------------------------------------------------------------------------
// Engine lifetime

void sakura_audio_config_defaults(SakuraAudioConfig *config)
{
    if (config == NULL)
        return;

    config->sampleRate = 44100;
    config->channels = 2;

    // Voices and mixers together. Sized for the worst case that actually occurs in a rhythm game --
    // dense streams with long hitsound tails over two master mixers -- rather than for an arbitrary
    // round number, and configurable because the cost of being wrong is a failed voice allocation.
    config->maxNodes = 256;
    config->maxBuffers = 256;

    config->maxCommands = 4096;

    // The mix granularity, which is not the device buffer: SDL asks for whatever its buffer needs and
    // this is how finely that request is chopped up. Small enough that a volume change lands within
    // three milliseconds, large enough that per-block overhead is noise.
    config->mixBlockFrames = 128;
}

SakuraAudioEngine *sakura_audio_engine_create(const SakuraAudioConfig *config)
{
    if (config == NULL)
        return NULL;

    if (config->sampleRate <= 0 || config->channels <= 0 || config->channels > SAKURA_AUDIO_MAX_CHANNELS)
        return NULL;

    if (config->maxNodes <= 0 || config->maxNodes > (int)HANDLE_INDEX_MASK || config->maxBuffers <= 0)
        return NULL;

    if (config->maxCommands <= 0 || config->mixBlockFrames <= 0)
        return NULL;

    SakuraAudioEngine *engine = (SakuraAudioEngine *)calloc(1, sizeof(SakuraAudioEngine));

    if (engine == NULL)
        return NULL;

    engine->config = *config;
    engine->nodeCount = config->maxNodes;
    engine->bufferCount = config->maxBuffers;

    size_t blockFloats = (size_t)config->mixBlockFrames * (size_t)config->channels;

    engine->nodes = (Node *)calloc((size_t)engine->nodeCount, sizeof(Node));
    engine->buffers = (Buffer *)calloc((size_t)engine->bufferCount, sizeof(Buffer));
    engine->nodeScratch = (float *)calloc((size_t)engine->nodeCount * blockFloats, sizeof(float));
    engine->mixBuffer = (float *)calloc(blockFloats, sizeof(float));

    engine->commands.capacity = round_up_power_of_two((uint64_t)config->maxCommands);
    engine->commands.slots = (Command *)calloc((size_t)engine->commands.capacity, sizeof(Command));
    engine->commands.ready = (sakura_atomic_u32 *)calloc((size_t)engine->commands.capacity, sizeof(sakura_atomic_u32));

    engine->fftWindow = (float *)calloc(SAKURA_AUDIO_FFT_SIZE, sizeof(float));
    engine->fftReversal = (int *)calloc(SAKURA_AUDIO_FFT_SIZE, sizeof(int));
    engine->twiddleReal = (float *)calloc(SAKURA_AUDIO_FFT_SIZE / 2, sizeof(float));
    engine->twiddleImaginary = (float *)calloc(SAKURA_AUDIO_FFT_SIZE / 2, sizeof(float));

    if (engine->nodes == NULL || engine->buffers == NULL || engine->nodeScratch == NULL || engine->mixBuffer == NULL
        || engine->commands.slots == NULL || engine->commands.ready == NULL || engine->fftWindow == NULL
        || engine->fftReversal == NULL || engine->twiddleReal == NULL || engine->twiddleImaginary == NULL)
    {
        sakura_audio_engine_destroy(engine);
        return NULL;
    }

    // Hann window, matching the managed AudioFft's (N - 1) denominator so the two produce the same
    // magnitudes for the same input.
    for (int i = 0; i < SAKURA_AUDIO_FFT_SIZE; i++)
        engine->fftWindow[i] = (float)(0.5 * (1.0 - cos(2.0 * 3.14159265358979323846 * i / (SAKURA_AUDIO_FFT_SIZE - 1))));

    int bits = 0;

    while ((1 << bits) < SAKURA_AUDIO_FFT_SIZE)
        bits++;

    for (int i = 0; i < SAKURA_AUDIO_FFT_SIZE; i++)
    {
        int reversed = 0;

        for (int bit = 0; bit < bits; bit++)
        {
            if (i & (1 << bit))
                reversed |= 1 << (bits - 1 - bit);
        }

        engine->fftReversal[i] = reversed;
    }

    for (int i = 0; i < SAKURA_AUDIO_FFT_SIZE / 2; i++)
    {
        double angle = -2.0 * 3.14159265358979323846 * i / SAKURA_AUDIO_FFT_SIZE;
        engine->twiddleReal[i] = (float)cos(angle);
        engine->twiddleImaginary[i] = (float)sin(angle);
    }

    engine->root = claim_node(engine, NODE_MIXER);

    if (engine->root == SAKURA_AUDIO_INVALID_HANDLE)
    {
        sakura_audio_engine_destroy(engine);
        return NULL;
    }

    return engine;
}

SakuraAudioHandle sakura_audio_engine_root(SakuraAudioEngine *engine) { return engine == NULL ? SAKURA_AUDIO_INVALID_HANDLE : engine->root; }

void sakura_audio_engine_maintain(SakuraAudioEngine *engine)
{
    if (engine == NULL)
        return;

    // The only place anything is freed. A slot reaches RETIRED only after the audio thread has
    // unlinked it from the graph, so nothing it owns can still be in use.
    for (int i = 0; i < engine->nodeCount; i++)
    {
        Node *node = &engine->nodes[i];

        if (sakura_atomic_load_u32(&node->slotState) != SLOT_RETIRED)
            continue;

        free(node->ring.data);
        node->ring.data = NULL;
        node->ring.capacity = 0;

        sakura_atomic_store_u32(&node->slotState, SLOT_FREE);
    }

    for (int i = 0; i < engine->bufferCount; i++)
    {
        Buffer *buffer = &engine->buffers[i];

        if (sakura_atomic_load_u32(&buffer->slotState) != SLOT_RETIRED)
            continue;

        free(buffer->data);
        buffer->data = NULL;
        buffer->frames = 0;

        sakura_atomic_store_u32(&buffer->slotState, SLOT_FREE);
    }
}

void sakura_audio_engine_destroy(SakuraAudioEngine *engine)
{
    if (engine == NULL)
        return;

    if (engine->nodes != NULL)
    {
        for (int i = 0; i < engine->nodeCount; i++)
            free(engine->nodes[i].ring.data);
    }

    if (engine->buffers != NULL)
    {
        for (int i = 0; i < engine->bufferCount; i++)
            free(engine->buffers[i].data);
    }

    free(engine->nodes);
    free(engine->buffers);
    free(engine->nodeScratch);
    free(engine->mixBuffer);
    free(engine->commands.slots);
    free(engine->commands.ready);
    free(engine->fftWindow);
    free(engine->fftReversal);
    free(engine->twiddleReal);
    free(engine->twiddleImaginary);
    free(engine);
}
