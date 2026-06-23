// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.BassEngine;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Audio;

[TestFixture]
[VisualTestOnly("Requires the BASS audio backend / a real audio device")]
public partial class TestLowPassFilter : TestScene
{
    [Resolved]
    private IAudioStore<ITrack> trackStore { get; set; } = null!;

    private ITrack track = null!;
    private IAudioChannel channel = null!;
    private BassLowPassFilter filter = null!;

    public override void Load()
    {
        base.Load();
        track = trackStore.Get("test.mp3");
    }

    private void createFilteredChannel()
    {
        AddStep("Get channel and play", () =>
        {
            channel = track.GetChannel();
            channel.Looping = true;
            channel.Play();
        });

        AddStep("Attach low-pass filter", () => filter = channel.AddLowPassFilter());
        AddAssert("Filter was created", () => filter != null);
    }

    [Test]
    public void TestConstruction()
    {
        createFilteredChannel();

        AddAssert("Cutoff starts at default",
            () => filter.CutoffFrequency.Value == BassLowPassFilter.DefaultCutoffFrequency);
    }

    [Test]
    public void TestCutoffSweep()
    {
        createFilteredChannel();

        // Audible/visible sweep: open -> closed -> open.
        AddStep("Cutoff 20000Hz (open)", () => filter.CutoffFrequency.Value = 20000);
        AddWaitStep("Listen", 600);
        AddStep("Cutoff 2000Hz", () => filter.CutoffFrequency.Value = 2000);
        AddWaitStep("Listen", 600);
        AddStep("Cutoff 500Hz (muffled)", () => filter.CutoffFrequency.Value = 500);
        AddWaitStep("Listen", 600);
        AddAssert("Cutoff is 500Hz", () => filter.CutoffFrequency.Value == 500);
        AddStep("Cutoff 20000Hz (open)", () => filter.CutoffFrequency.Value = 20000);
        AddWaitStep("Listen", 600);
        AddAssert("Cutoff is 20000Hz", () => filter.CutoffFrequency.Value == 20000);
    }

    [Test]
    public void TestInteractiveCutoff()
    {
        createFilteredChannel();

        AddSliderStep("Cutoff frequency (Hz)", 1.0, 22050.0, BassLowPassFilter.DefaultCutoffFrequency,
            v => filter.CutoffFrequency.Value = v);
    }

    [TestCase(500.0)]
    [TestCase(5000.0)]
    [TestCase(20000.0)]
    public void TestReactiveBinding(double cutoff)
    {
        createFilteredChannel();

        AddStep($"Set cutoff to {cutoff}Hz", () => filter.CutoffFrequency.Value = cutoff);
        AddAssert("Reactive value updated", () => filter.CutoffFrequency.Value == cutoff);
        AddWaitStep("Let the filter settle", 300);
    }

    [Test]
    public void TestReset()
    {
        createFilteredChannel();

        AddStep("Set cutoff to 500Hz", () => filter.CutoffFrequency.Value = 500);
        AddAssert("Cutoff is 500Hz", () => filter.CutoffFrequency.Value == 500);
        AddStep("Reset filter", () => filter.Reset());
        AddAssert("Cutoff back to default",
            () => filter.CutoffFrequency.Value == BassLowPassFilter.DefaultCutoffFrequency);
    }

    [Test]
    public void TestDispose()
    {
        createFilteredChannel();

        AddWaitStep("Let it play briefly", 400);
        AddStep("Dispose filter", () => filter.Dispose());
        AddStep("Dispose filter again (must be safe)", () => filter.Dispose());
        AddWaitStep("Ensure no background crash", 400);
    }

    [TearDown]
    public void TearDown()
    {
        AddStep("Stop and clean up channel", () =>
        {
            filter?.Dispose();
            channel?.Stop();
            channel?.Dispose();
        });
        AddStep("Clear scene", Clear);
    }
}
