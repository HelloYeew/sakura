// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Graphics.Audio;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Audio;

[TestFixture]
[VisualTestOnly("Requires the BASS audio backend / a real audio device")]
public partial class TestAudioVisualizer : TestScene
{
    [Resolved]
    private IAudioStore<ITrack> trackStore { get; set; } = null!;

    private AudioVisualizer visualizer = null!;
    private IAudioChannel channel = null!;

    private void createVisualizer()
    {
        AddStep("Add visualizer", () =>
        {
            visualizer = new AudioVisualizer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.8f)
            };
            Add(visualizer);
        });

        AddStep("Play track and attach as source", () =>
        {
            channel = trackStore.Get("test.mp3").GetChannel();
            channel.Looping = true;
            channel.Play();
            visualizer.ChangeSource(channel);
        });

        AddAssert("Visualizer has one source", () => visualizer.SourceCount == 1);
    }

    [Test]
    public void TestAmplitudeApiReturnsSpectrum()
    {
        createVisualizer();

        // The new API contract: a playing BASS channel exposes a correctly sized spectrum.
        AddAssert("Spectrum is the expected size",
            () => channel.CurrentAmplitudes.FrequencyAmplitudes.Length == ChannelAmplitudes.AMPLITUDES_SIZE);

        // While the track plays, at least one frequency bin should be non-zero.
        AddUntilStep("Spectrum has energy while playing", () =>
        {
            var span = channel.CurrentAmplitudes.FrequencyAmplitudes.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] > 0f)
                    return true;
            }

            return false;
        });
    }

    [Test]
    public void TestBarsReactToAudio()
    {
        createVisualizer();

        // The visualiser should pick up energy from the spectrum and raise at least one bar.
        AddUntilStep("At least one bar rises", () =>
        {
            for (int i = 0; i < 150; i++)
            {
                if (visualizer.GetBarValue(i) > 0.01f)
                    return true;
            }

            return false;
        });
    }

    [Test]
    public void TestBarsDecayWhenStopped()
    {
        createVisualizer();

        AddUntilStep("Bars are active", () =>
        {
            for (int i = 0; i < 150; i++)
            {
                if (visualizer.GetBarValue(i) > 0.05f)
                    return true;
            }

            return false;
        });

        AddStep("Stop the track", () => channel.Stop());

        // Stop() is applied asynchronously on the audio thread; wait for it to take effect
        // so the channel reports silence before we expect the bars to decay.
        AddUntilStep("Channel reports stopped", () => !channel.IsRunning.Value);

        // With no input energy, every bar should decay back toward zero.
        AddUntilStep("All bars decay to ~zero", () =>
        {
            for (int i = 0; i < 150; i++)
            {
                if (visualizer.GetBarValue(i) > 0.01f)
                    return false;
            }

            return true;
        }, timeout: 20000);
    }

    [Test]
    public void TestClearSources()
    {
        createVisualizer();

        AddStep("Clear amplitude sources", () => visualizer.ClearAmplitudeSources());
        AddAssert("No sources remain", () => visualizer.SourceCount == 0);
        AddUntilStep("Bars decay with no source", () =>
        {
            for (int i = 0; i < 150; i++)
            {
                if (visualizer.GetBarValue(i) > 0.01f)
                    return false;
            }

            return true;
        });
    }

    [Test]
    public void TestHeadlessSourceIsSilent()
    {
        // A non-BASS source returns ChannelAmplitudes.Empty, so no bars should ever rise.
        AddStep("Add visualizer", () =>
        {
            visualizer = new AudioVisualizer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.8f)
            };
            Add(visualizer);
        });

        AddAssert("Empty amplitudes report zero energy", () =>
        {
            var empty = ChannelAmplitudes.Empty;
            if (empty.FrequencyAmplitudes.Length != ChannelAmplitudes.AMPLITUDES_SIZE)
                return false;

            var span = empty.FrequencyAmplitudes.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] != 0f)
                    return false;
            }

            return true;
        });
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
