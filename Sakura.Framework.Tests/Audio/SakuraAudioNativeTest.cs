// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Audio.SdlEngine;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// The <c>libsakura-audio</c> boundary: that the P/Invoke surface and struct layouts agree with the
/// native side, and that the native mixer produces the same audio as the managed one it replaces.
/// </summary>
/// <remarks>
/// <para>
/// The parity tests are the point of this fixture. The managed mixer in
/// <see cref="Sakura.Framework.Audio.SdlEngine"/> is the reference implementation and is not going
/// away — it is the fallback wherever the native library is missing — so "the native engine is
/// correct" means "the native engine agrees with it sample for sample".
/// </para>
/// <para>
/// Inconclusive rather than failing where the native library is not present: it ships per RID, and a
/// platform that has not built or packaged it yet is a known state, not a broken one.
/// </para>
/// </remarks>
[TestFixture]
public class SakuraAudioNativeTest
{
    private const int rate = 44100;
    private const int channels = 2;

    /// <summary>
    /// Stands in for the manager when driving a managed channel: runs queued actions inline, since a
    /// test has no audio thread to marshal them to.
    /// </summary>
    private sealed class StubContext : ISDLAudioContext
    {
        public int SampleRate => rate;
        public int Channels => channels;

        /// <summary>
        /// Output latency to report, so a test can pin position compensation.
        /// </summary>
        public double OutputLatencyMs { get; set; }

        public void EnqueueAction(Action action) => action();
        public void WakeDecoder() { }
    }

    [OneTimeSetUp]
    public void CheckAvailability()
    {
        if (!SakuraAudioEngine.IsAvailable)
        {
            Assert.Ignore("libsakura-audio is not available for this platform. Build it with " +
                          "'cmake -S native/sakura-audio -B native/sakura-audio/build && cmake --build native/sakura-audio/build' and rerun.");
        }
    }

    private static SakuraAudioEngine createEngine(int mixBlockFrames = 128) =>
        SakuraAudioEngine.Create(rate, channels, mixBlockFrames)
        ?? throw new InvalidOperationException("The native engine refused a valid configuration.");

    /// <summary>
    /// A ramp, small enough to stay well inside the output clamp so that the clamp is not what a
    /// parity check ends up measuring.
    /// </summary>
    private static float[] makeRamp(int frames)
    {
        float[] pcm = new float[frames * channels];

        for (int i = 0; i < frames; i++)
        {
            pcm[i * 2] = 0.4f * MathF.Sin(2f * MathF.PI * 8f * i / frames);
            pcm[i * 2 + 1] = 0.3f * MathF.Cos(2f * MathF.PI * 5f * i / frames);
        }

        return pcm;
    }

    #region Boundary

    [Test]
    public void TestAbiVersionMatches()
    {
        // The first thing managed checks, and the reason it is checked: every struct in the bindings
        // is a layout contract with the shipped library.
        Assert.That(SakuraAudioNative.sakura_audio_abi_version(), Is.EqualTo(SakuraAudioNative.ABI_VERSION));
    }

    [Test]
    public void TestEngineLifetime()
    {
        using var engine = createEngine();

        Assert.That(engine.Handle, Is.Not.EqualTo(nint.Zero));
        Assert.That(engine.Root, Is.Not.Zero);
        Assert.That(engine.SampleRate, Is.EqualTo(rate));
        Assert.That(engine.Channels, Is.EqualTo(channels));
        Assert.That(SakuraAudioEngine.StreamCallback, Is.Not.EqualTo(nint.Zero));

        // Disposal is idempotent, and everything after it is inert rather than a use-after-free.
        engine.Dispose();
        engine.Dispose();

        Assert.That(engine.CreateVoice(), Is.Zero);
        Assert.That(engine.Mix(new float[16]), Is.Zero);
    }

    [Test]
    public void TestNodePoolIsBounded()
    {
        using var engine = SakuraAudioEngine.Create(rate, channels, 64, maxNodes: 4)
                           ?? throw new InvalidOperationException("The native engine refused a valid configuration.");

        // Three, not four: the root mixer holds a slot. Preallocated means it refuses rather than
        // allocating while the device callback is running.
        Assert.That(engine.CreateVoice(), Is.Not.Zero);
        Assert.That(engine.CreateVoice(), Is.Not.Zero);
        Assert.That(engine.CreateVoice(), Is.Not.Zero);
        Assert.That(engine.CreateVoice(), Is.Zero);
    }

    [Test]
    public void TestSdlPutResolves()
    {
        // The one thing the native library needs from SDL, found by export lookup rather than linked.
        // If this breaks, the native engine mixes into nowhere.
        Assert.That(SakuraAudioEngine.TrySetSdlPut(), Is.True);
    }

    [Test]
    public void TestNodeStateAndStatsMarshalling()
    {
        using var engine = createEngine();

        float[] pcm = makeRamp(256);
        uint buffer = engine.CreateBuffer(pcm);
        uint voice = engine.CreateVoice();

        Assert.That(buffer, Is.Not.Zero);
        Assert.That(voice, Is.Not.Zero);
        Assert.That(engine.SetVoiceBuffer(voice, buffer), Is.True);
        Assert.That(engine.AddChild(engine.Root, voice), Is.True);

        engine.Play(voice);
        engine.Mix(new float[128 * channels]);

        Assert.That(engine.TryGetState(voice, out var state), Is.True);
        Assert.That(state.Running, Is.EqualTo(1));
        Assert.That(state.Ended, Is.Zero);
        Assert.That(state.EndEpoch, Is.Zero);
        Assert.That(state.SourceFrames, Is.GreaterThanOrEqualTo(128));
        Assert.That(state.AmplitudeLeft, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.4f));
        Assert.That(state.AmplitudeRight, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.3f));

        var stats = engine.GetStats();
        Assert.That(stats.FramesMixed, Is.EqualTo(128));
        Assert.That(stats.ActiveVoices, Is.EqualTo(1));
        Assert.That(stats.CommandsDropped, Is.Zero);
        Assert.That(stats.Starvations, Is.Zero);

        // A stale handle resolves to nothing rather than to whatever takes the slot next.
        engine.DestroyNode(voice);
        engine.Mix(new float[128 * channels]);
        engine.Maintain();

        Assert.That(engine.TryGetState(voice, out _), Is.False);

        engine.ReleaseBuffer(buffer);
    }

    #endregion

    #region Parity with the managed mixer

    /// <summary>
    /// Renders the same PCM through the managed channel and through a native voice and returns both
    /// blocks, so a test only has to say what it configured.
    /// </summary>
    private static (float[] Managed, float[] Native) renderBoth(
        float[] pcm,
        int frames,
        double rateRatio = 1.0,
        double volume = 1.0,
        double balance = 0.0,
        double? cutoff = null)
    {
        float[] managed = new float[frames * channels];
        float[] native = new float[frames * channels];

        var buffer = PcmBuffer.FromSamples(pcm, channels, rate);
        var channel = new SDLAudioChannel(new StubContext(), new MemoryPcmSource(buffer));

        channel.Volume.Value = volume;
        channel.Balance.Value = balance;
        channel.Frequency.Value = rateRatio;

        using var engine = createEngine(frames);

        uint handle = engine.CreateBuffer(pcm);
        uint voice = engine.CreateVoice();

        engine.SetVoiceBuffer(voice, handle);
        engine.AddChild(engine.Root, voice);
        engine.SetRate(voice, rateRatio);

        // The pan law lives on the managed side, next to the BASS backend's, and reaches the native
        // engine as two per-side gains. Reading it back off the channel is what keeps the two in step.
        double clamped = Math.Clamp(balance, -1.0, 1.0);
        float panLeft = (float)(clamped <= 0 ? 1.0 : 1.0 - clamped);
        float panRight = (float)(clamped >= 0 ? 1.0 : 1.0 + clamped);
        engine.SetGain(voice, (float)volume, panLeft, panRight);

        if (cutoff.HasValue)
        {
            var filter = channel.AttachLowPassFilter();
            filter.CutoffFrequency.Value = cutoff.Value;

            var (enabled, coefficients) = filter.CurrentCoefficients;
            engine.SetFilter(voice, enabled, coefficients.B0, coefficients.B1, coefficients.B2, coefficients.A1, coefficients.A2);
        }

        channel.Play();
        engine.Play(voice);

        channel.Fill(managed);
        engine.Mix(native);

        channel.Dispose();

        return (managed, native);
    }

    [TestCase(1.0, 1.0, 0.0)]
    [TestCase(1.0, 0.5, 0.0)]
    [TestCase(1.0, 1.0, -0.6)]
    [TestCase(1.0, 0.75, 0.4)]
    [TestCase(0.5, 1.0, 0.0)]
    [TestCase(1.25, 1.0, 0.0)]
    [TestCase(2.0, 1.0, 0.0)]
    public void TestMixMatchesManagedMixer(double rateRatio, double volume, double balance)
    {
        const int frames = 512;

        var (managed, native) = renderBoth(makeRamp(frames), frames, rateRatio, volume, balance);

        // Not "close enough": both sides run the same arithmetic in the same order, so a difference
        // here is a difference in the algorithm rather than in the precision.
        for (int i = 0; i < managed.Length; i++)
            Assert.That(native[i], Is.EqualTo(managed[i]).Within(1e-6f), $"sample {i}");
    }

    [Test]
    public void TestFilteredMixMatchesManagedMixer()
    {
        const int frames = 512;

        var (managed, native) = renderBoth(makeRamp(frames), frames, cutoff: 800.0);

        for (int i = 0; i < managed.Length; i++)
            Assert.That(native[i], Is.EqualTo(managed[i]).Within(1e-6f), $"sample {i}");
    }

    [Test]
    public void TestSpectrumMatchesManagedFft()
    {
        const int frames = 512;
        const int bin = 24;

        float[] pcm = new float[frames * channels];
        float[] mono = new float[frames];

        for (int i = 0; i < frames; i++)
        {
            float sample = MathF.Sin(2f * MathF.PI * bin * i / frames);
            pcm[i * 2] = pcm[i * 2 + 1] = sample;
            mono[i] = sample;
        }

        using var engine = createEngine(frames);

        uint buffer = engine.CreateBuffer(pcm);
        uint voice = engine.CreateVoice();

        engine.SetVoiceBuffer(voice, buffer);
        engine.AddChild(engine.Root, voice);
        engine.Play(voice);
        engine.Mix(new float[frames * channels]);

        float[] nativeBins = new float[SakuraAudioNative.BIN_COUNT];
        Assert.That(engine.ReadSpectrum(voice, nativeBins), Is.EqualTo(SakuraAudioNative.BIN_COUNT));

        float[] managedBins = new float[AudioFft.BIN_COUNT];
        new AudioFft().Compute(mono, managedBins);

        // Two independent transforms of the same window -- the native one against a precomputed
        // twiddle table, the managed one computing them per butterfly -- so this is a real
        // cross-check of the scaling, the window and the bin layout rather than a tautology.
        Assert.That(nativeBins[bin], Is.EqualTo(1f).Within(0.05f));

        for (int i = 0; i < managedBins.Length; i++)
            Assert.That(nativeBins[i], Is.EqualTo(managedBins[i]).Within(1e-4f), $"bin {i}");

        engine.ReleaseBuffer(buffer);
    }

    #endregion

    #region Streaming

    [Test]
    public void TestStreamingRoundTrip()
    {
        using var engine = createEngine();

        uint voice = engine.CreateVoice();
        Assert.That(engine.SetVoiceStream(voice, 1024), Is.True);
        Assert.That(engine.AddChild(engine.Root, voice), Is.True);

        engine.Play(voice);

        Assert.That(engine.StreamSpace(voice), Is.EqualTo(1024));
        Assert.That(engine.StreamBuffered(voice), Is.Zero);

        float[] pcm = new float[256 * channels];
        Array.Fill(pcm, 0.25f);

        Assert.That(engine.StreamWrite(voice, pcm), Is.EqualTo(256));
        Assert.That(engine.StreamBuffered(voice), Is.EqualTo(256));

        float[] output = new float[128 * channels];
        engine.Mix(output);

        Assert.That(output[0], Is.EqualTo(0.25f).Within(1e-6f));

        // Drained *and* empty is the end; drained with audio still buffered is not.
        engine.StreamSetDrained(voice, true);
        engine.Mix(output);
        engine.Mix(output);

        Assert.That(engine.TryGetState(voice, out var state), Is.True);
        Assert.That(state.Ended, Is.EqualTo(1));
        Assert.That(state.EndEpoch, Is.EqualTo(1));
        Assert.That(state.Running, Is.Zero);
    }

    [Test]
    public void TestStreamFlushWaitsForTheAudioThread()
    {
        using var engine = createEngine();

        uint voice = engine.CreateVoice();
        engine.SetVoiceStream(voice, 1024);
        engine.AddChild(engine.Root, voice);
        engine.Play(voice);

        float[] stale = new float[512 * channels];
        Array.Fill(stale, 0.5f);
        engine.StreamWrite(voice, stale);

        float[] output = new float[128 * channels];
        engine.Mix(output);

        Assert.That(engine.StreamFlushPending(voice), Is.False);

        engine.StreamFlushBegin(voice);

        // Until the audio thread has acknowledged, a write would be discarded along with the audio
        // being flushed, so it is refused outright.
        Assert.That(engine.StreamFlushPending(voice), Is.True);
        Assert.That(engine.StreamWrite(voice, stale), Is.Zero);
        Assert.That(engine.StreamSpace(voice), Is.Zero);

        engine.Mix(output);

        Assert.That(engine.StreamFlushPending(voice), Is.False);
        Assert.That(engine.StreamBuffered(voice), Is.Zero);

        float[] fresh = new float[128 * channels];
        Array.Fill(fresh, -0.75f);

        Assert.That(engine.StreamWrite(voice, fresh), Is.EqualTo(128));
        engine.Mix(output);

        // What comes out is the new position, not a fragment of the old one.
        Assert.That(output[0], Is.EqualTo(-0.75f).Within(1e-6f));
    }

    [Test]
    public void TestStarvationIsReportedRatherThanTreatedAsAnEnd()
    {
        using var engine = createEngine();

        uint voice = engine.CreateVoice();
        engine.SetVoiceStream(voice, 1024);
        engine.AddChild(engine.Root, voice);
        engine.Play(voice);

        float[] output = new float[128 * channels];
        engine.Mix(output);
        engine.Mix(output);

        Assert.That(engine.GetStats().Starvations, Is.GreaterThan(0));

        Assert.That(engine.TryGetState(voice, out var state), Is.True);
        Assert.That(state.Ended, Is.Zero);
        Assert.That(state.EndEpoch, Is.Zero);
        Assert.That(state.Running, Is.EqualTo(1));
        Assert.That(output[0], Is.Zero);
    }

    #endregion
}
