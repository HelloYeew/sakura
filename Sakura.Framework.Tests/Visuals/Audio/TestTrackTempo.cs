// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Audio;

[TestFixture]
[VisualTestOnly("Requires the BASS audio backend / a real audio device")]
public partial class TestTrackTempo : TestScene
{
    [Resolved]
    private IAudioStore<ITrack> trackStore { get; set; } = null!;

    private IAudioChannel channel = null!;

    private void createChannel()
    {
        AddStep("Create and play track", () =>
        {
            channel = trackStore.Get("test.mp3").GetChannel();
            channel.Looping = true;
            channel.Play();
        });

        AddAssert("Tempo defaults to 1.0", () => channel.Tempo.Value == 1.0);
    }

    [Test]
    public void TestTempoDefaults()
    {
        createChannel();
        AddAssert("Frequency defaults to 1.0", () => channel.Frequency.Value == 1.0);
    }

    [TestCase(2.0)]
    [TestCase(1.5)]
    public void TestFasterTempoAdvancesPositionFaster(double tempo)
    {
        createChannel();

        // Measure how far the playback position moves over a fixed wall-clock window at 1.0x...
        double baselineAdvance = 0;
        double startAt1x = 0;
        AddStep("Record position @ 1.0x", () => startAt1x = channel.CurrentTime);
        AddWaitStep("Play 1000ms @ 1.0x", 1000);
        AddStep("Compute 1.0x advance", () => baselineAdvance = channel.CurrentTime - startAt1x);

        // ...then at the faster tempo, the position should advance noticeably more.
        double startAtFast = 0;
        AddStep($"Set tempo to {tempo}x", () => channel.Tempo.Value = tempo);
        AddStep("Record position @ fast", () => startAtFast = channel.CurrentTime);
        AddWaitStep("Play 1000ms @ fast", 1000);
        AddAssert($"Position advances faster at {tempo}x", () =>
        {
            double fastAdvance = channel.CurrentTime - startAtFast;
            // Allow generous slack for buffering/scheduling; just require it is clearly faster.
            return fastAdvance > baselineAdvance * 1.2;
        });
    }

    [Test]
    public void TestSlowerTempoAdvancesPositionSlower()
    {
        createChannel();

        double baselineAdvance = 0;
        double startAt1x = 0;
        AddStep("Record position @ 1.0x", () => startAt1x = channel.CurrentTime);
        AddWaitStep("Play 1000ms @ 1.0x", 1000);
        AddStep("Compute 1.0x advance", () => baselineAdvance = channel.CurrentTime - startAt1x);

        double startAtSlow = 0;
        AddStep("Set tempo to 0.5x", () => channel.Tempo.Value = 0.5);
        AddStep("Record position @ slow", () => startAtSlow = channel.CurrentTime);
        AddWaitStep("Play 1000ms @ slow", 1000);
        AddAssert("Position advances slower at 0.5x", () =>
        {
            double slowAdvance = channel.CurrentTime - startAtSlow;
            return slowAdvance < baselineAdvance * 0.8;
        });
    }

    [Test]
    public void TestTempoAndFrequencyAreIndependent()
    {
        createChannel();

        // Tempo (pitch-preserving) and Frequency (resampling) are separate reactives.
        AddStep("Set tempo 1.5x", () => channel.Tempo.Value = 1.5);
        AddStep("Set frequency 1.2x", () => channel.Frequency.Value = 1.2);
        AddAssert("Tempo retained", () => channel.Tempo.Value == 1.5);
        AddAssert("Frequency retained", () => channel.Frequency.Value == 1.2);
        AddWaitStep("Listen (fast + higher pitch)", 800);

        AddStep("Reset frequency", () => channel.Frequency.Value = 1.0);
        AddWaitStep("Listen (fast, normal pitch)", 800);
    }

    [Test]
    public void TestInteractiveTempo()
    {
        createChannel();

        // Drag to hear pitch-preserving speed changes in the runner.
        AddSliderStep("Tempo (x)", 0.25, 3.0, 1.0, v => channel.Tempo.Value = v);
    }

    [TearDown]
    public void TearDown()
    {
        AddStep("Clean up", () =>
        {
            channel?.Stop();
            channel?.Dispose();
            channel = null!;
        });
        AddStep("Clear scene", Clear);
    }
}
