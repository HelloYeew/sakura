// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

// Tests for libsakura-audio. Plain C against the same translation unit the library ships, with no
// test framework, so this runs anywhere the library builds -- which is the point, given that the
// library's whole claim is that it builds and behaves identically on twelve RIDs.
//
// The numbers here are not regression baselines captured from this implementation. They are what the
// managed reference mixer in Sakura.Framework/Audio/SdlEngine produces for the same input, worked out
// from first principles: unity playback is bit-exact, a Hann-windowed bin-centred sine reads back at
// its own amplitude, a peak-hold window is 1024 frames long. If this file and that one disagree, one
// of them is wrong, and that is the whole reason the managed mixer was written first.

#include "../sakura_audio.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

static int failures = 0;
static int checks = 0;
static const char *current_test = "";

#define CHECK(condition)                                                                      \
    do {                                                                                      \
        checks++;                                                                             \
        if (!(condition)) {                                                                   \
            failures++;                                                                       \
            printf("  FAIL %s:%d in %s: %s\n", __FILE__, __LINE__, current_test, #condition); \
        }                                                                                     \
    } while (0)

#define CHECK_NEAR(actual, expected, tolerance)                                                     \
    do {                                                                                            \
        checks++;                                                                                   \
        double difference = fabs((double)(actual) - (double)(expected));                            \
        if (!(difference <= (tolerance))) {                                                          \
            failures++;                                                                             \
            printf("  FAIL %s:%d in %s: %s = %.9g, expected %.9g (+-%.3g)\n",                       \
                   __FILE__, __LINE__, current_test, #actual, (double)(actual), (double)(expected),  \
                   (double)(tolerance));                                                             \
        }                                                                                            \
    } while (0)

#define RUN(test)                        \
    do {                                 \
        current_test = #test;            \
        printf("- %s\n", current_test);  \
        test();                          \
    } while (0)

#define SAMPLE_RATE 44100
#define CHANNELS 2

static int put_calls = 0;
static int put_frames = 0;

static bool fake_put(void *stream, const void *buffer, int lengthBytes)
{
    (void)stream;
    (void)buffer;
    put_calls++;
    put_frames += lengthBytes / (int)(CHANNELS * sizeof(float));
    return true;
}

static SakuraAudioEngine *make_engine(int blockFrames)
{
    SakuraAudioConfig config;
    sakura_audio_config_defaults(&config);
    config.sampleRate = SAMPLE_RATE;
    config.channels = CHANNELS;
    config.mixBlockFrames = blockFrames;

    SakuraAudioEngine *engine = sakura_audio_engine_create(&config);
    CHECK(engine != NULL);
    return engine;
}

// A voice with a static buffer, routed into the root and playing.
static SakuraAudioHandle attach_static_voice(SakuraAudioEngine *engine, const float *pcm, int64_t frames, SakuraAudioHandle *bufferOut)
{
    SakuraAudioHandle buffer = sakura_audio_buffer_create(engine, pcm, frames);
    SakuraAudioHandle voice = sakura_audio_create_voice(engine);

    CHECK(buffer != SAKURA_AUDIO_INVALID_HANDLE);
    CHECK(voice != SAKURA_AUDIO_INVALID_HANDLE);
    CHECK(sakura_audio_voice_set_buffer(engine, voice, buffer) == SAKURA_AUDIO_OK);
    CHECK(sakura_audio_add_child(engine, sakura_audio_engine_root(engine), voice) == SAKURA_AUDIO_OK);
    CHECK(sakura_audio_node_play(engine, voice) == SAKURA_AUDIO_OK);

    if (bufferOut != NULL)
        *bufferOut = buffer;

    return voice;
}

// ---------------------------------------------------------------------------------------------

static void test_abi_and_lifetime(void)
{
    CHECK(sakura_audio_abi_version() == SAKURA_AUDIO_ABI_VERSION);

    SakuraAudioConfig config;
    sakura_audio_config_defaults(&config);

    CHECK(config.channels == 2);
    CHECK(config.maxNodes > 0);

    // Rejected rather than crashed: managed passes a device format through here, and an unopenable
    // device is a plausible source of nonsense.
    config.channels = SAKURA_AUDIO_MAX_CHANNELS + 1;
    CHECK(sakura_audio_engine_create(&config) == NULL);

    config.channels = 2;
    config.sampleRate = 0;
    CHECK(sakura_audio_engine_create(&config) == NULL);

    CHECK(sakura_audio_engine_create(NULL) == NULL);
    sakura_audio_engine_destroy(NULL);

    SakuraAudioEngine *engine = make_engine(128);
    CHECK(sakura_audio_engine_root(engine) != SAKURA_AUDIO_INVALID_HANDLE);
    sakura_audio_engine_destroy(engine);
}

static void test_stale_handle_is_rejected(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    SakuraAudioHandle voice = sakura_audio_create_voice(engine);
    SakuraAudioNodeState state;

    CHECK(sakura_audio_node_get_state(engine, voice, &state) == SAKURA_AUDIO_OK);

    sakura_audio_destroy_node(engine, voice);

    float block[8 * CHANNELS];
    sakura_audio_engine_mix(engine, block, 8); // applies the destroy
    sakura_audio_engine_maintain(engine);      // frees the slot

    // The slot is free and its generation has moved on, so the old handle resolves to nothing rather
    // than to whatever now lives there.
    CHECK(sakura_audio_node_get_state(engine, voice, &state) == SAKURA_AUDIO_INVALID);

    SakuraAudioHandle replacement = sakura_audio_create_voice(engine);
    CHECK(replacement != SAKURA_AUDIO_INVALID_HANDLE);
    CHECK(replacement != voice);
    CHECK(sakura_audio_node_get_state(engine, voice, &state) == SAKURA_AUDIO_INVALID);

    sakura_audio_engine_destroy(engine);
}

// Unity rate and unity gain must be a copy, not an approximation: cubic Hermite at t = 0 collapses
// to the sample itself, so normal playback is not quietly low-passed and needs no bypass path.
static void test_static_playback_is_bit_exact(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    const int frames = 8;
    float pcm[8 * CHANNELS];

    for (int i = 0; i < frames; i++)
    {
        pcm[i * 2] = 0.1f * (float)(i + 1);
        pcm[i * 2 + 1] = -0.1f * (float)(i + 1);
    }

    SakuraAudioHandle voice = attach_static_voice(engine, pcm, frames, NULL);

    float out[16 * CHANNELS];
    CHECK(sakura_audio_engine_mix(engine, out, 16) == 16);

    for (int i = 0; i < frames * CHANNELS; i++)
        CHECK_NEAR(out[i], pcm[i], 0.0);

    // The resampler's four-frame window plays out into silence rather than cutting off mid-sample,
    // and then there is genuinely nothing left.
    for (int i = frames * CHANNELS; i < 16 * CHANNELS; i++)
        CHECK_NEAR(out[i], 0.0f, 0.0);

    SakuraAudioNodeState state;
    CHECK(sakura_audio_node_get_state(engine, voice, &state) == SAKURA_AUDIO_OK);
    CHECK(state.ended == 1);
    CHECK(state.endEpoch == 1);
    CHECK(state.running == 0); // not looping, so the voice stopped itself
    CHECK(state.sourceFrames == frames);

    sakura_audio_engine_destroy(engine);
}

static void test_gain_and_pan(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    const int frames = 4;
    float pcm[4 * CHANNELS];

    for (int i = 0; i < frames * CHANNELS; i++)
        pcm[i] = 0.5f;

    SakuraAudioHandle voice = attach_static_voice(engine, pcm, frames, NULL);

    // Hard right: the left side goes silent, the right keeps unity. Matches the BASS backend's
    // linear ChannelAttribute.Pan.
    CHECK(sakura_audio_node_set_gain(engine, voice, 0.5f, 0.0f, 1.0f) == SAKURA_AUDIO_OK);

    float out[4 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 4);

    for (int i = 0; i < frames; i++)
    {
        CHECK_NEAR(out[i * 2], 0.0f, 0.0);
        CHECK_NEAR(out[i * 2 + 1], 0.25f, 1e-7);
    }

    // Negative volume is clamped rather than inverting the signal.
    CHECK(sakura_audio_node_set_gain(engine, voice, -1.0f, 1.0f, 1.0f) == SAKURA_AUDIO_OK);
    sakura_audio_node_seek(engine, voice, 0);
    sakura_audio_engine_mix(engine, out, 4);
    CHECK_NEAR(out[0], 0.0f, 0.0);

    sakura_audio_engine_destroy(engine);
}

static void test_rate(void)
{
    SakuraAudioEngine *engine = make_engine(256);

    const int frames = 64;
    float pcm[64 * CHANNELS];

    // Scaled to stay inside the output clamp: the mix clamps to +-1 at the end, so a ramp of raw
    // frame indices would come back as a row of ones and prove nothing.
    for (int i = 0; i < frames; i++)
    {
        pcm[i * 2] = 0.01f * (float)i;
        pcm[i * 2 + 1] = 0.01f * (float)i;
    }

    SakuraAudioHandle voice = attach_static_voice(engine, pcm, frames, NULL);
    CHECK(sakura_audio_node_set_rate(engine, voice, 2.0) == SAKURA_AUDIO_OK);

    float out[64 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 64);

    // Double rate lands on every other input frame, and each of those is a window centre, so they
    // come through exactly.
    for (int i = 0; i < 8; i++)
        CHECK_NEAR(out[i * 2], pcm[i * 4], 0.0);

    // Half rate consumes half as many input frames as it produces, plus the three the window was
    // primed with.
    SakuraAudioHandle slow = attach_static_voice(engine, pcm, frames, NULL);
    CHECK(sakura_audio_node_set_rate(engine, slow, 0.5) == SAKURA_AUDIO_OK);
    sakura_audio_engine_mix(engine, out, 32);

    SakuraAudioNodeState state;
    sakura_audio_node_get_state(engine, slow, &state);
    CHECK(state.sourceFrames >= 16 && state.sourceFrames <= 20);

    // Out of range ratios are clamped, not obeyed: a near-zero ratio would spin an enormous number
    // of output frames out of one input frame.
    CHECK(sakura_audio_node_set_rate(engine, slow, 0.0) == SAKURA_AUDIO_OK);
    sakura_audio_engine_mix(engine, out, 32);

    sakura_audio_engine_destroy(engine);
}

static void test_mixer_sums_then_applies_its_own_inserts(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    const int frames = 8;
    float pcm[8 * CHANNELS];

    for (int i = 0; i < frames * CHANNELS; i++)
        pcm[i] = 0.25f;

    SakuraAudioHandle mixer = sakura_audio_create_mixer(engine);
    CHECK(sakura_audio_add_child(engine, sakura_audio_engine_root(engine), mixer) == SAKURA_AUDIO_OK);

    for (int i = 0; i < 2; i++)
    {
        SakuraAudioHandle buffer = sakura_audio_buffer_create(engine, pcm, frames);
        SakuraAudioHandle voice = sakura_audio_create_voice(engine);
        sakura_audio_voice_set_buffer(engine, voice, buffer);
        sakura_audio_add_child(engine, mixer, voice);
        sakura_audio_node_play(engine, voice);
        sakura_audio_buffer_release(engine, buffer);
    }

    // Own volume applied to the sum of the children, which is what keeps a mixer a mixer rather than
    // bookkeeping over a flat device mix.
    CHECK(sakura_audio_node_set_gain(engine, mixer, 0.5f, 1.0f, 1.0f) == SAKURA_AUDIO_OK);

    float out[8 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 8);

    for (int i = 0; i < frames * CHANNELS; i++)
        CHECK_NEAR(out[i], 0.25f, 1e-7); // (0.25 + 0.25) * 0.5

    // An empty mixer contributes nothing at all, rather than a block of silence with its inserts run
    // over it.
    SakuraAudioHandle empty = sakura_audio_create_mixer(engine);
    sakura_audio_add_child(engine, sakura_audio_engine_root(engine), empty);
    sakura_audio_engine_mix(engine, out, 8);

    SakuraAudioNodeState state;
    sakura_audio_node_get_state(engine, empty, &state);
    CHECK_NEAR(state.amplitudeLeft, 0.0f, 0.0);

    sakura_audio_engine_destroy(engine);
}

static void test_output_is_clamped(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    const int frames = 8;
    float pcm[8 * CHANNELS];

    // Decoded audio is not normalised -- a lossy decoder reconstructing a hot master genuinely
    // overshoots -- and summing several such sources overshoots further.
    for (int i = 0; i < frames * CHANNELS; i++)
        pcm[i] = 0.9f;

    for (int i = 0; i < 3; i++)
    {
        SakuraAudioHandle buffer = sakura_audio_buffer_create(engine, pcm, frames);
        SakuraAudioHandle voice = sakura_audio_create_voice(engine);
        sakura_audio_voice_set_buffer(engine, voice, buffer);
        sakura_audio_add_child(engine, sakura_audio_engine_root(engine), voice);
        sakura_audio_node_play(engine, voice);
        sakura_audio_buffer_release(engine, buffer);
    }

    float out[8 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 8);

    for (int i = 0; i < frames * CHANNELS; i++)
        CHECK_NEAR(out[i], 1.0f, 0.0);

    sakura_audio_engine_destroy(engine);
}

static void test_looping_static_voice(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    const int frames = 4;
    float pcm[4 * CHANNELS];

    for (int i = 0; i < frames; i++)
    {
        pcm[i * 2] = (float)(i + 1);
        pcm[i * 2 + 1] = (float)(i + 1);
    }

    SakuraAudioHandle voice = attach_static_voice(engine, pcm, frames, NULL);
    CHECK(sakura_audio_node_set_loop(engine, voice, 1, 0) == SAKURA_AUDIO_OK);

    float out[32 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 32);
    sakura_audio_engine_mix(engine, out, 32);

    SakuraAudioNodeState state;
    sakura_audio_node_get_state(engine, voice, &state);

    // Still running, and it has wrapped more than once. The end is still published each time round,
    // because the managed side raises OnEnd on every loop.
    CHECK(state.running == 1);
    CHECK(state.endEpoch >= 2);
    CHECK(state.ended == 0);

    sakura_audio_engine_destroy(engine);
}

static void test_transport(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    const int frames = 64;
    float pcm[64 * CHANNELS];

    for (int i = 0; i < frames * CHANNELS; i++)
        pcm[i] = 0.5f;

    SakuraAudioHandle voice = attach_static_voice(engine, pcm, frames, NULL);

    float out[8 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 8);

    SakuraAudioNodeState state;
    sakura_audio_node_get_state(engine, voice, &state);
    CHECK(state.sourceFrames > 0);

    // Pause leaves the cursor where it is.
    int64_t paused = state.sourceFrames;
    sakura_audio_node_pause(engine, voice);
    sakura_audio_engine_mix(engine, out, 8);
    sakura_audio_node_get_state(engine, voice, &state);
    CHECK(state.running == 0);
    CHECK(state.sourceFrames == paused);
    CHECK_NEAR(out[0], 0.0f, 0.0);

    // Stop rewinds, matching the BASS backend where Stop rewinds and Pause does not.
    sakura_audio_node_stop(engine, voice);
    sakura_audio_engine_mix(engine, out, 8);
    sakura_audio_node_get_state(engine, voice, &state);
    CHECK(state.running == 0);
    CHECK(state.sourceFrames == 0);

    // Commands are applied in the order they were posted, so this seek wins over the play before it.
    sakura_audio_node_play(engine, voice);
    sakura_audio_node_seek(engine, voice, 32);
    sakura_audio_engine_mix(engine, out, 8);
    sakura_audio_node_get_state(engine, voice, &state);
    CHECK(state.running == 1);
    CHECK(state.sourceFrames >= 32);

    // Seeking past the end clamps rather than running off the buffer.
    sakura_audio_node_seek(engine, voice, 1000);
    sakura_audio_engine_mix(engine, out, 8);
    sakura_audio_node_get_state(engine, voice, &state);
    CHECK(state.sourceFrames == frames);

    sakura_audio_engine_destroy(engine);
}

static void test_filter(void)
{
    SakuraAudioEngine *engine = make_engine(512);

    const int frames = 512;
    static float low[512 * CHANNELS];
    static float high[512 * CHANNELS];

    for (int i = 0; i < frames; i++)
    {
        float lowSample = (float)sin(2.0 * M_PI * 100.0 * i / SAMPLE_RATE);
        float highSample = (float)sin(2.0 * M_PI * 10000.0 * i / SAMPLE_RATE);

        low[i * 2] = low[i * 2 + 1] = lowSample;
        high[i * 2] = high[i * 2 + 1] = highSample;
    }

    // The RBJ low-pass, worked out here rather than taken from the library, so this checks the
    // coefficients are being *applied* correctly. Computing them is the managed side's job -- one
    // home, one set of tests -- and this is the same Q = 0.707 the BASS backend passes as fQ.
    const double cutoff = 1000.0;
    const double q = 0.707;
    double w0 = 2.0 * M_PI * cutoff / SAMPLE_RATE;
    double alpha = sin(w0) / (2.0 * q);
    double a0 = 1.0 + alpha;
    double b0 = (1.0 - cos(w0)) / 2.0 / a0;
    double b1 = (1.0 - cos(w0)) / a0;
    double b2 = b0;
    double a1 = -2.0 * cos(w0) / a0;
    double a2 = (1.0 - alpha) / a0;

    static float out[512 * CHANNELS];

    for (int pass = 0; pass < 2; pass++)
    {
        const float *pcm = pass == 0 ? low : high;

        SakuraAudioHandle voice = attach_static_voice(engine, pcm, frames, NULL);
        CHECK(sakura_audio_node_set_filter(engine, voice, 1, (float)b0, (float)b1, (float)b2, (float)a1, (float)a2) == SAKURA_AUDIO_OK);

        sakura_audio_engine_mix(engine, out, frames);

        // Skip the first few frames: the biquad starts from a cleared delay line.
        double energy = 0;

        for (int i = 64; i < frames; i++)
            energy += (double)out[i * 2] * out[i * 2];

        double rms = sqrt(energy / (frames - 64));

        if (pass == 0)
            CHECK_NEAR(rms, 0.7071, 0.02); // 100 Hz, a decade below cutoff: through untouched
        else
            CHECK(rms < 0.02); // 10 kHz, a decade above: -40 dB from a second-order slope

        sakura_audio_destroy_node(engine, voice);
        sakura_audio_engine_mix(engine, out, 8);
        sakura_audio_engine_maintain(engine);
    }

    // Disabled means bypassed, with no coefficient maths involved at all.
    SakuraAudioHandle voice = attach_static_voice(engine, high, frames, NULL);
    sakura_audio_node_set_filter(engine, voice, 0, (float)b0, (float)b1, (float)b2, (float)a1, (float)a2);

    sakura_audio_engine_mix(engine, out, frames);

    for (int i = 0; i < 32; i++)
        CHECK_NEAR(out[i * 2], high[i * 2], 0.0);

    sakura_audio_engine_destroy(engine);
}

// A bin-centred full-scale sine must read back at its own peak amplitude: that is what the
// 2 / (N * coherentGain) scaling is for, and it is the property the managed AudioFft is pinned to.
static void test_spectrum(void)
{
    SakuraAudioEngine *engine = make_engine(512);

    const int frames = 1024;
    const int bin = 32;
    float *pcm = (float *)malloc((size_t)frames * CHANNELS * sizeof(float));

    for (int i = 0; i < frames; i++)
    {
        float sample = (float)sin(2.0 * M_PI * bin * i / SAKURA_AUDIO_FFT_SIZE);
        pcm[i * 2] = pcm[i * 2 + 1] = sample;
    }

    SakuraAudioHandle voice = attach_static_voice(engine, pcm, frames, NULL);

    static float out[512 * CHANNELS];
    float bins[SAKURA_AUDIO_BINS];

    // Nothing has passed through yet, so there is no window to transform and no spectrum to report.
    CHECK(sakura_audio_node_read_spectrum(engine, voice, bins, SAKURA_AUDIO_BINS) == 0);

    sakura_audio_engine_mix(engine, out, 512);

    CHECK(sakura_audio_node_read_spectrum(engine, voice, bins, SAKURA_AUDIO_BINS) == SAKURA_AUDIO_BINS);
    CHECK_NEAR(bins[bin], 1.0f, 0.05);

    // Hann leaks one bin either side and nothing beyond that worth speaking of.
    CHECK(bins[bin - 1] < 0.55f);
    CHECK(bins[bin + 1] < 0.55f);

    for (int i = 0; i < SAKURA_AUDIO_BINS; i++)
    {
        if (i < bin - 3 || i > bin + 3)
            CHECK(bins[i] < 0.02f);
    }

    // A partial read is honoured, and an oversized one is clamped rather than overrunning.
    CHECK(sakura_audio_node_read_spectrum(engine, voice, bins, 16) == 16);
    CHECK(sakura_audio_node_read_spectrum(engine, voice, bins, SAKURA_AUDIO_BINS * 4) == SAKURA_AUDIO_BINS);
    CHECK(sakura_audio_node_read_spectrum(engine, voice, NULL, 16) == SAKURA_AUDIO_INVALID);

    free(pcm);
    sakura_audio_engine_destroy(engine);
}

// The peak window is measured in audio, not in reads: 1024 frames of history regardless of when the
// reader asks. A peak computed per callback into an atomic reads zero whenever the reader lands
// between callbacks, which is the bug the managed tap had and had fixed.
static void test_peak_metering(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    const int frames = 2048;
    float *pcm = (float *)malloc((size_t)frames * CHANNELS * sizeof(float));

    for (int i = 0; i < frames; i++)
    {
        pcm[i * 2] = 0.5f * (float)sin(2.0 * M_PI * 32 * i / SAKURA_AUDIO_FFT_SIZE);
        pcm[i * 2 + 1] = 0.25f * (float)sin(2.0 * M_PI * 32 * i / SAKURA_AUDIO_FFT_SIZE);
    }

    SakuraAudioHandle voice = attach_static_voice(engine, pcm, frames, NULL);

    float out[128 * CHANNELS];

    for (int i = 0; i < 8; i++)
        sakura_audio_engine_mix(engine, out, 128);

    SakuraAudioNodeState state;
    sakura_audio_node_get_state(engine, voice, &state);
    CHECK_NEAR(state.amplitudeLeft, 0.5f, 0.02);
    CHECK_NEAR(state.amplitudeRight, 0.25f, 0.02);

    // A voice that has stopped keeps its last peaks: the managed channel reports Empty amplitudes
    // for anything not running, so there is nothing here to decay towards.
    for (int i = 0; i < 64; i++)
        sakura_audio_engine_mix(engine, out, 128);

    sakura_audio_node_get_state(engine, voice, &state);
    CHECK(state.running == 0);
    CHECK_NEAR(state.amplitudeLeft, 0.5f, 0.02);

    // A voice that is *still running* but starved does decay, because the silent tail of its block is
    // fed to the tap along with the audio. Freezing here instead is what made the managed
    // visualiser ride between half and full scale where BASS sat pinned near 0 dB.
    SakuraAudioHandle starved = sakura_audio_create_voice(engine);
    sakura_audio_voice_set_stream(engine, starved, 4096);
    sakura_audio_add_child(engine, sakura_audio_engine_root(engine), starved);
    sakura_audio_node_play(engine, starved);
    sakura_audio_stream_write(engine, starved, pcm, 512);

    for (int i = 0; i < 4; i++)
        sakura_audio_engine_mix(engine, out, 128);

    sakura_audio_node_get_state(engine, starved, &state);
    CHECK_NEAR(state.amplitudeLeft, 0.5f, 0.02);

    // The decoder never comes back, and the meter falls to nothing over the length of the peak
    // window rather than holding the last thing it saw.
    for (int i = 0; i < 16; i++)
        sakura_audio_engine_mix(engine, out, 128);

    sakura_audio_node_get_state(engine, starved, &state);
    CHECK(state.running == 1);
    CHECK_NEAR(state.amplitudeLeft, 0.0f, 1e-6);

    free(pcm);
    sakura_audio_engine_destroy(engine);
}

static void test_streaming(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    SakuraAudioHandle voice = sakura_audio_create_voice(engine);
    CHECK(sakura_audio_voice_set_stream(engine, voice, 1024) == SAKURA_AUDIO_OK);

    // Set once: the ring is allocated up front and never resized, so a second call is a caller bug.
    CHECK(sakura_audio_voice_set_stream(engine, voice, 1024) == SAKURA_AUDIO_ERROR);

    CHECK(sakura_audio_add_child(engine, sakura_audio_engine_root(engine), voice) == SAKURA_AUDIO_OK);
    CHECK(sakura_audio_node_play(engine, voice) == SAKURA_AUDIO_OK);

    CHECK(sakura_audio_stream_space(engine, voice) == 1024);
    CHECK(sakura_audio_stream_buffered(engine, voice) == 0);

    float pcm[256 * CHANNELS];

    for (int i = 0; i < 256 * CHANNELS; i++)
        pcm[i] = 0.25f;

    CHECK(sakura_audio_stream_write(engine, voice, pcm, 256) == 256);
    CHECK(sakura_audio_stream_buffered(engine, voice) == 256);
    CHECK(sakura_audio_stream_space(engine, voice) == 768);

    float out[128 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 128);

    CHECK_NEAR(out[0], 0.25f, 1e-7);
    CHECK(sakura_audio_stream_buffered(engine, voice) < 256);

    SakuraAudioNodeState state;
    sakura_audio_node_get_state(engine, voice, &state);
    CHECK(state.sourceFrames >= 128);
    CHECK(state.ended == 0);

    // A ring that filled reports the short write rather than dropping the remainder: dropping it
    // would put a gap in the audio.
    int written = 0;

    for (int i = 0; i < 16; i++)
        written += sakura_audio_stream_write(engine, voice, pcm, 256);

    CHECK(written < 16 * 256);
    CHECK(sakura_audio_stream_space(engine, voice) == 0);

    sakura_audio_engine_destroy(engine);
}

static void test_streaming_starvation_and_end(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    SakuraAudioHandle voice = sakura_audio_create_voice(engine);
    sakura_audio_voice_set_stream(engine, voice, 1024);
    sakura_audio_add_child(engine, sakura_audio_engine_root(engine), voice);
    sakura_audio_node_play(engine, voice);

    float out[128 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 128);
    sakura_audio_engine_mix(engine, out, 128);

    SakuraAudioStats stats;
    sakura_audio_engine_get_stats(engine, &stats);

    // A running voice with an empty ring and a decoder that has not reported EOF is a decoder that
    // has fallen behind, and it says so rather than pretending the track ended.
    CHECK(stats.starvations > 0);

    SakuraAudioNodeState state;
    sakura_audio_node_get_state(engine, voice, &state);
    CHECK(state.ended == 0);
    CHECK(state.endEpoch == 0);
    CHECK(state.running == 1);
    CHECK_NEAR(out[0], 0.0f, 0.0);

    // Drained *and* empty is the end. Drained with audio still buffered is not.
    float pcm[64 * CHANNELS];

    for (int i = 0; i < 64 * CHANNELS; i++)
        pcm[i] = 0.5f;

    sakura_audio_stream_write(engine, voice, pcm, 64);
    CHECK(sakura_audio_stream_set_drained(engine, voice, 1) == SAKURA_AUDIO_OK);

    sakura_audio_engine_mix(engine, out, 128);
    sakura_audio_node_get_state(engine, voice, &state);

    CHECK(state.ended == 1);
    CHECK(state.endEpoch == 1);
    CHECK(state.running == 0);

    sakura_audio_engine_destroy(engine);
}

// A discard cannot be done by the writer, so the writer posts it and the audio thread performs it.
// Until it has, the writer must not write, or the new position's audio is thrown away with the old.
static void test_stream_flush_protocol(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    SakuraAudioHandle voice = sakura_audio_create_voice(engine);
    sakura_audio_voice_set_stream(engine, voice, 1024);
    sakura_audio_add_child(engine, sakura_audio_engine_root(engine), voice);
    sakura_audio_node_play(engine, voice);

    float stale[512 * CHANNELS];

    for (int i = 0; i < 512 * CHANNELS; i++)
        stale[i] = 0.5f;

    sakura_audio_stream_write(engine, voice, stale, 512);
    sakura_audio_stream_set_drained(engine, voice, 1);

    float out[128 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 128);

    CHECK(sakura_audio_stream_flush_pending(engine, voice) == 0);
    CHECK(sakura_audio_stream_flush_begin(engine, voice) == SAKURA_AUDIO_OK);
    CHECK(sakura_audio_stream_flush_pending(engine, voice) == 1);

    // While pending, writes are refused rather than silently discarded a moment later.
    CHECK(sakura_audio_stream_write(engine, voice, stale, 64) == 0);
    CHECK(sakura_audio_stream_space(engine, voice) == 0);

    sakura_audio_engine_mix(engine, out, 128);

    CHECK(sakura_audio_stream_flush_pending(engine, voice) == 0);
    CHECK(sakura_audio_stream_buffered(engine, voice) == 0);

    // The flush also clears the drained flag: a seek means the decoder is going back to work.
    SakuraAudioNodeState state;
    sakura_audio_node_get_state(engine, voice, &state);
    CHECK(state.ended == 0);

    float fresh[128 * CHANNELS];

    for (int i = 0; i < 128 * CHANNELS; i++)
        fresh[i] = -0.75f;

    CHECK(sakura_audio_stream_write(engine, voice, fresh, 128) == 128);
    sakura_audio_engine_mix(engine, out, 8);

    // What comes out is the new position, not a fragment of the old one.
    CHECK_NEAR(out[0], -0.75f, 1e-7);

    sakura_audio_engine_destroy(engine);
}

static void test_node_pool_is_bounded_and_recycled(void)
{
    SakuraAudioConfig config;
    sakura_audio_config_defaults(&config);
    config.sampleRate = SAMPLE_RATE;
    config.channels = CHANNELS;
    config.mixBlockFrames = 64;
    config.maxNodes = 8;
    config.maxBuffers = 2;

    SakuraAudioEngine *engine = sakura_audio_engine_create(&config);
    CHECK(engine != NULL);

    SakuraAudioHandle voices[8];
    int created = 0;

    for (int i = 0; i < 8; i++)
    {
        voices[i] = sakura_audio_create_voice(engine);

        if (voices[i] != SAKURA_AUDIO_INVALID_HANDLE)
            created++;
    }

    // Seven, not eight: the root mixer holds the first slot. A preallocated pool refuses rather
    // than allocating on the audio path.
    CHECK(created == 7);
    CHECK(sakura_audio_create_voice(engine) == SAKURA_AUDIO_INVALID_HANDLE);

    float out[64 * CHANNELS];

    CHECK(sakura_audio_destroy_node(engine, voices[0]) == SAKURA_AUDIO_OK);
    sakura_audio_engine_mix(engine, out, 64);
    sakura_audio_engine_maintain(engine);

    CHECK(sakura_audio_create_voice(engine) != SAKURA_AUDIO_INVALID_HANDLE);

    // The root is not destroyable; the whole graph hangs off it.
    CHECK(sakura_audio_destroy_node(engine, sakura_audio_engine_root(engine)) == SAKURA_AUDIO_INVALID);

    // Buffers are a separate, equally bounded pool.
    float pcm[4 * CHANNELS] = {0};
    SakuraAudioHandle first = sakura_audio_buffer_create(engine, pcm, 4);
    SakuraAudioHandle second = sakura_audio_buffer_create(engine, pcm, 4);
    CHECK(first != SAKURA_AUDIO_INVALID_HANDLE);
    CHECK(second != SAKURA_AUDIO_INVALID_HANDLE);
    CHECK(sakura_audio_buffer_create(engine, pcm, 4) == SAKURA_AUDIO_INVALID_HANDLE);

    CHECK(sakura_audio_buffer_release(engine, first) == SAKURA_AUDIO_OK);
    sakura_audio_engine_maintain(engine);
    CHECK(sakura_audio_buffer_create(engine, pcm, 4) != SAKURA_AUDIO_INVALID_HANDLE);

    sakura_audio_engine_destroy(engine);
}

// The PCM behind a sample outlives the caller's reference: every voice playing it holds one, which
// is what stops an eviction from freeing audio out from under a playing hitsound.
static void test_buffer_reference_counting(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    const int frames = 8;
    float pcm[8 * CHANNELS];

    for (int i = 0; i < frames * CHANNELS; i++)
        pcm[i] = 0.5f;

    SakuraAudioHandle buffer = sakura_audio_buffer_create(engine, pcm, frames);
    SakuraAudioHandle voice = sakura_audio_create_voice(engine);

    sakura_audio_voice_set_buffer(engine, voice, buffer);
    sakura_audio_add_child(engine, sakura_audio_engine_root(engine), voice);
    sakura_audio_node_play(engine, voice);

    // The loader is done with it; the voice is not.
    CHECK(sakura_audio_buffer_release(engine, buffer) == SAKURA_AUDIO_OK);
    sakura_audio_engine_maintain(engine);

    float out[8 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 8);
    CHECK_NEAR(out[0], 0.5f, 1e-7);

    // Once the voice is gone the last reference goes with it, and the slot comes back.
    sakura_audio_destroy_node(engine, voice);
    sakura_audio_engine_mix(engine, out, 8);
    sakura_audio_engine_maintain(engine);

    CHECK(sakura_audio_buffer_release(engine, buffer) == SAKURA_AUDIO_INVALID);

    sakura_audio_engine_destroy(engine);
}

static void test_invalid_arguments(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    SakuraAudioNodeState state;
    float bins[SAKURA_AUDIO_BINS];
    float out[8 * CHANNELS];

    CHECK(sakura_audio_node_get_state(engine, 12345, &state) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_node_get_state(NULL, 1, &state) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_node_read_spectrum(engine, 12345, bins, 16) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_node_set_gain(engine, 12345, 1, 1, 1) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_node_play(engine, 12345) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_engine_mix(engine, NULL, 8) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_engine_mix(engine, out, 0) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_engine_get_stats(engine, NULL) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_buffer_create(engine, NULL, 0) == SAKURA_AUDIO_INVALID_HANDLE);

    // Streaming entry points reject a voice that has no ring, rather than reading a null pointer.
    SakuraAudioHandle voice = sakura_audio_create_voice(engine);
    CHECK(sakura_audio_stream_write(engine, voice, out, 4) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_stream_buffered(engine, voice) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_stream_flush_begin(engine, voice) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_stream_flush_pending(engine, voice) == 0);

    // A voice cannot be a parent, and a mixer cannot have a source.
    SakuraAudioHandle mixer = sakura_audio_create_mixer(engine);
    CHECK(sakura_audio_add_child(engine, voice, mixer) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_voice_set_stream(engine, mixer, 128) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_voice_set_buffer(engine, mixer, SAKURA_AUDIO_INVALID_HANDLE) == SAKURA_AUDIO_INVALID);

    sakura_audio_engine_destroy(engine);
}

// The device callback path, exercised without a device: the injected put function stands in for
// SDL_PutAudioStreamData, which is the only thing this library wants from SDL.
static void test_device_callback(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    const int frames = 512;
    static float pcm[512 * CHANNELS];

    for (int i = 0; i < frames * CHANNELS; i++)
        pcm[i] = 0.5f;

    attach_static_voice(engine, pcm, frames, NULL);

    void (*callback)(void *, void *, int, int) = (void (*)(void *, void *, int, int))sakura_audio_get_stream_callback();
    CHECK(callback != NULL);

    put_calls = 0;
    put_frames = 0;
    sakura_audio_set_sdl_put(fake_put);

    // 512 frames asked for in one go, answered in 128-frame blocks.
    callback(engine, NULL, 512 * CHANNELS * (int)sizeof(float), 512 * CHANNELS * (int)sizeof(float));

    CHECK(put_calls == 4);
    CHECK(put_frames == 512);

    SakuraAudioStats stats;
    sakura_audio_engine_get_stats(engine, &stats);
    CHECK(stats.callbacks == 1);
    CHECK(stats.framesMixed == 512);
    CHECK(stats.putFailures == 0);
    CHECK(stats.commandsDropped == 0);
    CHECK(stats.activeVoices == 1);
    CHECK(stats.callbackMicroseconds >= 0);

    // A request for nothing, and a callback with no engine, are both no-ops rather than crashes.
    callback(engine, NULL, 0, 0);
    callback(NULL, NULL, 512, 512);
    CHECK(put_calls == 4);

    // With no put function installed the mix still runs and the failure is counted, which is what
    // makes a missing handshake visible instead of silent.
    sakura_audio_set_sdl_put(NULL);
    callback(engine, NULL, 128 * CHANNELS * (int)sizeof(float), 128 * CHANNELS * (int)sizeof(float));
    sakura_audio_engine_get_stats(engine, &stats);
    CHECK(stats.putFailures == 1);

    sakura_audio_set_sdl_put(fake_put);
    sakura_audio_engine_destroy(engine);
}

int main(void)
{
    printf("sakura-audio tests (ABI %d)\n", sakura_audio_abi_version());

    RUN(test_abi_and_lifetime);
    RUN(test_stale_handle_is_rejected);
    RUN(test_static_playback_is_bit_exact);
    RUN(test_gain_and_pan);
    RUN(test_rate);
    RUN(test_mixer_sums_then_applies_its_own_inserts);
    RUN(test_output_is_clamped);
    RUN(test_looping_static_voice);
    RUN(test_transport);
    RUN(test_filter);
    RUN(test_spectrum);
    RUN(test_peak_metering);
    RUN(test_streaming);
    RUN(test_streaming_starvation_and_end);
    RUN(test_stream_flush_protocol);
    RUN(test_node_pool_is_bounded_and_recycled);
    RUN(test_buffer_reference_counting);
    RUN(test_invalid_arguments);
    RUN(test_device_callback);

    printf("\n%d checks, %d failures\n", checks, failures);
    return failures == 0 ? 0 : 1;
}
