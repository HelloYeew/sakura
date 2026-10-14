// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

// Benchmark for libsakura-audio. Plain C against the same translation unit the library ships, like the
// tests, and through the public API only, so what it measures is what a caller gets.
//
// Not a test: it asserts nothing and its numbers are machine-specific. It exists so that a change to the
// mix path comes with a before and an after, and so the platforms this machine cannot measure (Windows,
// where the atomics compile differently) can be measured in CI.
//
// Two kinds of figure:
//
//   * throughput: N voices mixed offline for ten seconds of audio, reported as the share of one core
//     that real time would take. That is the number that says whether the mixer keeps up.
//   * control-side calls, warm and cold: what the managed side pays per call from its own threads, and
//     what one device callback costs when the cache has been evicted by everything else the game does --
//     which is the normal state of the audio thread, not the worst case.

#include "../sakura_audio.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#define SAMPLE_RATE 48000
#define CHANNELS 2

// Long enough that no voice ends during a throughput run.
#define SOURCE_FRAMES (SAMPLE_RATE * 30)
#define RUN_SECONDS 10

static double now_seconds(void)
{
#ifdef SAKURA_AUDIO_HAVE_TIMESPEC_GET
    struct timespec time;

    if (timespec_get(&time, TIME_UTC) != 0)
        return (double)time.tv_sec + (double)time.tv_nsec * 1e-9;
#endif

    return (double)clock() / CLOCKS_PER_SEC;
}

static float *source_pcm;

// Larger than any last-level cache this will run on, so walking it evicts the engine's working set.
#define POLLUTE_BYTES ((size_t)64 << 20)
static unsigned char *pollution;

static void evict_caches(void)
{
    for (size_t i = 0; i < POLLUTE_BYTES; i += 64)
        pollution[i]++;
}

static SakuraAudioEngine *make_engine(int maxNodes)
{
    SakuraAudioConfig config;
    sakura_audio_config_defaults(&config);
    config.sampleRate = SAMPLE_RATE;
    config.channels = CHANNELS;

    if (maxNodes > 0)
    {
        config.maxNodes = maxNodes;
        config.maxBuffers = maxNodes;
    }

    return sakura_audio_engine_create(&config);
}

typedef struct Scenario
{
    const char *name;
    int voices;
    int streaming;
    double rate;
    int filter;
    int metered; // read the spectrum every block, as a visualiser would
} Scenario;

static double run_throughput(const Scenario *scenario)
{
    SakuraAudioEngine *engine = make_engine(512);
    SakuraAudioHandle mixer = sakura_audio_create_mixer(engine);
    sakura_audio_add_child(engine, sakura_audio_engine_root(engine), mixer);

    SakuraAudioHandle buffer = sakura_audio_buffer_create(engine, source_pcm, SOURCE_FRAMES);
    SakuraAudioHandle voices[256];

    for (int i = 0; i < scenario->voices; i++)
    {
        voices[i] = sakura_audio_create_voice(engine);

        if (scenario->streaming)
            sakura_audio_voice_set_stream(engine, voices[i], SAMPLE_RATE / 2);
        else
            sakura_audio_voice_set_buffer(engine, voices[i], buffer);

        sakura_audio_node_set_rate(engine, voices[i], scenario->rate);

        // A 1 kHz RBJ low-pass at 48 kHz, Q 0.707.
        if (scenario->filter)
            sakura_audio_node_set_filter(engine, voices[i], 1, 0.0039160f, 0.0078320f, 0.0039160f, -1.8153396f, 0.8310037f);

        sakura_audio_add_child(engine, mixer, voices[i]);
        sakura_audio_node_play(engine, voices[i]);
    }

    enum { BLOCK = 512 };
    static float out[BLOCK * CHANNELS];
    float bins[SAKURA_AUDIO_BINS];
    int64_t written = 0;

    int blocks = SAMPLE_RATE * RUN_SECONDS / BLOCK;
    double started = now_seconds();

    for (int block = 0; block < blocks; block++)
    {
        // The writer's share is counted: on the real path it is the decode thread, but it is the same
        // ring traffic and the same atomics, and leaving it out would flatter the streaming case.
        if (scenario->streaming)
        {
            for (int i = 0; i < scenario->voices; i++)
                sakura_audio_stream_write(engine, voices[i], source_pcm + (written % (SOURCE_FRAMES - BLOCK)) * CHANNELS, BLOCK);

            written += BLOCK;
        }

        if (scenario->metered)
            sakura_audio_node_read_spectrum(engine, scenario->voices > 0 ? voices[0] : mixer, bins, SAKURA_AUDIO_BINS);

        sakura_audio_engine_mix(engine, out, BLOCK);
    }

    double elapsed = now_seconds() - started;

    sakura_audio_engine_destroy(engine);
    return elapsed / RUN_SECONDS * 100.0;
}

static void print_throughput(void)
{
    static const Scenario scenarios[] = {
        { "no voices (fixed cost)", 0, 0, 1.0, 0, 0 },
        { "1 voice, static, rate 1.0", 1, 0, 1.0, 0, 0 },
        { "32 voices, static, rate 1.0", 32, 0, 1.0, 0, 0 },
        { "32 voices, static, rate 1.5", 32, 0, 1.5, 0, 0 },
        { "32 voices, static, rate 1.0, low-pass", 32, 0, 1.0, 1, 0 },
        { "32 voices, static, rate 1.0, one metered", 32, 0, 1.0, 0, 1 },
        { "32 voices, streaming, rate 1.0 (+writer)", 32, 1, 1.0, 0, 0 },
        { "128 voices, static, rate 1.0", 128, 0, 1.0, 0, 0 },
    };

    printf("\nThroughput: %d s of %d Hz stereo, mixed offline in 512-frame blocks\n", RUN_SECONDS, SAMPLE_RATE);
    printf("  %-44s %12s %16s\n", "scenario", "core share", "ns/voice-frame");

    for (size_t i = 0; i < sizeof(scenarios) / sizeof(scenarios[0]); i++)
    {
        double share = run_throughput(&scenarios[i]);
        double perVoiceFrame = scenarios[i].voices > 0
                                   ? share / 100.0 * 1e9 / ((double)SAMPLE_RATE * scenarios[i].voices)
                                   : 0.0;

        printf("  %-44s %11.3f%% %16.2f\n", scenarios[i].name, share, perVoiceFrame);
    }
}

// ---------------------------------------------------------------------------------------------
// Control-side calls

static int put_calls;

static bool counting_put(void *stream, const void *buffer, int lengthBytes)
{
    (void)stream;
    (void)buffer;
    (void)lengthBytes;
    put_calls++;
    return true;
}

typedef void (*stream_callback_fn)(void *, void *, int, int);

static void print_control(void)
{
    // The framework's defaults: 256 nodes and 256 buffers, 128-frame mix blocks.
    SakuraAudioEngine *engine = make_engine(0);
    SakuraAudioHandle mixer = sakura_audio_create_mixer(engine);
    sakura_audio_add_child(engine, sakura_audio_engine_root(engine), mixer);

    SakuraAudioHandle buffer = sakura_audio_buffer_create(engine, source_pcm, SAMPLE_RATE);
    SakuraAudioHandle voices[40];

    // Forty looping hitsound voices: a dense chart's worth, still playing when they are measured.
    for (int i = 0; i < 40; i++)
    {
        voices[i] = sakura_audio_create_voice(engine);
        sakura_audio_voice_set_buffer(engine, voices[i], buffer);
        sakura_audio_node_set_loop(engine, voices[i], 1, 0);
        sakura_audio_add_child(engine, mixer, voices[i]);
        sakura_audio_node_play(engine, voices[i]);
    }

    static float out[4096 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 128);

    enum { WARM = 4000, COLD = 200 };
    double started, warm, cold;
    SakuraAudioNodeState state;

    printf("\nControl side: default config (256 nodes / 256 buffers), 40 looping voices\n");
    printf("  %-52s %10s %10s\n", "call", "warm", "cold");

#define MEASURE(label, body)                                          \
    do {                                                              \
        started = now_seconds();                                      \
        for (int rep = 0; rep < WARM; rep++) { body; }                \
        warm = (now_seconds() - started) / WARM;                      \
        cold = 0;                                                     \
        for (int rep = 0; rep < COLD; rep++)                          \
        {                                                             \
            evict_caches();                                           \
            started = now_seconds();                                  \
            body;                                                     \
            cold += now_seconds() - started;                          \
        }                                                             \
        cold /= COLD;                                                 \
        printf("  %-52s %8.2f us %8.2f us\n", label, warm * 1e6, cold * 1e6); \
    } while (0)

    MEASURE("engine_maintain (called 1000x/s by the manager)", sakura_audio_engine_maintain(engine));
    MEASURE("node_get_state x 40 (PollEvents, 1000x/s)",
            for (int i = 0; i < 40; i++) sakura_audio_node_get_state(engine, voices[i], &state));
    MEASURE("engine_mix, 128 frames", sakura_audio_engine_mix(engine, out, 128));

    // A device callback the size WASAPI's shared mode typically asks for, through the injected put, so
    // the per-put overhead is in the figure.
    stream_callback_fn callback = (stream_callback_fn)sakura_audio_get_stream_callback();
    int requestBytes = 480 * CHANNELS * (int)sizeof(float);
    sakura_audio_set_sdl_put(counting_put);
    put_calls = 0;
    MEASURE("device callback, 480 frames", callback(engine, NULL, requestBytes, requestBytes));
    printf("  %-52s %10.1f\n", "  puts per 480-frame callback", (double)put_calls / (WARM + COLD));
    sakura_audio_set_sdl_put(NULL);

    // What playing one hitsound costs the control side, start to reclaim.
    started = now_seconds();

    for (int rep = 0; rep < WARM; rep++)
    {
        SakuraAudioHandle voice = sakura_audio_create_voice(engine);
        sakura_audio_voice_set_buffer(engine, voice, buffer);
        sakura_audio_add_child(engine, mixer, voice);
        sakura_audio_node_play(engine, voice);
        sakura_audio_destroy_node(engine, voice);
        sakura_audio_engine_mix(engine, out, 1);
        sakura_audio_engine_maintain(engine);
    }

    printf("  %-52s %8.2f us\n", "voice create -> play -> destroy -> reclaim", (now_seconds() - started) / WARM * 1e6);

#undef MEASURE

    sakura_audio_engine_destroy(engine);
}

int main(void)
{
    printf("sakura-audio benchmark (ABI %d)\n", sakura_audio_abi_version());

    source_pcm = (float *)malloc((size_t)SOURCE_FRAMES * CHANNELS * sizeof(float));
    pollution = (unsigned char *)calloc(POLLUTE_BYTES, 1);

    if (source_pcm == NULL || pollution == NULL)
    {
        printf("allocation failed\n");
        return 1;
    }

    // Quiet and decaying-free, so the clamp never engages and no path takes a shortcut on silence.
    for (int i = 0; i < SOURCE_FRAMES; i++)
    {
        float sample = 0.05f * (float)sin(2.0 * 3.14159265358979323846 * 440.0 * i / SAMPLE_RATE);
        source_pcm[i * 2] = sample;
        source_pcm[i * 2 + 1] = -sample;
    }

    print_throughput();
    print_control();

    free(source_pcm);
    free(pollution);
    return 0;
}
