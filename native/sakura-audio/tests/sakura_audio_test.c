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
#include <stddef.h>
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
    int64_t seeksBefore = state.seekEpoch;
    sakura_audio_node_play(engine, voice);
    sakura_audio_node_seek(engine, voice, 32);
    sakura_audio_engine_mix(engine, out, 8);
    sakura_audio_node_get_state(engine, voice, &state);
    CHECK(state.running == 1);
    CHECK(state.sourceFrames >= 32);

    // The seek epoch is what tells a caller its seek has landed. Without it, a position read in the
    // window between posting a seek and the audio thread applying it pairs the old cursor with the
    // new base and reads as a jump.
    CHECK(state.seekEpoch > seeksBefore);

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

    // Metering only runs for a node whose spectrum somebody reads, and a read is what arms it -- which
    // is what the managed reader does on every poll.
    float bins[SAKURA_AUDIO_BINS];
    sakura_audio_node_read_spectrum(engine, voice, bins, SAKURA_AUDIO_BINS);

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
    sakura_audio_node_read_spectrum(engine, starved, bins, SAKURA_AUDIO_BINS);

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

    // 512 frames asked for in one go: mixed in 128-frame blocks, handed to SDL in one put, since every
    // put takes SDL's stream lock.
    callback(engine, NULL, 512 * CHANNELS * (int)sizeof(float), 512 * CHANNELS * (int)sizeof(float));

    CHECK(put_calls == 1);
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
    CHECK(put_calls == 1);

    // Only a request larger than the put buffer (4096 frames) is split.
    put_calls = 0;
    put_frames = 0;
    callback(engine, NULL, 5000 * CHANNELS * (int)sizeof(float), 5000 * CHANNELS * (int)sizeof(float));
    CHECK(put_calls == 2);
    CHECK(put_frames == 5000);

    // With no put function installed the mix still runs and the failure is counted, which is what
    // makes a missing handshake visible instead of silent.
    sakura_audio_set_sdl_put(NULL);
    callback(engine, NULL, 128 * CHANNELS * (int)sizeof(float), 128 * CHANNELS * (int)sizeof(float));
    sakura_audio_engine_get_stats(engine, &stats);
    CHECK(stats.putFailures == 1);

    sakura_audio_set_sdl_put(fake_put);
    sakura_audio_engine_destroy(engine);
}

// The AAudio callback path: the mix lands in the buffer handed over, exactly as sakura_audio_engine_mix
// would have written it, and the callback is counted and timed like SDL's.
static void test_aaudio_callback(void)
{
    SakuraAudioEngine *engine = make_engine(128);
    SakuraAudioEngine *reference = make_engine(128);

    const int frames = 1024;
    static float pcm[1024 * CHANNELS];

    for (int i = 0; i < frames * CHANNELS; i++)
        pcm[i] = (float)sin(i * 0.01) * 0.5f;

    attach_static_voice(engine, pcm, frames, NULL);
    attach_static_voice(reference, pcm, frames, NULL);

    int32_t (*callback)(void *, void *, void *, int32_t) = (int32_t (*)(void *, void *, void *, int32_t))sakura_audio_get_aaudio_callback();
    CHECK(callback != NULL);

    // A burst that isn't a whole number of mix blocks, as AAudio's often aren't.
    static float out[333 * CHANNELS];
    static float expected[333 * CHANNELS];

    put_calls = 0;
    CHECK(callback(NULL, engine, out, 333) == 0);
    sakura_audio_engine_mix(reference, expected, 333);

    CHECK(memcmp(out, expected, sizeof(out)) == 0);
    CHECK(put_calls == 0);

    SakuraAudioStats stats;
    sakura_audio_engine_get_stats(engine, &stats);
    CHECK(stats.callbacks == 1);
    CHECK(stats.framesMixed == 333);
    CHECK(stats.activeVoices == 1);

    // Nothing asked for, nowhere to write or no engine: carry on without touching anything.
    CHECK(callback(NULL, engine, out, 0) == 0);
    CHECK(callback(NULL, engine, NULL, 333) == 0);
    CHECK(callback(NULL, NULL, out, 333) == 0);
    sakura_audio_engine_get_stats(engine, &stats);
    CHECK(stats.callbacks == 1);

    sakura_audio_engine_destroy(reference);
    sakura_audio_engine_destroy(engine);
}

// Core Audio's AudioBuffer and AudioBufferList, as an output audio unit hands them over.
typedef struct
{
    uint32_t channels;
    uint32_t byteSize;
    void *data;
} test_buffer;

typedef struct
{
    uint32_t count;
    test_buffer buffers[1];
} test_buffer_list;

// The audio unit (RemoteIO) callback path: as AAudio's, the mix lands in the buffer handed over, and the callback is
// counted and timed. A buffer too small for what the engine mixes is silenced and marked so, never overrun.
static void test_audio_unit_callback(void)
{
    SakuraAudioEngine *engine = make_engine(128);
    SakuraAudioEngine *reference = make_engine(128);

    const int frames = 1024;
    static float pcm[1024 * CHANNELS];

    for (int i = 0; i < frames * CHANNELS; i++)
        pcm[i] = (float)sin(i * 0.01) * 0.5f;

    attach_static_voice(engine, pcm, frames, NULL);
    attach_static_voice(reference, pcm, frames, NULL);

    typedef int32_t (*render_fn)(void *, uint32_t *, const void *, uint32_t, uint32_t, void *);
    render_fn callback = (render_fn)sakura_audio_get_audio_unit_callback();
    CHECK(callback != NULL);

    // Apple's offsets on its 64-bit platforms (the buffer 8 into the list, its data 8 into the buffer), which the
    // library's own declaration has to match.
    if (sizeof(void *) == 8)
    {
        CHECK(offsetof(test_buffer_list, buffers) == 8);
        CHECK(offsetof(test_buffer, data) == 8);
        CHECK(sizeof(test_buffer_list) == 24);
    }

    // RemoteIO's slice at 48 kHz with a 5 ms buffer, not a whole number of mix blocks.
    static float out[240 * CHANNELS];
    static float expected[240 * CHANNELS];
    test_buffer_list list = { 1, { { CHANNELS, sizeof(out), out } } };
    uint32_t flags = 0;

    CHECK(callback(engine, &flags, NULL, 0, 240, &list) == 0);
    sakura_audio_engine_mix(reference, expected, 240);

    CHECK(memcmp(out, expected, sizeof(out)) == 0);
    CHECK(flags == 0);

    SakuraAudioStats stats;
    sakura_audio_engine_get_stats(engine, &stats);
    CHECK(stats.callbacks == 1);
    CHECK(stats.framesMixed == 240);
    CHECK(stats.activeVoices == 1);

    // A buffer of half what's asked for: zeroed, said to be silence, and nothing mixed.
    for (int i = 0; i < 240 * CHANNELS; i++)
        out[i] = 1.0f;

    test_buffer_list small = { 1, { { CHANNELS, sizeof(out) / 2, out } } };
    CHECK(callback(engine, &flags, NULL, 0, 240, &small) == 0);
    CHECK(flags == (1u << 4));
    CHECK(out[0] == 0.0f && out[240 * CHANNELS / 2 - 1] == 0.0f);
    CHECK(out[240 * CHANNELS / 2] == 1.0f);

    // Nothing asked for, no buffers, nowhere to write or no engine: carry on without touching anything.
    test_buffer_list empty = { 0, { { CHANNELS, sizeof(out), out } } };
    test_buffer_list nowhere = { 1, { { CHANNELS, sizeof(out), NULL } } };
    CHECK(callback(engine, NULL, NULL, 0, 0, &list) == 0);
    CHECK(callback(engine, NULL, NULL, 0, 240, &empty) == 0);
    CHECK(callback(engine, NULL, NULL, 0, 240, &nowhere) == 0);
    CHECK(callback(engine, NULL, NULL, 0, 240, NULL) == 0);
    CHECK(callback(NULL, NULL, NULL, 0, 240, &list) == 0);
    sakura_audio_engine_get_stats(engine, &stats);
    CHECK(stats.callbacks == 1);
    CHECK(stats.framesMixed == 240);

    sakura_audio_engine_destroy(reference);
    sakura_audio_engine_destroy(engine);
}

// Core Audio's AudioTimeStamp, as an output audio unit hands it to its render callback.
typedef struct
{
    double sampleTime;
    uint64_t hostTime;
    double rateScalar;
    uint64_t wordClockTime;
    uint8_t smpteTime[24];
    uint32_t flags;
    uint32_t reserved;
} test_time_stamp;

// The device's clock from the audio unit callback: each callback publishes when its first frame goes out against the
// frames mixed before it, and a sample time that doesn't carry on from the last callback counts as a missed cycle.
static void test_audio_unit_device_time(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    typedef int32_t (*render_fn)(void *, uint32_t *, const void *, uint32_t, uint32_t, void *);
    render_fn callback = (render_fn)sakura_audio_get_audio_unit_callback();

    // Apple's layout: the flags 56 bytes in, 64 bytes in all.
    CHECK(offsetof(test_time_stamp, flags) == 56);
    CHECK(sizeof(test_time_stamp) == 64);

    SakuraAudioDeviceTime time;

    // Nothing published before the first callback, though the count is there.
    CHECK(sakura_audio_engine_get_device_time(engine, &time) == SAKURA_AUDIO_ERROR);
    CHECK(time.discontinuities == 0);
    CHECK(sakura_audio_engine_get_device_time(NULL, &time) == SAKURA_AUDIO_INVALID);
    CHECK(sakura_audio_engine_get_device_time(engine, NULL) == SAKURA_AUDIO_INVALID);

    static float out[240 * CHANNELS];
    test_buffer_list list = { 1, { { CHANNELS, sizeof(out), out } } };
    test_time_stamp stamp = { 0 };
    stamp.flags = 3; // sample time and host time valid

    stamp.sampleTime = 1000;
    stamp.hostTime = 5000;
    CHECK(callback(engine, NULL, &stamp, 0, 240, &list) == 0);

    CHECK(sakura_audio_engine_get_device_time(engine, &time) == SAKURA_AUDIO_OK);
    CHECK(time.framesMixed == 0);
    CHECK(time.hostTime == 5000);
    CHECK(time.discontinuities == 0);

    // The next cycle, carrying straight on.
    stamp.sampleTime = 1240;
    stamp.hostTime = 6000;
    callback(engine, NULL, &stamp, 0, 240, &list);

    CHECK(sakura_audio_engine_get_device_time(engine, &time) == SAKURA_AUDIO_OK);
    CHECK(time.framesMixed == 240);
    CHECK(time.hostTime == 6000);
    CHECK(time.discontinuities == 0);

    // A cycle skipped: the sample time is a buffer further on than where the last one ended.
    stamp.sampleTime = 1720;
    stamp.hostTime = 8000;
    callback(engine, NULL, &stamp, 0, 240, &list);

    CHECK(sakura_audio_engine_get_device_time(engine, &time) == SAKURA_AUDIO_OK);
    CHECK(time.framesMixed == 480);
    CHECK(time.discontinuities == 1);

    // A timestamp without a host time leaves the last one published; one without a sample time isn't judged.
    stamp.flags = 1;
    stamp.sampleTime = 1960;
    stamp.hostTime = 9000;
    callback(engine, NULL, &stamp, 0, 240, &list);
    stamp.flags = 0;
    stamp.sampleTime = 50000;
    callback(engine, NULL, &stamp, 0, 240, &list);

    CHECK(sakura_audio_engine_get_device_time(engine, &time) == SAKURA_AUDIO_OK);
    CHECK(time.hostTime == 8000);
    CHECK(time.framesMixed == 480);
    CHECK(time.discontinuities == 1);

    sakura_audio_engine_destroy(engine);
}

// ---------------------------------------------------------------------------------------------
// WASAPI, against fake COM objects: a vtable pointer first, methods in audioclient.h's slots, and a device model that
// plays `period` frames between events.

typedef void (*test_com_method)(void);

typedef struct
{
    const test_com_method *vtable;
} test_com_object;

#define FAKE_WASAPI_MAX_PLAYED (1 << 19)
#define FAKE_WASAPI_MAX_CALLS 64
#define FAKE_WASAPI_STOP 0xFFFFFFFEu // what the fake wait does once its events run out: ask the output to stop

#define TEST_DEVICE_INVALIDATED ((int32_t)0x88890004u) // AUDCLNT_E_DEVICE_INVALIDATED
#define TEST_BUFFER_TOO_LARGE ((int32_t)0x88890006u)   // AUDCLNT_E_BUFFER_TOO_LARGE

static struct
{
    SakuraAudioWasapiOutput *output;
    uint32_t bufferFrames;
    uint32_t period;
    int frameBytes;
    int exclusive;
    int events;         // signalled before endResult
    uint32_t endResult; // a WaitForSingleObject result, or FAKE_WASAPI_STOP
    int failGetBufferAt;
    int32_t failResult;

    uint32_t padding;   // frames in the endpoint not played yet
    uint64_t position;  // frames played, in the clock's units (its frequency is the rate)
    uint64_t qpc;       // 1000 a period, in 100 ns units
    int starts;
    int stops;
    int waits;
    int paddingCalls;
    int getBufferCalls;
    int releases;
    uint32_t requested[FAKE_WASAPI_MAX_CALLS];
    uint32_t releaseFlags[FAKE_WASAPI_MAX_CALLS];
    uint8_t endpoint[4096 * SAKURA_AUDIO_MAX_CHANNELS * 4];
    uint8_t played[FAKE_WASAPI_MAX_PLAYED]; // every frame released, silence written out as zeros
    size_t playedBytes;
} wasapi_fake;

static int32_t SAKURA_AUDIO_STDCALL fake_client_padding(void *self, uint32_t *padding)
{
    (void)self;
    wasapi_fake.paddingCalls++;
    *padding = wasapi_fake.padding;
    return 0;
}

static int32_t SAKURA_AUDIO_STDCALL fake_client_start(void *self)
{
    (void)self;
    wasapi_fake.starts++;
    return 0;
}

static int32_t SAKURA_AUDIO_STDCALL fake_client_stop(void *self)
{
    (void)self;
    wasapi_fake.stops++;
    return 0;
}

static int32_t SAKURA_AUDIO_STDCALL fake_render_get_buffer(void *self, uint32_t frames, uint8_t **data)
{
    (void)self;
    int call = wasapi_fake.getBufferCalls++;

    if (call + 1 == wasapi_fake.failGetBufferAt)
        return wasapi_fake.failResult;

    // WASAPI's own rules: no more than the endpoint has room for, and the whole buffer in exclusive mode.
    if (frames > wasapi_fake.bufferFrames - wasapi_fake.padding || (wasapi_fake.exclusive && frames != wasapi_fake.bufferFrames))
        return TEST_BUFFER_TOO_LARGE;

    if (call < FAKE_WASAPI_MAX_CALLS)
        wasapi_fake.requested[call] = frames;

    memset(wasapi_fake.endpoint, 0xCD, (size_t)frames * (size_t)wasapi_fake.frameBytes);
    *data = wasapi_fake.endpoint;
    return 0;
}

static int32_t SAKURA_AUDIO_STDCALL fake_render_release(void *self, uint32_t frames, uint32_t flags)
{
    (void)self;
    int call = wasapi_fake.releases++;

    if (call < FAKE_WASAPI_MAX_CALLS)
        wasapi_fake.releaseFlags[call] = flags;

    size_t bytes = (size_t)frames * (size_t)wasapi_fake.frameBytes;

    if (wasapi_fake.playedBytes + bytes <= sizeof(wasapi_fake.played))
    {
        if (flags & 0x2u)
            memset(wasapi_fake.played + wasapi_fake.playedBytes, 0, bytes);
        else
            memcpy(wasapi_fake.played + wasapi_fake.playedBytes, wasapi_fake.endpoint, bytes);

        wasapi_fake.playedBytes += bytes;
    }

    wasapi_fake.padding += frames;
    return 0;
}

static int32_t SAKURA_AUDIO_STDCALL fake_clock_position(void *self, uint64_t *position, uint64_t *qpcPosition)
{
    (void)self;
    *position = wasapi_fake.position;
    *qpcPosition = wasapi_fake.qpc;
    return 0;
}

// Each event is a period the device played: the padding drains by it, the position and QPC time move on.
static uint32_t SAKURA_AUDIO_STDCALL fake_wait(void *handle, uint32_t milliseconds)
{
    (void)handle;
    (void)milliseconds;
    wasapi_fake.waits++;

    if (wasapi_fake.events > 0)
    {
        wasapi_fake.events--;

        uint32_t played = wasapi_fake.period < wasapi_fake.padding ? wasapi_fake.period : wasapi_fake.padding;
        wasapi_fake.padding -= played;
        wasapi_fake.position += played;
        wasapi_fake.qpc += 1000;
        return 0;
    }

    if (wasapi_fake.endResult != FAKE_WASAPI_STOP)
        return wasapi_fake.endResult;

    // As the managed side stops it: asks, then signals the event.
    sakura_audio_wasapi_stop(wasapi_fake.output);
    return 0;
}

static const test_com_method fake_client_vtable[12] = {
    NULL, NULL, NULL, NULL, NULL, NULL, (test_com_method)fake_client_padding, NULL, NULL, NULL, (test_com_method)fake_client_start,
    (test_com_method)fake_client_stop,
};

static const test_com_method fake_render_vtable[5] = {
    NULL, NULL, NULL, (test_com_method)fake_render_get_buffer, (test_com_method)fake_render_release,
};

static const test_com_method fake_clock_vtable[5] = {
    NULL, NULL, NULL, NULL, (test_com_method)fake_clock_position,
};

static test_com_object fake_client = { fake_client_vtable };
static test_com_object fake_render = { fake_render_vtable };
static test_com_object fake_clock = { fake_clock_vtable };

static SakuraAudioWasapiConfig fake_wasapi(uint32_t bufferFrames, uint32_t period, int channels, int format, int sampleBytes, int exclusive, int events)
{
    memset(&wasapi_fake, 0, sizeof(wasapi_fake));
    wasapi_fake.bufferFrames = bufferFrames;
    wasapi_fake.period = period;
    wasapi_fake.frameBytes = channels * sampleBytes;
    wasapi_fake.exclusive = exclusive;
    wasapi_fake.events = events;
    wasapi_fake.endResult = FAKE_WASAPI_STOP;

    SakuraAudioWasapiConfig config;
    memset(&config, 0, sizeof(config));
    config.clockFrequency = SAMPLE_RATE;
    config.audioClient = &fake_client;
    config.renderClient = &fake_render;
    config.audioClock = &fake_clock;
    config.event = &wasapi_fake;
    config.wait = fake_wait;
    config.bufferFrames = (int32_t)bufferFrames;
    config.channels = channels;
    config.sampleFormat = format;
    config.exclusive = exclusive;
    return config;
}

static void fill_test_signal(float *pcm, int frames)
{
    for (int i = 0; i < frames * CHANNELS; i++)
        pcm[i] = (float)sin(i * 0.01) * 0.5f;
}

// Shared mode: the buffer goes in silent before the start, then each event fills what the endpoint has room for with
// exactly what the engine mixes, and the client is stopped before the run returns.
static void test_wasapi_shared_render_loop(void)
{
    SakuraAudioEngine *engine = make_engine(128);
    SakuraAudioEngine *reference = make_engine(128);

    static float pcm[2048 * CHANNELS];
    fill_test_signal(pcm, 2048);
    attach_static_voice(engine, pcm, 2048, NULL);
    attach_static_voice(reference, pcm, 2048, NULL);

    // A 480-frame endpoint played 160 frames between events, five events.
    SakuraAudioWasapiConfig config = fake_wasapi(480, 160, CHANNELS, SAKURA_AUDIO_SAMPLE_FLOAT32, 4, 0, 5);
    SakuraAudioWasapiOutput *output = sakura_audio_wasapi_create(engine, &config);
    CHECK(output != NULL);
    wasapi_fake.output = output;

    CHECK(sakura_audio_wasapi_run(output) == SAKURA_AUDIO_OK);

    CHECK(wasapi_fake.starts == 1);
    CHECK(wasapi_fake.stops == 1);
    CHECK(wasapi_fake.waits == 6);
    CHECK(wasapi_fake.paddingCalls == 5);
    CHECK(wasapi_fake.getBufferCalls == 6);
    CHECK(wasapi_fake.requested[0] == 480);
    CHECK(wasapi_fake.releaseFlags[0] == 0x2u);

    for (int i = 1; i < 6; i++)
    {
        CHECK(wasapi_fake.requested[i] == 160);
        CHECK(wasapi_fake.releaseFlags[i] == 0);
    }

    // 480 frames of silence, then the engine's mix exactly.
    static float expected[800 * CHANNELS];
    sakura_audio_engine_mix(reference, expected, 800);

    CHECK(wasapi_fake.playedBytes == (480 + 800) * CHANNELS * sizeof(float));

    static float silence[480 * CHANNELS];
    CHECK(memcmp(wasapi_fake.played, silence, sizeof(silence)) == 0);
    CHECK(memcmp(wasapi_fake.played + sizeof(silence), expected, sizeof(expected)) == 0);

    SakuraAudioStats stats;
    sakura_audio_engine_get_stats(engine, &stats);
    CHECK(stats.callbacks == 5);
    CHECK(stats.framesMixed == 800);
    CHECK(stats.activeVoices == 1);

    // The last cycle: 1120 frames handed over, the silence included, 800 played at QPC time 5000. Its first frame goes
    // out after the other 320, 320 / 44100 s later, which is 72562 of QPC's 100 ns units.
    SakuraAudioDeviceTime time;
    CHECK(sakura_audio_engine_get_device_time(engine, &time) == SAKURA_AUDIO_OK);
    CHECK(time.framesMixed == 640);
    CHECK(time.hostTime == 5000 + 72562);
    CHECK(time.discontinuities == 0);

    // Once per output.
    CHECK(sakura_audio_wasapi_run(output) == SAKURA_AUDIO_INVALID);
    CHECK(wasapi_fake.starts == 1);

    sakura_audio_wasapi_destroy(output);
    sakura_audio_engine_destroy(reference);
    sakura_audio_engine_destroy(engine);
}

// Shared mode with a fill: a 960-frame endpoint kept at 480 frames queued, two of its 240-frame periods. The silence
// before the start is the fill, and each event tops it up by what the device played, not to the whole buffer.
static void test_wasapi_shared_fill(void)
{
    SakuraAudioEngine *engine = make_engine(128);
    SakuraAudioEngine *reference = make_engine(128);

    static float pcm[2048 * CHANNELS];
    fill_test_signal(pcm, 2048);
    attach_static_voice(engine, pcm, 2048, NULL);
    attach_static_voice(reference, pcm, 2048, NULL);

    SakuraAudioWasapiConfig config = fake_wasapi(960, 240, CHANNELS, SAKURA_AUDIO_SAMPLE_FLOAT32, 4, 0, 4);
    config.fillFrames = 480;

    SakuraAudioWasapiOutput *output = sakura_audio_wasapi_create(engine, &config);
    CHECK(output != NULL);
    wasapi_fake.output = output;

    CHECK(sakura_audio_wasapi_run(output) == SAKURA_AUDIO_OK);

    CHECK(wasapi_fake.getBufferCalls == 5);
    CHECK(wasapi_fake.requested[0] == 480);
    CHECK(wasapi_fake.releaseFlags[0] == 0x2u);

    for (int i = 1; i < 5; i++)
        CHECK(wasapi_fake.requested[i] == 240);

    // Never more queued than the fill.
    CHECK(wasapi_fake.padding <= 480);

    // 480 frames of silence, then the engine's mix exactly.
    static float expected[960 * CHANNELS];
    sakura_audio_engine_mix(reference, expected, 960);

    CHECK(wasapi_fake.playedBytes == (480 + 960) * CHANNELS * sizeof(float));

    static float silence[480 * CHANNELS];
    CHECK(memcmp(wasapi_fake.played, silence, sizeof(silence)) == 0);
    CHECK(memcmp(wasapi_fake.played + sizeof(silence), expected, sizeof(expected)) == 0);

    sakura_audio_wasapi_destroy(output);

    // A fill past the buffer is the whole buffer; a negative one isn't a config.
    config = fake_wasapi(960, 240, CHANNELS, SAKURA_AUDIO_SAMPLE_FLOAT32, 4, 0, 1);
    config.fillFrames = 4000;
    output = sakura_audio_wasapi_create(engine, &config);
    CHECK(output != NULL);
    wasapi_fake.output = output;
    CHECK(sakura_audio_wasapi_run(output) == SAKURA_AUDIO_OK);
    CHECK(wasapi_fake.requested[0] == 960);
    sakura_audio_wasapi_destroy(output);

    config.fillFrames = -1;
    CHECK(sakura_audio_wasapi_create(engine, &config) == NULL);

    sakura_audio_engine_destroy(reference);
    sakura_audio_engine_destroy(engine);
}

// Exclusive mode takes the whole buffer at every event and never reads the padding. Here into 16-bit samples, rounded
// from the engine's mix.
static void test_wasapi_exclusive_to_int16(void)
{
    SakuraAudioEngine *engine = make_engine(128);
    SakuraAudioEngine *reference = make_engine(128);

    static float pcm[2048 * CHANNELS];
    fill_test_signal(pcm, 2048);
    attach_static_voice(engine, pcm, 2048, NULL);
    attach_static_voice(reference, pcm, 2048, NULL);

    SakuraAudioWasapiConfig config = fake_wasapi(256, 256, CHANNELS, SAKURA_AUDIO_SAMPLE_INT16, 2, 1, 3);
    SakuraAudioWasapiOutput *output = sakura_audio_wasapi_create(engine, &config);
    CHECK(output != NULL);
    wasapi_fake.output = output;

    CHECK(sakura_audio_wasapi_run(output) == SAKURA_AUDIO_OK);
    CHECK(wasapi_fake.paddingCalls == 0);
    CHECK(wasapi_fake.getBufferCalls == 4);

    for (int i = 0; i < 4; i++)
        CHECK(wasapi_fake.requested[i] == 256);

    static float expected[768 * CHANNELS];
    sakura_audio_engine_mix(reference, expected, 768);

    CHECK(wasapi_fake.playedBytes == (256 + 768) * CHANNELS * sizeof(int16_t));

    int mismatches = 0;

    for (int i = 0; i < 768 * CHANNELS; i++)
    {
        int16_t sample;
        memcpy(&sample, wasapi_fake.played + (256 * CHANNELS + i) * sizeof(int16_t), sizeof(sample));

        if (sample != (int16_t)lrintf(expected[i] * 32767.0f))
            mismatches++;
    }

    CHECK(mismatches == 0);

    sakura_audio_wasapi_destroy(output);
    sakura_audio_engine_destroy(reference);
    sakura_audio_engine_destroy(engine);
}

// Runs one exclusive cycle of a steady stereo frame, left 0.25 and right -0.375, out to a device of another format or
// width, and returns the first frame it played after the silence.
static const uint8_t *wasapi_play_constant(int channels, int format, int sampleBytes)
{
    SakuraAudioEngine *engine = make_engine(128);

    static float pcm[64 * CHANNELS];

    for (int i = 0; i < 64; i++)
    {
        pcm[i * 2] = 0.25f;
        pcm[i * 2 + 1] = -0.375f;
    }

    attach_static_voice(engine, pcm, 64, NULL);

    SakuraAudioWasapiConfig config = fake_wasapi(32, 32, channels, format, sampleBytes, 1, 1);
    SakuraAudioWasapiOutput *output = sakura_audio_wasapi_create(engine, &config);
    CHECK(output != NULL);
    wasapi_fake.output = output;

    CHECK(sakura_audio_wasapi_run(output) == SAKURA_AUDIO_OK);
    CHECK(wasapi_fake.playedBytes == (size_t)64 * (size_t)channels * (size_t)sampleBytes);

    sakura_audio_wasapi_destroy(output);
    sakura_audio_engine_destroy(engine);
    return wasapi_fake.played + (size_t)32 * (size_t)channels * (size_t)sampleBytes;
}

// The device's samples and width: rounded to 16, packed 24 and 32 bits, little-endian; a wider device gets silence in
// the channels the engine doesn't have, and a mono one the average.
static void test_wasapi_formats_and_widths(void)
{
    const uint8_t *frame = wasapi_play_constant(CHANNELS, SAKURA_AUDIO_SAMPLE_INT16, 2);
    int16_t pair16[2];
    memcpy(pair16, frame, sizeof(pair16));
    CHECK(pair16[0] == 8192);   // 8191.75
    CHECK(pair16[1] == -12288); // -12287.625

    frame = wasapi_play_constant(CHANNELS, SAKURA_AUDIO_SAMPLE_INT24, 3);
    CHECK(frame[0] == 0x00 && frame[1] == 0x00 && frame[2] == 0x20); // 2097152
    CHECK(frame[3] == 0x00 && frame[4] == 0x00 && frame[5] == 0xD0); // -3145728

    frame = wasapi_play_constant(CHANNELS, SAKURA_AUDIO_SAMPLE_INT32, 4);
    int32_t pair32[2];
    memcpy(pair32, frame, sizeof(pair32));
    CHECK(pair32[0] == 536870912);
    CHECK(pair32[1] == -805306368);

    frame = wasapi_play_constant(4, SAKURA_AUDIO_SAMPLE_FLOAT32, 4);
    float quad[4];
    memcpy(quad, frame, sizeof(quad));
    CHECK(quad[0] == 0.25f && quad[1] == -0.375f);
    CHECK(quad[2] == 0.0f && quad[3] == 0.0f);

    frame = wasapi_play_constant(1, SAKURA_AUDIO_SAMPLE_FLOAT32, 4);
    float mono;
    memcpy(&mono, frame, sizeof(mono));
    CHECK(mono == -0.0625f);
}

// The ways a run ends early. Each stops the client it started, and none starts one it was told to stop before.
static void test_wasapi_failures(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    // The device went away mid-stream: the failing HRESULT comes back as it is.
    SakuraAudioWasapiConfig config = fake_wasapi(480, 160, CHANNELS, SAKURA_AUDIO_SAMPLE_FLOAT32, 4, 0, 5);
    wasapi_fake.failGetBufferAt = 3;
    wasapi_fake.failResult = TEST_DEVICE_INVALIDATED;
    SakuraAudioWasapiOutput *output = sakura_audio_wasapi_create(engine, &config);
    wasapi_fake.output = output;
    CHECK(sakura_audio_wasapi_run(output) == TEST_DEVICE_INVALIDATED);
    CHECK(wasapi_fake.starts == 1 && wasapi_fake.stops == 1);
    sakura_audio_wasapi_destroy(output);

    // Before the start: nothing to stop.
    config = fake_wasapi(480, 160, CHANNELS, SAKURA_AUDIO_SAMPLE_FLOAT32, 4, 0, 5);
    wasapi_fake.failGetBufferAt = 1;
    wasapi_fake.failResult = TEST_DEVICE_INVALIDATED;
    output = sakura_audio_wasapi_create(engine, &config);
    wasapi_fake.output = output;
    CHECK(sakura_audio_wasapi_run(output) == TEST_DEVICE_INVALIDATED);
    CHECK(wasapi_fake.starts == 0 && wasapi_fake.stops == 0);
    sakura_audio_wasapi_destroy(output);

    // No event for two seconds, and a wait that failed.
    config = fake_wasapi(480, 160, CHANNELS, SAKURA_AUDIO_SAMPLE_FLOAT32, 4, 0, 2);
    wasapi_fake.endResult = 0x102u;
    output = sakura_audio_wasapi_create(engine, &config);
    wasapi_fake.output = output;
    CHECK(sakura_audio_wasapi_run(output) == SAKURA_AUDIO_TIMEOUT);
    CHECK(wasapi_fake.stops == 1);
    sakura_audio_wasapi_destroy(output);

    config = fake_wasapi(480, 160, CHANNELS, SAKURA_AUDIO_SAMPLE_FLOAT32, 4, 0, 0);
    wasapi_fake.endResult = 0xFFFFFFFFu;
    output = sakura_audio_wasapi_create(engine, &config);
    wasapi_fake.output = output;
    CHECK(sakura_audio_wasapi_run(output) == SAKURA_AUDIO_ERROR);
    CHECK(wasapi_fake.stops == 1);
    sakura_audio_wasapi_destroy(output);

    // Stopped before it ran: returns at once and touches nothing.
    config = fake_wasapi(480, 160, CHANNELS, SAKURA_AUDIO_SAMPLE_FLOAT32, 4, 0, 5);
    output = sakura_audio_wasapi_create(engine, &config);
    sakura_audio_wasapi_stop(output);
    CHECK(sakura_audio_wasapi_run(output) == SAKURA_AUDIO_OK);
    CHECK(wasapi_fake.starts == 0 && wasapi_fake.getBufferCalls == 0);
    sakura_audio_wasapi_destroy(output);

    // Without a clock nothing is published, and the rest plays the same. On an engine of its own, since the runs above
    // published.
    SakuraAudioEngine *unclocked = make_engine(128);
    config = fake_wasapi(480, 160, CHANNELS, SAKURA_AUDIO_SAMPLE_FLOAT32, 4, 0, 2);
    config.audioClock = NULL;
    output = sakura_audio_wasapi_create(unclocked, &config);
    wasapi_fake.output = output;
    CHECK(sakura_audio_wasapi_run(output) == SAKURA_AUDIO_OK);
    CHECK(wasapi_fake.getBufferCalls == 3);

    SakuraAudioDeviceTime time;
    CHECK(sakura_audio_engine_get_device_time(unclocked, &time) == SAKURA_AUDIO_ERROR);
    sakura_audio_wasapi_destroy(output);
    sakura_audio_engine_destroy(unclocked);

    // Configs it can't play.
    config = fake_wasapi(480, 160, CHANNELS, SAKURA_AUDIO_SAMPLE_FLOAT32, 4, 0, 0);
    CHECK(sakura_audio_wasapi_create(NULL, &config) == NULL);
    CHECK(sakura_audio_wasapi_create(engine, NULL) == NULL);

    SakuraAudioWasapiConfig bad = config;
    bad.wait = NULL;
    CHECK(sakura_audio_wasapi_create(engine, &bad) == NULL);
    bad = config;
    bad.event = NULL;
    CHECK(sakura_audio_wasapi_create(engine, &bad) == NULL);
    bad = config;
    bad.bufferFrames = 0;
    CHECK(sakura_audio_wasapi_create(engine, &bad) == NULL);
    bad = config;
    bad.channels = SAKURA_AUDIO_MAX_CHANNELS + 1;
    CHECK(sakura_audio_wasapi_create(engine, &bad) == NULL);
    bad = config;
    bad.sampleFormat = 7;
    CHECK(sakura_audio_wasapi_create(engine, &bad) == NULL);

    CHECK(sakura_audio_wasapi_run(NULL) == SAKURA_AUDIO_INVALID);
    sakura_audio_wasapi_stop(NULL);
    sakura_audio_wasapi_destroy(NULL);

    sakura_audio_engine_destroy(engine);
}

// ---------------------------------------------------------------------------------------------
// PipeWire, against a fake stream: pw_buffer and SPA's buffer, data and chunk as their headers lay them out.

typedef struct
{
    uint32_t offset;
    uint32_t size;
    int32_t stride;
    int32_t flags;
} test_spa_chunk;

typedef struct
{
    uint32_t type;
    uint32_t flags;
    int64_t fd;
    uint32_t mapOffset;
    uint32_t maxSize;
    void *data;
    test_spa_chunk *chunk;
} test_spa_data;

typedef struct
{
    uint32_t metaCount;
    uint32_t dataCount;
    void *metas;
    test_spa_data *datas;
} test_spa_buffer;

typedef struct
{
    test_spa_buffer *buffer;
    void *userData;
    uint64_t size;
    uint64_t requested;
    uint64_t time;
} test_pw_buffer;

static struct
{
    test_pw_buffer *next; // what the next dequeue hands over
    int dequeues;
    int queues;
    void *queuedStream;
    void *queuedBuffer;
} pipewire_fake;

static void *fake_dequeue(void *stream)
{
    (void)stream;
    pipewire_fake.dequeues++;
    return pipewire_fake.next;
}

static int fake_queue(void *stream, void *buffer)
{
    pipewire_fake.queues++;
    pipewire_fake.queuedStream = stream;
    pipewire_fake.queuedBuffer = buffer;
    return 0;
}

// The process callback mixes what the stream asked for straight into its buffer, or the configured quantum where it
// doesn't say, never more than the buffer holds, and hands it back with its chunk and its size in frames.
static void test_pipewire_process(void)
{
    // libpipewire's offsets on 64-bit targets, which the library's own declarations have to match.
    if (sizeof(void *) == 8)
    {
        CHECK(offsetof(test_spa_data, maxSize) == 20);
        CHECK(offsetof(test_spa_data, data) == 24);
        CHECK(offsetof(test_spa_data, chunk) == 32);
        CHECK(offsetof(test_spa_buffer, datas) == 16);
        CHECK(offsetof(test_pw_buffer, size) == 16);
        CHECK(offsetof(test_pw_buffer, requested) == 24);
    }

    SakuraAudioEngine *engine = make_engine(128);
    SakuraAudioEngine *reference = make_engine(128);

    static float pcm[2048 * CHANNELS];
    fill_test_signal(pcm, 2048);
    attach_static_voice(engine, pcm, 2048, NULL);
    attach_static_voice(reference, pcm, 2048, NULL);

    int stream = 0;
    SakuraAudioPipeWireConfig config;
    memset(&config, 0, sizeof(config));
    config.stream = &stream;
    config.dequeue = fake_dequeue;
    config.queue = fake_queue;
    config.framesPerCycle = 128;
    config.hasRequested = 1;

    SakuraAudioPipeWireOutput *output = sakura_audio_pipewire_create(engine, &config);
    CHECK(output != NULL);

    void (*process)(void *) = (void (*)(void *))sakura_audio_get_pipewire_process_callback();
    CHECK(process != NULL);

    static float data[1024 * CHANNELS];
    test_spa_chunk chunk = { 99, 99, 99, 0 };
    test_spa_data plane = { 0, 0, -1, 0, (uint32_t)sizeof(data), data, &chunk };
    test_spa_buffer spa = { 0, 1, NULL, &plane };
    test_pw_buffer buffer = { &spa, NULL, 0, 300, 0 };

    memset(&pipewire_fake, 0, sizeof(pipewire_fake));
    pipewire_fake.next = &buffer;

    // What the stream asked for, which isn't a whole number of mix blocks.
    static float expected[1024 * CHANNELS];
    process(output);
    sakura_audio_engine_mix(reference, expected, 300);

    CHECK(memcmp(data, expected, 300 * CHANNELS * sizeof(float)) == 0);
    CHECK(pipewire_fake.dequeues == 1 && pipewire_fake.queues == 1);
    CHECK(pipewire_fake.queuedStream == &stream && pipewire_fake.queuedBuffer == &buffer);
    CHECK(chunk.offset == 0);
    CHECK(chunk.stride == CHANNELS * (int32_t)sizeof(float));
    CHECK(chunk.size == 300 * CHANNELS * sizeof(float));
    CHECK(buffer.size == 300);

    SakuraAudioStats stats;
    sakura_audio_engine_get_stats(engine, &stats);
    CHECK(stats.callbacks == 1);
    CHECK(stats.framesMixed == 300);

    // Nothing asked for: the quantum.
    buffer.requested = 0;
    process(output);
    sakura_audio_engine_mix(reference, expected, 128);
    CHECK(memcmp(data, expected, 128 * CHANNELS * sizeof(float)) == 0);
    CHECK(buffer.size == 128);

    // More asked for than the buffer holds: what it holds.
    buffer.requested = 5000;
    process(output);
    CHECK(buffer.size == 1024);
    CHECK(chunk.size == sizeof(data));
    sakura_audio_engine_mix(reference, expected, 1024);

    // A libpipewire from before `requested`, where that field is something else: the quantum.
    SakuraAudioPipeWireConfig old = config;
    old.hasRequested = 0;
    SakuraAudioPipeWireOutput *oldOutput = sakura_audio_pipewire_create(engine, &old);
    buffer.requested = 300;
    process(oldOutput);
    sakura_audio_engine_mix(reference, expected, 128);
    CHECK(memcmp(data, expected, 128 * CHANNELS * sizeof(float)) == 0);
    CHECK(buffer.size == 128);
    sakura_audio_pipewire_destroy(oldOutput);

    sakura_audio_engine_get_stats(engine, &stats);
    CHECK(stats.callbacks == 4);
    CHECK(stats.framesMixed == 300 + 128 + 1024 + 128);

    // No buffer free: nothing mixed and nothing queued.
    pipewire_fake.next = NULL;
    pipewire_fake.queues = 0;
    process(output);
    CHECK(pipewire_fake.queues == 0);

    // A buffer with nowhere to write goes back empty.
    plane.data = NULL;
    pipewire_fake.next = &buffer;
    process(output);
    CHECK(pipewire_fake.queues == 1);
    CHECK(buffer.size == 0 && chunk.size == 0);

    process(NULL);

    sakura_audio_engine_get_stats(engine, &stats);
    CHECK(stats.callbacks == 4);

    // Configs it can't play.
    CHECK(sakura_audio_pipewire_create(NULL, &config) == NULL);
    CHECK(sakura_audio_pipewire_create(engine, NULL) == NULL);

    SakuraAudioPipeWireConfig bad = config;
    bad.stream = NULL;
    CHECK(sakura_audio_pipewire_create(engine, &bad) == NULL);
    bad = config;
    bad.dequeue = NULL;
    CHECK(sakura_audio_pipewire_create(engine, &bad) == NULL);
    bad = config;
    bad.framesPerCycle = 0;
    CHECK(sakura_audio_pipewire_create(engine, &bad) == NULL);
    sakura_audio_pipewire_destroy(NULL);

    sakura_audio_pipewire_destroy(output);
    sakura_audio_engine_destroy(reference);
    sakura_audio_engine_destroy(engine);
}

// The unity path copies whole runs instead of interpolating each frame. It has to be a copy of exactly
// the right frames, across block boundaries, and it has to leave the interpolation window where the
// per-frame path would have, so that a rate change mid-stream carries straight on.
static void test_unity_runs_and_rate_handover(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    const int frames = 1024;
    float *pcm = (float *)malloc((size_t)frames * CHANNELS * sizeof(float));

    for (int i = 0; i < frames; i++)
    {
        pcm[i * 2] = 0.0005f * (float)i;
        pcm[i * 2 + 1] = -0.0005f * (float)i;
    }

    SakuraAudioHandle voice = attach_static_voice(engine, pcm, frames, NULL);

    // Odd-sized requests, so blocks and runs never line up with anything.
    static float out[1024 * CHANNELS];
    int produced = 0;
    int sizes[] = { 3, 1, 2, 77, 128, 50 };

    for (int i = 0; i < 6; i++)
    {
        sakura_audio_engine_mix(engine, out + produced * CHANNELS, sizes[i]);
        produced += sizes[i];
    }

    int mismatches = 0;

    for (int i = 0; i < produced * CHANNELS; i++)
    {
        if (out[i] != pcm[i])
            mismatches++;
    }

    CHECK(mismatches == 0);

    // The window holds the three frames after the last one played, plus that one. Doubling the rate
    // now must land on frame `produced` and then every other frame, exactly, as it would had the
    // whole stream been interpolated frame by frame.
    sakura_audio_node_set_rate(engine, voice, 2.0);

    float doubled[8 * CHANNELS];
    sakura_audio_engine_mix(engine, doubled, 8);

    for (int i = 0; i < 8; i++)
        CHECK_NEAR(doubled[i * 2], pcm[(produced + i * 2) * 2], 0.0);

    free(pcm);
    sakura_audio_engine_destroy(engine);
}

// The run-based resampler paths against the per-frame algorithm they replaced, reimplemented here
// from its definition: a 4-frame Hermite window primed with silence then the first three frames, one
// output per step, the position advanced by the ratio and the window shifted once per whole frame,
// and a dry source shifting in silence until the window is all zeros. Same inputs, same arithmetic,
// so the outputs must match to the bit -- at unity, off unity, across odd block sizes, and through a
// rate change halfway through.
typedef struct ReferenceResampler
{
    const float *pcm;
    int frames;
    int next;
    float window[4 * CHANNELS];
    double position;
    int drained;
} ReferenceResampler;

static float reference_interpolate(float w0, float w1, float w2, float w3, float t)
{
    float c0 = w1;
    float c1 = 0.5f * (w2 - w0);
    float c2 = w0 - 2.5f * w1 + 2.0f * w2 - 0.5f * w3;
    float c3 = 0.5f * (w3 - w0) + 1.5f * (w1 - w2);
    return ((c3 * t + c2) * t + c1) * t + c0;
}

static int reference_advance(ReferenceResampler *r)
{
    memmove(r->window, r->window + CHANNELS, 3 * CHANNELS * sizeof(float));

    if (r->next < r->frames)
    {
        memcpy(r->window + 3 * CHANNELS, r->pcm + r->next * CHANNELS, CHANNELS * sizeof(float));
        r->next++;
        r->drained = 0;
        return 1;
    }

    memset(r->window + 3 * CHANNELS, 0, CHANNELS * sizeof(float));
    return ++r->drained < 4;
}

// Produces up to `count` frames; returns how many before the window drained.
static int reference_read(ReferenceResampler *r, float *out, int count, double ratio)
{
    for (int produced = 0; produced < count; produced++)
    {
        for (int c = 0; c < CHANNELS; c++)
            out[produced * CHANNELS + c] = reference_interpolate(r->window[c], r->window[CHANNELS + c],
                                                                 r->window[2 * CHANNELS + c], r->window[3 * CHANNELS + c],
                                                                 (float)r->position);

        r->position += ratio;

        while (r->position >= 1.0)
        {
            if (!reference_advance(r))
            {
                r->position -= floor(r->position);
                return produced + 1;
            }

            r->position -= 1.0;
        }
    }

    return count;
}

static void test_resampler_paths_match_the_per_frame_algorithm(void)
{
    const int frames = 3000;
    float *pcm = (float *)malloc((size_t)frames * CHANNELS * sizeof(float));

    // Not periodic, not smooth, never zero: anything that took the wrong frame or the wrong position
    // would show.
    for (int i = 0; i < frames; i++)
    {
        pcm[i * 2] = 0.4f * (float)sin(0.0137 * i) + 0.1f * (float)sin(0.91 * i) + 0.001f;
        pcm[i * 2 + 1] = 0.3f * (float)cos(0.0291 * i) - 0.2f * (float)sin(0.57 * i) - 0.001f;
    }

    // The last two schedules switch rate partway: 1.0 then 1.37 from output frame 700 on, and 1.37
    // back to exactly 1.0 -- which leaves the position between frames for good, the case the copy path
    // cannot take.
    double ratios[] = { 1.0, 0.73, 1.37, 2.5, -1.0, -2.0 };
    int sizes[] = { 128, 3, 1, 61, 128, 200, 7, 128 };

    for (int schedule = 0; schedule < 6; schedule++)
    {
        SakuraAudioEngine *engine = make_engine(128);
        SakuraAudioHandle voice = attach_static_voice(engine, pcm, frames, NULL);

        ReferenceResampler reference = { pcm, frames, 0, { 0 }, 0.0, 0 };

        for (int i = 1; i < 4; i++)
        {
            memcpy(reference.window + i * CHANNELS, pcm + reference.next * CHANNELS, CHANNELS * sizeof(float));
            reference.next++;
        }

        static float actual[400 * CHANNELS];
        static float expected[400 * CHANNELS];
        int played = 0;
        int mismatches = 0;

        for (int round = 0; round < 24; round++)
        {
            double ratio = ratios[schedule] > 0 ? ratios[schedule]
                           : ratios[schedule] == -1.0 ? (played < 700 ? 1.0 : 1.37)
                                                      : (played < 700 ? 1.37 : 1.0);
            int count = sizes[round % 8];

            sakura_audio_node_set_rate(engine, voice, ratio);
            sakura_audio_engine_mix(engine, actual, count);

            int produced = reference_read(&reference, expected, count, ratio);

            for (int i = 0; i < produced * CHANNELS; i++)
            {
                if (actual[i] != expected[i])
                    mismatches++;
            }

            played += count;
        }

        CHECK(mismatches == 0);
        sakura_audio_engine_destroy(engine);
    }

    free(pcm);
}

// A counter as audio: frame n carries (n mod 9000) / 16384. An integer times a power of two, so it is
// exact in a float and in any wider precision, which keeps an exact comparison exact on a compiler that
// holds intermediates in 80-bit x87 registers -- a value computed for comparison and one that went
// through memory as a float would otherwise differ in the last bits.
static float counter_sample(int frame)
{
    return (float)(frame % 9000) * (1.0f / 16384.0f);
}

// A ring whose capacity is not a multiple of the mix block, written and drained in sizes that wrap it
// at every offset: the audio thread reads runs straight out of the storage, so a frame split across
// the end, or a run that miscounts the wrap, would show up as a wrong sample.
static void test_stream_runs_across_the_wrap(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    SakuraAudioHandle voice = sakura_audio_create_voice(engine);
    CHECK(sakura_audio_voice_set_stream(engine, voice, 300) == SAKURA_AUDIO_OK);
    sakura_audio_add_child(engine, sakura_audio_engine_root(engine), voice);
    sakura_audio_node_play(engine, voice);

    float chunk[200 * CHANNELS];
    float out[150 * CHANNELS];
    int writtenFrames = 0;
    int playedFrames = 0;
    int mismatches = 0;

    for (int round = 0; round < 40; round++)
    {
        int toWrite = 97 + (round * 31) % 100;
        int space = sakura_audio_stream_space(engine, voice);

        if (toWrite > space)
            toWrite = space;

        for (int i = 0; i < toWrite; i++)
        {
            chunk[i * 2] = counter_sample(writtenFrames + i);
            chunk[i * 2 + 1] = -chunk[i * 2];
        }

        CHECK(sakura_audio_stream_write(engine, voice, chunk, toWrite) == toWrite);
        writtenFrames += toWrite;

        // Only ever ask for what is buffered beyond the window, so the voice never starves and every
        // frame out is a frame that was written.
        int request = 61 + (round * 17) % 89;
        int ahead = writtenFrames - playedFrames - 3;

        if (request > ahead)
            request = ahead;

        if (request <= 0)
            continue;

        sakura_audio_engine_mix(engine, out, request);

        for (int i = 0; i < request; i++)
        {
            float expected = counter_sample(playedFrames + i);

            if (out[i * 2] != expected || out[i * 2 + 1] != -expected)
                mismatches++;
        }

        playedFrames += request;
    }

    CHECK(playedFrames > 2000);
    CHECK(mismatches == 0);

    sakura_audio_engine_destroy(engine);
}

// Nothing meters until something reads, a read arms it for a second of audio, and a meter nobody reads
// any more stops -- which is what makes metering free for every hitsound voice nobody visualises.
static void test_metering_runs_only_while_read(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    const int frames = SAMPLE_RATE * 3;
    float *pcm = (float *)malloc((size_t)frames * CHANNELS * sizeof(float));

    for (int i = 0; i < frames * CHANNELS; i++)
        pcm[i] = 0.5f;

    SakuraAudioHandle voice = attach_static_voice(engine, pcm, frames, NULL);

    float out[128 * CHANNELS];
    float bins[SAKURA_AUDIO_BINS];
    SakuraAudioNodeState state;

    for (int i = 0; i < 16; i++)
        sakura_audio_engine_mix(engine, out, 128);

    sakura_audio_node_get_state(engine, voice, &state);
    CHECK_NEAR(state.amplitudeLeft, 0.0f, 0.0);

    // The first read arms it and has nothing to report yet.
    CHECK(sakura_audio_node_read_spectrum(engine, voice, bins, SAKURA_AUDIO_BINS) == 0);

    for (int i = 0; i < 16; i++)
        sakura_audio_engine_mix(engine, out, 128);

    sakura_audio_node_get_state(engine, voice, &state);
    CHECK_NEAR(state.amplitudeLeft, 0.5f, 1e-6);
    CHECK(sakura_audio_node_read_spectrum(engine, voice, bins, SAKURA_AUDIO_BINS) == SAKURA_AUDIO_BINS);

    // A little over a second of audio with no reads, and the meter has gone quiet rather than holding
    // its last reading.
    for (int i = 0; i < SAMPLE_RATE / 128 + 8; i++)
        sakura_audio_engine_mix(engine, out, 128);

    sakura_audio_node_get_state(engine, voice, &state);
    CHECK(state.running == 1);
    CHECK_NEAR(state.amplitudeLeft, 0.0f, 0.0);

    free(pcm);
    sakura_audio_engine_destroy(engine);
}

// A gain change ramps across the next block rather than stepping at its start, which on a fade is the
// difference between a smooth curve and a 128-frame staircase you can hear as zipper noise.
static void test_gain_change_ramps(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    const int frames = 1024;
    float pcm[1024 * CHANNELS];

    for (int i = 0; i < frames * CHANNELS; i++)
        pcm[i] = 0.5f;

    SakuraAudioHandle voice = attach_static_voice(engine, pcm, frames, NULL);

    float out[128 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 128);
    CHECK_NEAR(out[0], 0.5f, 0.0);

    sakura_audio_node_set_gain(engine, voice, 0.0f, 1.0f, 1.0f);
    sakura_audio_engine_mix(engine, out, 128);

    // Linear from where the last block ended to the new gain, arriving on the last frame.
    CHECK_NEAR(out[0], 0.5f * (1.0f - 1.0f / 128.0f), 1e-6);
    CHECK_NEAR(out[63 * 2], 0.5f * (1.0f - 64.0f / 128.0f), 1e-6);
    CHECK_NEAR(out[127 * 2], 0.0f, 1e-7);

    int rising = 0;

    for (int i = 1; i < 128; i++)
    {
        if (out[i * 2] > out[(i - 1) * 2])
            rising++;
    }

    CHECK(rising == 0);

    sakura_audio_engine_mix(engine, out, 128);
    CHECK_NEAR(out[0], 0.0f, 0.0);

    // A voice that starts, or seeks, snaps to its gain instead: there is no previous block of this
    // audio to ramp from. (test_gain_and_pan pins the same thing from the first block.)
    sakura_audio_node_set_gain(engine, voice, 1.0f, 1.0f, 1.0f);
    sakura_audio_node_seek(engine, voice, 0);
    sakura_audio_engine_mix(engine, out, 128);
    CHECK_NEAR(out[0], 0.5f, 0.0);

    sakura_audio_engine_destroy(engine);
}

// A full queue refuses the newest command and says so. It used to spin the calling thread for up to a
// million iterations and then overwrite an unconsumed command anyway.
static void test_full_command_queue_refuses(void)
{
    SakuraAudioConfig config;
    sakura_audio_config_defaults(&config);
    config.sampleRate = SAMPLE_RATE;
    config.channels = CHANNELS;
    config.maxCommands = 4;

    SakuraAudioEngine *engine = sakura_audio_engine_create(&config);
    CHECK(engine != NULL);

    float out[8 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 8); // drains nothing; the engine was created with an empty queue

    SakuraAudioHandle voice = sakura_audio_create_voice(engine);

    for (int i = 0; i < 4; i++)
        CHECK(sakura_audio_node_pause(engine, voice) == SAKURA_AUDIO_OK);

    CHECK(sakura_audio_node_pause(engine, voice) == SAKURA_AUDIO_FULL);

    SakuraAudioStats stats;
    sakura_audio_engine_get_stats(engine, &stats);
    CHECK(stats.commandsDropped == 1);

    sakura_audio_engine_mix(engine, out, 8);
    CHECK(sakura_audio_node_pause(engine, voice) == SAKURA_AUDIO_OK);

    sakura_audio_engine_destroy(engine);
}

// A filter left running over silence decays to exactly zero instead of trailing off through ever
// smaller values into the subnormal range, where every multiply on x86 takes a microcode assist. The
// slow poles make the difference visible at the output: without the floor, 60 blocks in, the tail is
// still around 1e-30 -- small, but a float, and nonzero.
static void test_filter_state_settles_to_zero(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    // A silent stream keeps the voice, and so its filter, running after the audio is gone. Both poles
    // at 0.99 (a1 = -1.98, a2 = 0.9801), so the tail is long.
    SakuraAudioHandle voice = sakura_audio_create_voice(engine);
    sakura_audio_voice_set_stream(engine, voice, 1024);
    sakura_audio_node_set_filter(engine, voice, 1, 0.0001f, 0.0002f, 0.0001f, -1.98f, 0.9801f);
    sakura_audio_add_child(engine, sakura_audio_engine_root(engine), voice);
    sakura_audio_node_play(engine, voice);

    float loud[128 * CHANNELS];

    for (int i = 0; i < 128 * CHANNELS; i++)
        loud[i] = 0.9f;

    float silence[128 * CHANNELS] = { 0 };
    float out[128 * CHANNELS];

    sakura_audio_stream_write(engine, voice, loud, 128);
    sakura_audio_engine_mix(engine, out, 128);

    for (int i = 0; i < 60; i++)
    {
        sakura_audio_stream_write(engine, voice, silence, 128);
        sakura_audio_engine_mix(engine, out, 128);
    }

    int nonzero = 0;

    for (int i = 0; i < 128 * CHANNELS; i++)
    {
        if (out[i] != 0.0f)
            nonzero++;
    }

    CHECK(nonzero == 0);

    sakura_audio_engine_destroy(engine);
}

// A node that missed a block -- here a bus with nothing to mix while it was muted -- takes its gain
// outright on the next block it mixes, rather than ramping from the gain it had before the gap: a
// muted bus must not let a block of the next hit through on its way down to silence.
static void test_gain_snaps_after_a_skipped_block(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    float pcm[128 * CHANNELS];

    for (int i = 0; i < 128 * CHANNELS; i++)
        pcm[i] = 0.5f;

    SakuraAudioHandle bus = sakura_audio_create_mixer(engine);
    sakura_audio_add_child(engine, sakura_audio_engine_root(engine), bus);

    SakuraAudioHandle buffer = sakura_audio_buffer_create(engine, pcm, 128);
    SakuraAudioHandle first = sakura_audio_create_voice(engine);
    sakura_audio_voice_set_buffer(engine, first, buffer);
    sakura_audio_add_child(engine, bus, first);
    sakura_audio_node_play(engine, first);

    float out[128 * CHANNELS];
    sakura_audio_engine_mix(engine, out, 128);
    CHECK_NEAR(out[0], 0.5f, 0.0);

    // The hit is over and gone; the bus has nothing to mix, and is muted while it has nothing.
    sakura_audio_destroy_node(engine, first);
    sakura_audio_engine_mix(engine, out, 128);
    sakura_audio_node_set_gain(engine, bus, 0.0f, 1.0f, 1.0f);
    sakura_audio_engine_mix(engine, out, 128);

    SakuraAudioHandle second = sakura_audio_create_voice(engine);
    sakura_audio_voice_set_buffer(engine, second, buffer);
    sakura_audio_add_child(engine, bus, second);
    sakura_audio_node_play(engine, second);
    sakura_audio_engine_mix(engine, out, 128);

    int audible = 0;

    for (int i = 0; i < 128 * CHANNELS; i++)
    {
        if (out[i] != 0.0f)
            audible++;
    }

    CHECK(audible == 0);

    sakura_audio_buffer_release(engine, buffer);
    sakura_audio_engine_destroy(engine);
}

// A stream that starves long enough to drain its window, then recovers, plays its fresh audio from the
// first new frame. It used to skip one frame per starved block, because the position kept advancing
// over frames that never existed.
static void test_stream_recovers_without_skipping(void)
{
    SakuraAudioEngine *engine = make_engine(128);

    SakuraAudioHandle voice = sakura_audio_create_voice(engine);
    sakura_audio_voice_set_stream(engine, voice, 4096);
    sakura_audio_add_child(engine, sakura_audio_engine_root(engine), voice);
    sakura_audio_node_play(engine, voice);

    float pcm[64 * CHANNELS];

    for (int i = 0; i < 64 * CHANNELS; i++)
        pcm[i] = 0.25f;

    float out[128 * CHANNELS];

    sakura_audio_stream_write(engine, voice, pcm, 64);
    sakura_audio_engine_mix(engine, out, 128);

    for (int i = 0; i < 20; i++)
        sakura_audio_engine_mix(engine, out, 128);

    float fresh[64 * CHANNELS];

    for (int i = 0; i < 64; i++)
    {
        fresh[i * 2] = 0.001f * (float)(i + 1);
        fresh[i * 2 + 1] = -fresh[i * 2];
    }

    sakura_audio_stream_write(engine, voice, fresh, 64);
    sakura_audio_engine_mix(engine, out, 128);

    int first = -1;

    for (int i = 0; i < 128; i++)
    {
        if (out[i * 2] != 0.0f)
        {
            first = i;
            break;
        }
    }

    CHECK(first >= 0);

    if (first >= 0)
    {
        // The drained window plays out its silence, then the fresh audio arrives from its first frame.
        CHECK_NEAR(out[first * 2], fresh[0], 0.0);
        CHECK_NEAR(out[(first + 1) * 2], fresh[2], 0.0);
    }

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
    RUN(test_aaudio_callback);
    RUN(test_audio_unit_callback);
    RUN(test_audio_unit_device_time);
    RUN(test_wasapi_shared_render_loop);
    RUN(test_wasapi_shared_fill);
    RUN(test_wasapi_exclusive_to_int16);
    RUN(test_wasapi_formats_and_widths);
    RUN(test_wasapi_failures);
    RUN(test_pipewire_process);
    RUN(test_unity_runs_and_rate_handover);
    RUN(test_resampler_paths_match_the_per_frame_algorithm);
    RUN(test_stream_runs_across_the_wrap);
    RUN(test_metering_runs_only_while_read);
    RUN(test_gain_change_ramps);
    RUN(test_gain_snaps_after_a_skipped_block);
    RUN(test_stream_recovers_without_skipping);
    RUN(test_full_command_queue_refuses);
    RUN(test_filter_state_settles_to_zero);

    printf("\n%d checks, %d failures\n", checks, failures);
    return failures == 0 ? 0 : 1;
}
