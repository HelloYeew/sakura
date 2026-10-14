// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#include "sakura_audio.h"
#include "sakura_atomic.h"

#include <limits.h>
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

// Fields written by different threads are kept at least this far apart, so a store on one thread does
// not evict a line another thread is reading. 128 rather than 64: Apple silicon's lines are 128 bytes,
// and x86's adjacent-line prefetcher fetches in pairs, so 64 would still share on both.
#define CACHE_LINE_BYTES 128

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

// The most audio handed to SDL in one put. A device callback asks for its whole period at once, and
// every put takes SDL's stream lock and walks its queue, so the period is mixed block by block into
// one buffer and handed over in a single call. Only a request larger than this is split.
#define PUT_CHUNK_FRAMES 4096

// Biquad state below this is flushed to zero once per block. Well under anything audible (-300 dB),
// and well above the subnormal range, where x86 takes a microcode assist on every multiply. A filter
// left running on a silent input otherwise decays into exactly that range and stays there.
#define FILTER_STATE_FLOOR 1e-15

// ---------------------------------------------------------------------------------------------
// Ring buffer
//
// SPSC, one writer thread per voice, drained by the audio thread. Positions are monotonic 64-bit
// counters reduced modulo the capacity, so neither side ever has to reason about wraparound, and the
// capacity is a whole number of frames, so a frame never straddles the end of the storage and the
// audio thread can read straight out of it.
//
// The discard-on-seek protocol is the only interesting part. The writer cannot move the read cursor
// -- it does not know where the audio thread is, and moving it under a callback that is mid-copy is
// how a seek becomes a click -- so the writer posts a flush (a target position and an epoch bump)
// and the audio thread performs it, jumping its read cursor to the target. Until it has, the writer
// must not write: sakura_audio_stream_flush_pending is what it waits on.
//
// The two positions are the classic acquire/release pairing: each side publishes its own position
// with a release store once it is done with the floats behind it, and reads the other side's with an
// acquire load before touching any. The epochs and the flush target stay sequentially consistent;
// they move once per seek.

typedef struct Ring
{
    float *data;
    uint64_t capacity; // floats, a whole number of frames

    // Writer-owned. The decode thread stores to these on every write.
    sakura_atomic_u64 writePosition;
    sakura_atomic_u64 flushTarget;
    sakura_atomic_u32 writeEpoch;
    sakura_atomic_u32 drained;

    char writerPadding[CACHE_LINE_BYTES];

    // Audio-thread-owned. On a line of their own, so the writer's stores above never evict the cursor
    // the callback is advancing.
    sakura_atomic_u64 readPosition;
    sakura_atomic_u32 readEpoch;
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
// callback once a flush has been posted, playing or not -- a paused voice's seek has to complete too.
static int ring_sync(Ring *ring)
{
    uint32_t writeEpoch = sakura_atomic_load_u32(&ring->writeEpoch);

    if (writeEpoch == sakura_atomic_load_u32(&ring->readEpoch))
        return 0;

    sakura_atomic_store_u64(&ring->readPosition, sakura_atomic_load_u64(&ring->flushTarget));
    sakura_atomic_store_u32(&ring->readEpoch, writeEpoch);
    return 1;
}

// Writer thread.
static int ring_write(Ring *ring, const float *source, int count)
{
    if (count <= 0 || ring_flush_pending(ring))
        return 0;

    uint64_t write = sakura_atomic_load_u64_relaxed(&ring->writePosition); // our own
    uint64_t read = sakura_atomic_load_u64_acquire(&ring->readPosition);   // pairs with ring_consume
    uint64_t free = ring->capacity - (write - read);

    int writable = (uint64_t)count < free ? count : (int)free;

    if (writable <= 0)
        return 0;

    uint64_t offset = write % ring->capacity;
    uint64_t firstChunk = ring->capacity - offset;

    if (firstChunk > (uint64_t)writable)
        firstChunk = (uint64_t)writable;

    memcpy(ring->data + offset, source, (size_t)firstChunk * sizeof(float));

    if ((uint64_t)writable > firstChunk)
        memcpy(ring->data, source + firstChunk, (size_t)((uint64_t)writable - firstChunk) * sizeof(float));

    // Release: the floats above are visible to the audio thread before the position that covers them.
    sakura_atomic_store_u64_release(&ring->writePosition, write + (uint64_t)writable);
    return writable;
}

// Audio thread: the frames that can be read in one contiguous run, without consuming them.
static int ring_peek(const Ring *ring, int channels, const float **frames)
{
    uint64_t read = sakura_atomic_load_u64_relaxed(&ring->readPosition); // our own
    uint64_t available = sakura_atomic_load_u64_acquire(&ring->writePosition) - read; // pairs with ring_write

    uint64_t offset = read % ring->capacity;
    uint64_t contiguous = ring->capacity - offset;

    if (available > contiguous)
        available = contiguous;

    *frames = ring->data + offset;
    return (int)(available / (uint64_t)channels);
}

// Audio thread. Release: the writer may reuse these floats only once it sees the new position.
static void ring_consume(Ring *ring, int floats)
{
    uint64_t read = sakura_atomic_load_u64_relaxed(&ring->readPosition);
    sakura_atomic_store_u64_release(&ring->readPosition, read + (uint64_t)floats);
}

// ---------------------------------------------------------------------------------------------
// Nodes

typedef struct Buffer
{
    sakura_atomic_u32 slotState;
    uint32_t generation;
    sakura_atomic_u64 references;
    float *data;
    int64_t frames;
} Buffer;

// Grouped by which thread writes each field, with padding between the groups, so that the control
// thread setting a volume, the decode thread advancing its ring and the device callback advancing its
// cursor never contend for one cache line. The spectrum capture, six kilobytes per node, lives in its
// own array (see SakuraAudioEngine.captureStorage): inline, it made every node 6.8 KB and spread the
// handful of fields the callback actually touches across that much memory.
typedef struct Node
{
    // --- Identity and graph. Set when the slot is claimed and by graph commands.
    sakura_atomic_u32 slotState;
    uint32_t generation;
    int kind;

    // Graph links, owned by the audio thread and only ever mutated by a command.
    SakuraAudioHandle firstChild;
    SakuraAudioHandle nextSibling;
    SakuraAudioHandle parent;

    // --- Parameters. Written by control threads as single atomic stores and read once per block, so
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

    // Metering runs while engine frames mixed are below this; read_spectrum pushes it a second ahead.
    // Written by whichever thread reads the spectrum, so it sits with the other foreign writes.
    sakura_atomic_u64 meterArmedUntil;

    char parameterPadding[CACHE_LINE_BYTES];

    // --- Source. The ring pads its writer half from its reader half itself.
    sakura_atomic_u32 sourceKind;
    Ring ring;

    char sourcePadding[CACHE_LINE_BYTES];

    // --- Audio-thread-owned state.
    sakura_atomic_u32 running;

    SakuraAudioHandle buffer;
    Buffer *bufferData; // resolved once when the buffer is set; the voice's reference keeps it alive
    int64_t cursor;       // static sources, in frames
    int64_t streamFrames; // streaming sources: frames pulled since the last seek

    float resamplerWindow[RESAMPLER_WINDOW_FRAMES * SAKURA_AUDIO_MAX_CHANNELS];
    double resamplerPosition;
    int resamplerPrimed;
    int resamplerDrainedFrames;
    double filterState[SAKURA_AUDIO_MAX_CHANNELS * 2];

    // The per-side gain the last block ended on. A change ramps from here across the next block rather
    // than stepping. A node that was not mixed in the block just gone -- stopped, starved of children,
    // detached -- or that has just seeked snaps instead (gainPrimed cleared), so nothing ramps from a
    // gain that belonged to audio no longer playing: a bus muted while it had nothing to mix must not
    // let the first hit after it through for a block.
    float appliedLeft;
    float appliedRight;
    int gainPrimed;
    int64_t mixedUntil; // engine frames mixed at the end of the last block this node was mixed in

    int endHandled;
    int meterActive;

    // Published state.
    sakura_atomic_u64 sourceFrames;
    sakura_atomic_u64 endEpoch;
    sakura_atomic_u64 seekEpoch;
    sakura_atomic_u32 ended;

    // Metering.
    float *capture; // CAPTURE_SLOTS windows of SAKURA_AUDIO_FFT_SIZE floats
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

// ---------------------------------------------------------------------------------------------
// Command queue
//
// Bounded MPSC. A slot is reserved by compare-and-swap on the write sequence, and only while the queue
// has room, so several control threads can post without a lock and a full queue refuses the newest
// command instead of overwriting an unconsumed one. In practice the framework posts from one update
// thread, and the multi-producer tolerance is there so that a sample played from a loader thread is
// not a data race.
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

    float *nodeScratch;    // nodeCount blocks, carved up across the nodes
    float *captureStorage; // nodeCount * CAPTURE_SLOTS spectrum windows, carved up the same way
    float *putBuffer;      // what is handed to SDL, putChunkFrames long
    int putChunkFrames;

    // FFT tables, built once at create time so a spectrum read does no trigonometry.
    float *fftWindow;
    int *fftReversal;
    float *twiddleReal;
    float *twiddleImaginary;

    // Bumped by a writer posting a ring flush; the audio thread only walks the pool for flushes to
    // apply when it has moved. Without it every callback scanned every node for the one flush a seek
    // posts every few seconds.
    sakura_atomic_u32 flushGeneration;
    uint32_t flushGenerationSeen; // audio-thread-owned

    // Bumped whenever a node or buffer slot is retired; maintain only walks the pools when it has
    // moved. maintain is called from the manager's 1000 Hz audio update, almost always for nothing.
    sakura_atomic_u32 retireGeneration;
    uint32_t retireGenerationSeen; // owned by the thread that calls maintain

    sakura_atomic_u64 statCallbacks;
    sakura_atomic_u64 statFramesMixed;
    sakura_atomic_u64 statStarvations;
    sakura_atomic_u64 statPutFailures;
    sakura_atomic_u64 statCommandsDropped;
    sakura_atomic_u64 statCallbackMicroseconds;
    sakura_atomic_u32 statActiveVoices;

    int64_t framesMixed;       // audio-thread-owned copy of statFramesMixed, for the metering check
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
#ifdef SAKURA_AUDIO_HAVE_TIMESPEC_GET
    // timespec_get is C11 and needs no platform header, which is the whole reason it is used here
    // rather than clock_gettime or QueryPerformanceCounter. It is the one thing on the audio path
    // that is not pure arithmetic; it exists because a callback whose cost is not measured is a
    // callback whose latency budget is guesswork.
    struct timespec time;

    if (timespec_get(&time, TIME_UTC) == 0)
        return 0;

    return (int64_t)time.tv_sec * 1000000 + time.tv_nsec / 1000;
#else
    // Detected by CMake rather than assumed: bionic only declares timespec_get from API 29, and the
    // Android legs target 21 to match the shipped FFmpeg. Giving up the measurement is the right
    // trade -- reaching for clock_gettime or QueryPerformanceCounter instead would put a platform
    // branch on the audio path, which is the thing this library does not have.
    return 0;
#endif
}

static Node *resolve_node(SakuraAudioEngine *engine, SakuraAudioHandle handle)
{
    if (engine == NULL || handle == SAKURA_AUDIO_INVALID_HANDLE)
        return NULL;

    uint32_t index = handle_index(handle);

    if (index >= (uint32_t)engine->nodeCount)
        return NULL;

    Node *node = &engine->nodes[index];

    // Acquire: pairs with the store that made the slot live, so the initialisation is visible.
    if (sakura_atomic_load_u32_acquire(&node->slotState) != SLOT_LIVE)
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

    if (sakura_atomic_load_u32_acquire(&buffer->slotState) != SLOT_LIVE)
        return NULL;

    return buffer->generation == handle_generation(handle) ? buffer : NULL;
}

static SakuraAudioHandle node_handle(SakuraAudioEngine *engine, Node *node)
{
    return handle_make((uint32_t)(node - engine->nodes), node->generation);
}

// A compare-and-swap increment. The generation counters are bumped from more than one thread -- a slot
// is retired by the audio thread or by whoever drops a buffer's last reference, a flush is posted by
// each voice's own writer -- and two bumps must never collapse into one, or the walk that should
// follow the second is skipped until something else happens to bump it again.
static void generation_bump(sakura_atomic_u32 *generation)
{
    for (;;)
    {
        uint32_t current = sakura_atomic_load_u32(generation);

        if (sakura_atomic_compare_exchange_u32(generation, current, current + 1u) == current)
            return;
    }
}

// Marks a slot for sakura_audio_engine_maintain and tells maintain there is something to collect.
static void retire_slot(SakuraAudioEngine *engine, sakura_atomic_u32 *slotState)
{
    sakura_atomic_store_u32(slotState, SLOT_RETIRED);

    // After the state: a maintain that sees the new generation is guaranteed to see the slot retired.
    generation_bump(&engine->retireGeneration);
}

// Drops one reference. Callable from the audio thread: it never frees, it only marks the slot for
// sakura_audio_engine_maintain to collect.
static void buffer_release_reference(SakuraAudioEngine *engine, Buffer *buffer)
{
    if (buffer == NULL)
        return;

    if (sakura_atomic_add_i64(&buffer->references, -1) <= 0)
        retire_slot(engine, &buffer->slotState);
}

// ---------------------------------------------------------------------------------------------
// Metering
//
// The port of the managed AmplitudeTap, minus the FFT: peaks are trivial and are computed here, the
// transform is deferred to whoever reads the spectrum. An FFT on this thread would be the single
// most expensive thing in the callback and nothing needs it at audio rate.
//
// And only while somebody is reading. Every hitsound voice used to fold every frame into a peak and a
// capture window that nothing ever looked at; now a node meters for a second after each
// read_spectrum, and a node nobody reads costs nothing. That keeps the ABI as it was -- the managed
// reader has always read the spectrum alongside the peaks -- at the price of the very first read
// arming the meter rather than returning data.

static void tap_reset(Node *node)
{
    // The capture windows themselves are not cleared: nothing is read out of one until it has been
    // filled from the start and published, and capturePublished going to zero is what says none is.
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

// Whether this node meters this block. Starting or stopping both begin from a clean tap, so a meter
// that wakes up never reports what it saw a second ago.
static int metering_update(SakuraAudioEngine *engine, Node *node)
{
    int armed = engine->framesMixed < (int64_t)sakura_atomic_load_u64_relaxed(&node->meterArmedUntil);

    if (armed != node->meterActive)
    {
        tap_reset(node);
        node->meterActive = armed;
    }

    return armed;
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
    float *capture = node->capture + (size_t)slot * SAKURA_AUDIO_FFT_SIZE;

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
        capture[write] = (left + right) * 0.5f;

        if (++write == SAKURA_AUDIO_FFT_SIZE)
        {
            // Publish the finished window and move on to the next slot. Three slots means a reader
            // that started copying the window we just published has two more windows -- about 23 ms
            // at 44.1 kHz -- before the writer could come back around to it.
            sakura_atomic_store_u32(&node->capturePublished, (uint32_t)slot + 1u);
            slot = slot + 1 == CAPTURE_SLOTS ? 0 : slot + 1;
            capture = node->capture + (size_t)slot * SAKURA_AUDIO_FFT_SIZE;
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
//
// Read in runs, not frames. A source hands out the longest stretch it can supply contiguously -- the
// rest of a static buffer, or the ring up to its end -- and the resampler takes from that stretch
// with plain memory reads, committing what it took once at the end. That is one ring acquire and one
// release per run instead of a handful of sequentially consistent atomics per frame, and no buffer
// lookup at all: the voice holds a reference, so the pointer resolved when the buffer was set cannot
// go stale under it.

typedef struct SourceRun
{
    uint32_t kind;
    const float *frames;
    int available; // frames in this run
    int taken;     // of which the resampler has used this many
} SourceRun;

static int source_peek(SakuraAudioEngine *engine, Node *node, uint32_t kind, const float **frames)
{
    int channels = engine->config.channels;

    if (kind == SOURCE_STATIC)
    {
        const Buffer *buffer = node->bufferData;

        if (buffer == NULL || node->cursor >= buffer->frames)
            return 0;

        int64_t left = buffer->frames - node->cursor;

        *frames = buffer->data + node->cursor * channels;
        return left > INT_MAX ? INT_MAX : (int)left;
    }

    if (kind == SOURCE_STREAM)
    {
        if (node->ring.data == NULL)
            return 0;

        return ring_peek(&node->ring, channels, frames);
    }

    return 0;
}

static void run_commit(SakuraAudioEngine *engine, Node *node, SourceRun *run)
{
    if (run->taken > 0)
    {
        if (run->kind == SOURCE_STATIC)
        {
            node->cursor += run->taken;
        }
        else if (run->kind == SOURCE_STREAM)
        {
            ring_consume(&node->ring, run->taken * engine->config.channels);
            node->streamFrames += run->taken;
        }
    }

    run->frames = NULL;
    run->available = 0;
    run->taken = 0;
}

// Frames left in the current run, fetching the next one when it is used up.
static int run_contiguous(SakuraAudioEngine *engine, Node *node, SourceRun *run)
{
    if (run->taken < run->available)
        return run->available - run->taken;

    run_commit(engine, node, run);
    run->available = source_peek(engine, node, run->kind, &run->frames);
    return run->available;
}

static int run_pull_frame(SakuraAudioEngine *engine, Node *node, SourceRun *run, float *destination)
{
    if (run_contiguous(engine, node, run) <= 0)
        return 0;

    int channels = engine->config.channels;
    memcpy(destination, run->frames + (size_t)run->taken * channels, (size_t)channels * sizeof(float));
    run->taken++;
    return 1;
}

// The cursor as callers see it: absolute for a static buffer, frames since the last seek for a stream.
static void publish_source_frames(Node *node, uint32_t kind)
{
    sakura_atomic_store_i64(&node->sourceFrames, kind == SOURCE_STATIC ? node->cursor : node->streamFrames);
}

static int source_ended(SakuraAudioEngine *engine, Node *node)
{
    uint32_t kind = sakura_atomic_load_u32(&node->sourceKind);

    if (kind == SOURCE_STATIC)
    {
        const Buffer *buffer = node->bufferData;
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
static int resampler_prime(SakuraAudioEngine *engine, Node *node, SourceRun *run)
{
    int channels = engine->config.channels;

    for (int i = 1; i < RESAMPLER_WINDOW_FRAMES; i++)
    {
        if (!run_pull_frame(engine, node, run, node->resamplerWindow + i * channels))
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

static int resampler_advance(SakuraAudioEngine *engine, Node *node, SourceRun *run)
{
    int channels = engine->config.channels;

    memmove(node->resamplerWindow,
            node->resamplerWindow + channels,
            (size_t)(RESAMPLER_WINDOW_FRAMES - 1) * channels * sizeof(float));

    float *last = node->resamplerWindow + (RESAMPLER_WINDOW_FRAMES - 1) * channels;

    if (run_pull_frame(engine, node, run, last))
    {
        node->resamplerDrainedFrames = 0;
        return 1;
    }

    // Let the tail of the window play out into silence rather than cutting off mid-sample, but stop
    // once the whole window is zeros -- past that there is genuinely nothing left.
    memset(last, 0, (size_t)channels * sizeof(float));

    return ++node->resamplerDrainedFrames < RESAMPLER_WINDOW_FRAMES;
}

// Unity rate with the position on a frame boundary: the interpolation is the identity, so `count`
// frames are a copy. Over the sequence X = w0 w1 w2 w3 s0 s1 ... the per-frame path outputs X[k + 1]
// for frame k and pulls one source frame per output, leaving X[count .. count + 3] in the window --
// which is exactly what this produces, so the two paths can hand over to each other at any frame and
// a rate change mid-block continues from the same window.
static void resampler_copy_run(Node *node, float *restrict destination, const float *restrict source, int count, int channels)
{
    float *window = node->resamplerWindow;
    size_t frameBytes = (size_t)channels * sizeof(float);

    if (count >= 3)
    {
        memcpy(destination, window + channels, 3 * frameBytes);                         // w1 w2 w3
        memcpy(destination + 3 * channels, source, (size_t)(count - 3) * frameBytes);  // s0 ..
        memcpy(window, destination + (size_t)(count - 1) * channels, frameBytes);      // the last frame out
        memcpy(window + channels, source + (size_t)(count - 3) * channels, 3 * frameBytes);
    }
    else
    {
        memcpy(destination, window + channels, (size_t)count * frameBytes);
        memmove(window, window + (size_t)count * channels, (size_t)(RESAMPLER_WINDOW_FRAMES - count) * frameBytes);
        memcpy(window + (size_t)(RESAMPLER_WINDOW_FRAMES - count) * channels, source, (size_t)count * frameBytes);
    }
}

// Every hitsound and every track at normal speed: whole runs copied straight out of the source while
// the position sits on a frame boundary. Returns the new produced count.
static int resampler_unity_runs(SakuraAudioEngine *engine, Node *node, SourceRun *run, float *destination, int produced, int frameCount)
{
    int channels = engine->config.channels;

    while (produced < frameCount && node->resamplerPosition == 0.0)
    {
        int available = run_contiguous(engine, node, run);

        if (available <= 0)
            break;

        int count = available < frameCount - produced ? available : frameCount - produced;

        resampler_copy_run(node, destination + (size_t)produced * channels,
                           run->frames + (size_t)run->taken * channels, count, channels);

        run->taken += count;
        produced += count;
        node->resamplerDrainedFrames = 0;
    }

    return produced;
}

// Stereo at any other rate: the window held in locals and source frames read straight out of the run,
// for as long as the run can supply every frame the next output needs. Computes exactly what the
// per-frame path does -- the same interpolation of the same window at the same position, since
// subtracting whole frames from the position is exact whether done one at a time or at once -- and
// hands back to it at a run boundary or when the source runs dry.
static int resampler_stereo_runs(SakuraAudioEngine *engine, Node *node, SourceRun *run, float *destination, int produced, int frameCount, double ratio)
{
    float *window = node->resamplerWindow;
    float l0 = window[0], r0 = window[1], l1 = window[2], r1 = window[3];
    float l2 = window[4], r2 = window[5], l3 = window[6], r3 = window[7];
    double position = node->resamplerPosition;

    while (produced < frameCount)
    {
        double next = position + ratio;
        int advances = (int)next;
        int available = run->available - run->taken;

        if (advances > available && available == 0)
            available = run_contiguous(engine, node, run);

        if (advances > available)
            break;

        float t = (float)position;
        float *frame = destination + (size_t)produced * 2;

        frame[0] = interpolate(l0, l1, l2, l3, t);
        frame[1] = interpolate(r0, r1, r2, r3, t);
        produced++;

        const float *source = run->frames + (size_t)run->taken * 2;

        for (int i = 0; i < advances; i++)
        {
            l0 = l1; l1 = l2; l2 = l3; l3 = source[i * 2];
            r0 = r1; r1 = r2; r2 = r3; r3 = source[i * 2 + 1];
        }

        if (advances > 0)
        {
            run->taken += advances;
            node->resamplerDrainedFrames = 0;
        }

        position = next - (double)advances;
    }

    window[0] = l0; window[1] = r0; window[2] = l1; window[3] = r1;
    window[4] = l2; window[5] = r2; window[6] = l3; window[7] = r3;
    node->resamplerPosition = position;

    return produced;
}

// One frame the long way: across a run boundary, for a channel count with no fast path, and for the
// window playing out into silence once the source is dry. Returns 0 once the window has drained.
static int resampler_step(SakuraAudioEngine *engine, Node *node, SourceRun *run, float *frame, double ratio)
{
    int channels = engine->config.channels;
    const float *window = node->resamplerWindow;
    float t = (float)node->resamplerPosition;

    for (int channel = 0; channel < channels; channel++)
    {
        frame[channel] = interpolate(window[channel],
                                     window[channels + channel],
                                     window[2 * channels + channel],
                                     window[3 * channels + channel],
                                     t);
    }

    node->resamplerPosition += ratio;

    while (node->resamplerPosition >= 1.0)
    {
        if (!resampler_advance(engine, node, run))
        {
            // Drained: the whole frames this step would have moved past do not exist, so they are not
            // owed either. Left in the position they accumulated one per starved block, and a stream
            // that recovered skipped that many of its first fresh frames -- ~8 ms after a 1 s stall.
            node->resamplerPosition -= floor(node->resamplerPosition);
            return 0;
        }

        node->resamplerPosition -= 1.0;
    }

    return 1;
}

static int resampler_read(SakuraAudioEngine *engine, Node *node, uint32_t kind, float *destination, int frameCount, double ratio)
{
    if (frameCount <= 0)
        return 0;

    int channels = engine->config.channels;

    if (ratio < RATIO_MIN) ratio = RATIO_MIN;
    if (ratio > RATIO_MAX) ratio = RATIO_MAX;

    SourceRun run = { kind, NULL, 0, 0 };
    int produced = 0;

    if (!node->resamplerPrimed && !resampler_prime(engine, node, &run))
    {
        run_commit(engine, node, &run);
        return 0;
    }

    // Runs wherever the source allows, and a single frame the long way wherever it does not, which is
    // also where a dry source plays its window out into silence exactly as before.
    for (;;)
    {
        // Unity only copies from a frame boundary. A voice that went off unity and came back sits at a
        // fractional position for good, so it takes the stereo path rather than the per-frame one.
        if (ratio == 1.0 && node->resamplerPosition == 0.0)
            produced = resampler_unity_runs(engine, node, &run, destination, produced, frameCount);
        else if (channels == 2)
            produced = resampler_stereo_runs(engine, node, &run, destination, produced, frameCount, ratio);

        if (produced >= frameCount)
            break;

        int stillPlaying = resampler_step(engine, node, &run, destination + (size_t)produced * channels, ratio);
        produced++;

        if (!stillPlaying)
            break;
    }

    run_commit(engine, node, &run);
    publish_source_frames(node, kind);
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

    for (int i = 0; i < channels * 2; i++)
    {
        if (fabs(node->filterState[i]) < FILTER_STATE_FLOOR)
            node->filterState[i] = 0.0;
    }
}

// The per-side gain for frame `frame` of a block ramping from `from` to `to`: reaches `to` exactly on
// the last frame, so the next block continues from where this one ended.
static float ramp_gain(float from, float to, int frame, float inverseFrames)
{
    return from + (to - from) * ((float)(frame + 1) * inverseFrames);
}

// block *= gain, in place, for when the metering tap needs to see the post-gain block.
static void gain_in_place(float *block, int frames, int channels, float fromLeft, float fromRight, float toLeft, float toRight)
{
    float inverseFrames = 1.0f / (float)frames;
    int constant = fromLeft == toLeft && fromRight == toRight;

    if (channels == 2)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            float left = constant ? toLeft : ramp_gain(fromLeft, toLeft, frame, inverseFrames);
            float right = constant ? toRight : ramp_gain(fromRight, toRight, frame, inverseFrames);

            block[frame * 2] *= left;
            block[frame * 2 + 1] *= right;
        }
    }
    else
    {
        // Panning is only meaningful in stereo; anything else just takes the volume.
        for (int frame = 0; frame < frames; frame++)
        {
            float gain = constant ? toLeft : ramp_gain(fromLeft, toLeft, frame, inverseFrames);

            for (int channel = 0; channel < channels; channel++)
                block[frame * channels + channel] *= gain;
        }
    }
}

// destination += block * gain, in one pass, for the common case where nothing meters this node.
static void gain_and_sum(const float *restrict block, float *restrict destination, int frames, int channels,
                         float fromLeft, float fromRight, float toLeft, float toRight)
{
    float inverseFrames = 1.0f / (float)frames;

    if (fromLeft == toLeft && fromRight == toRight)
    {
        if (channels == 2)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                destination[frame * 2] += block[frame * 2] * toLeft;
                destination[frame * 2 + 1] += block[frame * 2 + 1] * toRight;
            }
        }
        else
        {
            for (int i = 0; i < frames * channels; i++)
                destination[i] += block[i] * toLeft;
        }

        return;
    }

    if (channels == 2)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            destination[frame * 2] += block[frame * 2] * ramp_gain(fromLeft, toLeft, frame, inverseFrames);
            destination[frame * 2 + 1] += block[frame * 2 + 1] * ramp_gain(fromRight, toRight, frame, inverseFrames);
        }
    }
    else
    {
        for (int frame = 0; frame < frames; frame++)
        {
            float gain = ramp_gain(fromLeft, toLeft, frame, inverseFrames);

            for (int channel = 0; channel < channels; channel++)
                destination[frame * channels + channel] += block[frame * channels + channel] * gain;
        }
    }
}

// Filter, then gain and pan, then metering, then sum into the parent's block. Filtering before gain
// so that automating volume does not change the filter's behaviour; metering after gain so a
// visualiser shows what is audible.
static void apply_inserts_and_mix(SakuraAudioEngine *engine, Node *node, float *restrict block, float *restrict destination, int frames)
{
    int channels = engine->config.channels;

    if (sakura_atomic_load_u32(&node->filterEnabled))
        filter_process(node, block, frames, channels);

    float volume = sakura_atomic_load_f32(&node->volume);
    float targetLeft = volume;
    float targetRight = volume;

    if (channels == 2)
    {
        targetLeft *= sakura_atomic_load_f32(&node->panLeft);
        targetRight *= sakura_atomic_load_f32(&node->panRight);
    }

    if (node->mixedUntil != engine->framesMixed)
        node->gainPrimed = 0;

    node->mixedUntil = engine->framesMixed + frames;

    if (!node->gainPrimed)
    {
        node->appliedLeft = targetLeft;
        node->appliedRight = targetRight;
        node->gainPrimed = 1;
    }

    float fromLeft = node->appliedLeft;
    float fromRight = node->appliedRight;

    node->appliedLeft = targetLeft;
    node->appliedRight = targetRight;

    if (metering_update(engine, node))
    {
        gain_in_place(block, frames, channels, fromLeft, fromRight, targetLeft, targetRight);
        tap_feed(node, block, frames, channels);

        for (int i = 0; i < frames * channels; i++)
            destination[i] += block[i];
    }
    else
    {
        gain_and_sum(block, destination, frames, channels, fromLeft, fromRight, targetLeft, targetRight);
    }
}

// ---------------------------------------------------------------------------------------------
// Transport, applied on the audio thread

// Clears every piece of state that still holds audio from before a discontinuity: the interpolation
// window, the filter's delay line, the gain ramp and the metering capture.
static void reset_playback_state(Node *node)
{
    resampler_reset(node);
    memset(node->filterState, 0, sizeof(node->filterState));
    node->gainPrimed = 0;
    tap_reset(node);

    node->endHandled = 0;
    sakura_atomic_store_u32(&node->ended, 0);
}

// Moves the cursor and clears everything that still holds audio from the old position.
static void seek_internal(SakuraAudioEngine *engine, Node *node, int64_t frame)
{
    (void)engine;

    uint32_t kind = sakura_atomic_load_u32(&node->sourceKind);

    if (kind == SOURCE_STATIC)
    {
        int64_t limit = node->bufferData != NULL ? node->bufferData->frames : 0;

        if (frame < 0) frame = 0;
        if (frame > limit) frame = limit;

        node->cursor = frame;
    }
    else
    {
        // A streaming voice's position is the base the caller established plus what has been pulled
        // since, so seeking resets the count and the caller owns the base. What the decoder does is
        // the caller's business too -- see sakura_audio_node_seek.
        node->streamFrames = 0;
    }

    publish_source_frames(node, kind);
    reset_playback_state(node);

    // Published last, so a caller that has seen the new epoch is guaranteed to see the reset cursor
    // that goes with it rather than the one it replaced.
    sakura_atomic_add_i64(&node->seekEpoch, 1);
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

            Buffer *buffer = node->bufferData;
            node->buffer = SAKURA_AUDIO_INVALID_HANDLE;
            node->bufferData = NULL;
            sakura_atomic_store_u32(&node->sourceKind, SOURCE_NONE);
            sakura_atomic_store_u32(&node->running, 0);
            buffer_release_reference(engine, buffer);

            // Nothing is freed here. The slot, its ring and the PCM it referenced come back on the
            // next sakura_audio_engine_maintain, on a thread that is allowed to call free.
            retire_slot(engine, &node->slotState);
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

            // No gain reset here: a voice that was stopped was not mixed in the last block, which is
            // what makes its first block back snap rather than ramp (see apply_inserts_and_mix).
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
            Buffer *previous = node->bufferData;

            // The reference this voice now holds was claimed by sakura_audio_voice_set_buffer before
            // the command was posted, so the buffer is live and cannot be retired under us.
            node->buffer = command->aux;
            node->bufferData = resolve_buffer(engine, command->aux);
            node->cursor = 0;
            sakura_atomic_store_u32(&node->sourceKind, node->bufferData == NULL ? SOURCE_NONE : SOURCE_STATIC);
            sakura_atomic_store_i64(&node->sourceFrames, 0);

            reset_playback_state(node);
            sakura_atomic_add_i64(&node->seekEpoch, 1);

            buffer_release_reference(engine, previous);
            break;
        }

        default:
            break;
    }
}

static int enqueue_command(SakuraAudioEngine *engine, uint32_t type, SakuraAudioHandle target, SakuraAudioHandle aux, int64_t value)
{
    CommandQueue *queue = &engine->commands;
    uint64_t sequence;

    // Reserve a slot only while there is room. Refusing the newest command is the honest failure: the
    // old version waited for room and then overwrote an unconsumed slot anyway, losing a command it had
    // already accepted -- after up to a million spins on the calling thread. With the default 4096
    // slots drained on every callback this is unreachable short of the audio thread having stopped.
    for (;;)
    {
        sequence = sakura_atomic_load_u64(&queue->writeSequence);

        if (sequence - sakura_atomic_load_u64(&queue->readSequence) >= queue->capacity)
        {
            sakura_atomic_add_i64(&engine->statCommandsDropped, 1);
            return SAKURA_AUDIO_FULL;
        }

        if (sakura_atomic_compare_exchange_u64(&queue->writeSequence, sequence, sequence + 1) == sequence)
            break;
    }

    uint64_t index = sequence & (queue->capacity - 1);

    queue->slots[index].type = type;
    queue->slots[index].target = target;
    queue->slots[index].aux = aux;
    queue->slots[index].value = value;

    sakura_atomic_store_u32(&queue->ready[index], 1);
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
// paused track has to complete, or the writer waits forever. Only walks the pool when a writer has
// posted something since the last walk.
static void sync_streams(SakuraAudioEngine *engine)
{
    uint32_t generation = sakura_atomic_load_u32(&engine->flushGeneration);

    if (generation == engine->flushGenerationSeen)
        return;

    // Recorded before the walk: a flush posted while it is running bumps the generation past this and
    // gets another walk next callback, whether or not this one already caught it.
    engine->flushGenerationSeen = generation;

    for (int i = 0; i < engine->nodeCount; i++)
    {
        Node *node = &engine->nodes[i];

        if (sakura_atomic_load_u32_acquire(&node->slotState) != SLOT_LIVE)
            continue;

        if (sakura_atomic_load_u32(&node->sourceKind) != SOURCE_STREAM || node->ring.data == NULL)
            continue;

        if (!ring_sync(&node->ring))
            continue;

        // The window still holds frames from before the seek, and three interpolated frames of the
        // old position bleeding into the new one is audible as a click. Clearing here as well as on
        // the seek command means the two do not have to arrive in a particular order.
        reset_playback_state(node);
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
        uint32_t kind = sakura_atomic_load_u32(&node->sourceKind);

        if (kind == SOURCE_NONE)
            return;

        double ratio = sakura_atomic_load_f64(&node->rate);
        int produced = resampler_read(engine, node, kind, block, frames, ratio);

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

    engine->framesMixed += frames;

    sakura_atomic_store_u32(&engine->statActiveVoices, (uint32_t)engine->activeVoicesThisBlock);
    sakura_atomic_store_i64(&engine->statFramesMixed, engine->framesMixed);
}

// Mixes `frames` frames as a run of mix blocks into `destination`.
static void mix_frames(SakuraAudioEngine *engine, float *destination, int frames)
{
    int channels = engine->config.channels;
    int written = 0;

    while (written < frames)
    {
        int remaining = frames - written;
        int block = remaining < engine->config.mixBlockFrames ? remaining : engine->config.mixBlockFrames;

        mix_block(engine, destination + (size_t)written * channels, block);
        written += block;
    }
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

    // Mixed block by block, handed over whole: one put per callback, unless the device asked for more
    // than the put buffer holds.
    while (remaining > 0)
    {
        int frames = remaining < engine->putChunkFrames ? remaining : engine->putChunkFrames;

        mix_frames(engine, engine->putBuffer, frames);

        sakura_sdl_put_fn put = sdl_put;

        if (put == NULL || !put(stream, engine->putBuffer, frames * frameBytes))
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

    mix_frames(engine, destination, frames);
    return frames;
}

// ---------------------------------------------------------------------------------------------
// Published state and spectrum

int sakura_audio_node_get_state(SakuraAudioEngine *engine, SakuraAudioHandle handle, SakuraAudioNodeState *state)
{
    Node *node = resolve_node(engine, handle);

    if (node == NULL || state == NULL)
        return SAKURA_AUDIO_INVALID;

    // The epoch before the cursor, and the cursor again if a seek landed in between. The audio thread
    // publishes the reset cursor and then the epoch, so reading in the same order could pair the old
    // cursor with the new epoch -- precisely the jump the epoch exists to let a caller avoid. Bounded:
    // a seek per callback is the most that can land, and losing this race four times running would
    // take a seek landing between each pair of loads.
    int64_t seekEpoch = sakura_atomic_load_i64(&node->seekEpoch);
    int64_t sourceFrames = sakura_atomic_load_i64(&node->sourceFrames);

    for (int attempt = 0; attempt < 4; attempt++)
    {
        int64_t confirmed = sakura_atomic_load_i64(&node->seekEpoch);

        if (confirmed == seekEpoch)
            break;

        seekEpoch = confirmed;
        sourceFrames = sakura_atomic_load_i64(&node->sourceFrames);
    }

    state->sourceFrames = sourceFrames;
    state->seekEpoch = seekEpoch;
    state->endEpoch = sakura_atomic_load_i64(&node->endEpoch);
    state->running = (int32_t)sakura_atomic_load_u32(&node->running);
    state->ended = (int32_t)sakura_atomic_load_u32(&node->ended);

    float peakLeft = 0.0f;
    float peakRight = 0.0f;

    // A node nobody has read the spectrum of recently is not metering, and its peaks are not live.
    if ((int64_t)sakura_atomic_load_u64(&node->meterArmedUntil) > sakura_atomic_load_i64(&engine->statFramesMixed))
    {
        for (int i = 0; i < PEAK_SEGMENT_COUNT; i++)
        {
            float left = sakura_atomic_load_f32(&node->segmentPeakLeft[i]);
            float right = sakura_atomic_load_f32(&node->segmentPeakRight[i]);

            if (left > peakLeft) peakLeft = left;
            if (right > peakRight) peakRight = right;
        }
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

    // Keeps this node metering for another second of audio. A visualiser reads every frame or so; one
    // that stops reading stops costing anything a second later.
    sakura_atomic_store_u64(&node->meterArmedUntil,
                            sakura_atomic_load_u64(&engine->statFramesMixed) + (uint64_t)engine->config.sampleRate);

    uint32_t published = sakura_atomic_load_u32(&node->capturePublished);

    if (published == 0)
        return 0;

    if (binCount > SAKURA_AUDIO_BINS)
        binCount = SAKURA_AUDIO_BINS;

    // On the stack, so this is reentrant and two visualisers on two threads cannot collide.
    float real[SAKURA_AUDIO_FFT_SIZE];
    float imaginary[SAKURA_AUDIO_FFT_SIZE];

    const float *capture = node->capture + (size_t)(published - 1) * SAKURA_AUDIO_FFT_SIZE;

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
    sakura_atomic_store_u32_release(&buffer->slotState, SLOT_LIVE);

    return handle_make((uint32_t)(buffer - engine->buffers), buffer->generation);
}

int sakura_audio_buffer_release(SakuraAudioEngine *engine, SakuraAudioHandle handle)
{
    Buffer *buffer = resolve_buffer(engine, handle);

    if (buffer == NULL)
        return SAKURA_AUDIO_INVALID;

    buffer_release_reference(engine, buffer);
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
        buffer_release_reference(engine, buffer);

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

    // Exactly the frames asked for: a whole number of frames is what lets the audio thread read a run
    // straight out of the storage without a frame ever being split across its end.
    uint64_t capacity = (uint64_t)capacityFrames * (uint64_t)engine->config.channels;

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

    // And only then the engine-wide generation, which is what gets the audio thread to look at all.
    generation_bump(&engine->flushGeneration);

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
    size_t index = (size_t)(node - engine->nodes);

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
    sakura_atomic_store_u64(&node->meterArmedUntil, 0);

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
    node->bufferData = NULL;
    node->cursor = 0;
    node->streamFrames = 0;

    memset(&node->ring, 0, sizeof(node->ring));

    node->capture = engine->captureStorage + index * CAPTURE_SLOTS * SAKURA_AUDIO_FFT_SIZE;
    node->scratch = engine->nodeScratch + index * (size_t)engine->config.mixBlockFrames * (size_t)engine->config.channels;
    node->meterActive = 0;
    node->mixedUntil = -1;

    reset_playback_state(node);

    sakura_atomic_store_i64(&node->sourceFrames, 0);
    sakura_atomic_store_i64(&node->endEpoch, 0);
    sakura_atomic_store_i64(&node->seekEpoch, 0);
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

        // Published last, with release semantics: until this store the audio thread cannot resolve
        // the handle, and once it can, it sees everything above.
        sakura_atomic_store_u32_release(&node->slotState, SLOT_LIVE);

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

    // A whole number of mix blocks, so a full put buffer is never a block cut short.
    engine->putChunkFrames = ((PUT_CHUNK_FRAMES + config->mixBlockFrames - 1) / config->mixBlockFrames) * config->mixBlockFrames;

    engine->nodes = (Node *)calloc((size_t)engine->nodeCount, sizeof(Node));
    engine->buffers = (Buffer *)calloc((size_t)engine->bufferCount, sizeof(Buffer));
    engine->nodeScratch = (float *)calloc((size_t)engine->nodeCount * blockFloats, sizeof(float));
    engine->captureStorage = (float *)calloc((size_t)engine->nodeCount * CAPTURE_SLOTS * SAKURA_AUDIO_FFT_SIZE, sizeof(float));
    engine->putBuffer = (float *)calloc((size_t)engine->putChunkFrames * (size_t)config->channels, sizeof(float));

    engine->commands.capacity = round_up_power_of_two((uint64_t)config->maxCommands);
    engine->commands.slots = (Command *)calloc((size_t)engine->commands.capacity, sizeof(Command));
    engine->commands.ready = (sakura_atomic_u32 *)calloc((size_t)engine->commands.capacity, sizeof(sakura_atomic_u32));

    engine->fftWindow = (float *)calloc(SAKURA_AUDIO_FFT_SIZE, sizeof(float));
    engine->fftReversal = (int *)calloc(SAKURA_AUDIO_FFT_SIZE, sizeof(int));
    engine->twiddleReal = (float *)calloc(SAKURA_AUDIO_FFT_SIZE / 2, sizeof(float));
    engine->twiddleImaginary = (float *)calloc(SAKURA_AUDIO_FFT_SIZE / 2, sizeof(float));

    if (engine->nodes == NULL || engine->buffers == NULL || engine->nodeScratch == NULL || engine->captureStorage == NULL
        || engine->putBuffer == NULL || engine->commands.slots == NULL || engine->commands.ready == NULL || engine->fftWindow == NULL
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

    // Nothing retired since the last walk is the overwhelmingly common case: this is called a
    // thousand times a second and a slot is retired a few times a second at most.
    uint32_t generation = sakura_atomic_load_u32(&engine->retireGeneration);

    if (generation == engine->retireGenerationSeen)
        return;

    // Recorded before the walk, so a slot retired while it runs gets another walk next time.
    engine->retireGenerationSeen = generation;

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
    free(engine->captureStorage);
    free(engine->putBuffer);
    free(engine->commands.slots);
    free(engine->commands.ready);
    free(engine->fftWindow);
    free(engine->fftReversal);
    free(engine->twiddleReal);
    free(engine->twiddleImaginary);
    free(engine);
}
