// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public class TestAudioMixerVisualiser : TestScene
{
    private AudioMixerVisualiser overlay;

    private ITrack testTrack;
    private ISample testSample;
    private ISample testLongSample;

    [Resolved]
    private IAudioManager audioManager { get; set; }

    [Resolved]
    private IAudioStore<ITrack> trackStore { get; set; }

    [Resolved]
    private IAudioStore<ISample> sampleStore { get; set; }

    [SetUp]
    public void SetUp()
    {
        AddStep("Add overlay", () => Add(overlay));
        AddStep("Pop in overlay", () => overlay.ToggleVisibility());
    }

    [Test]
    public void TestPlayback()
    {
        AddStep("Play track", () =>
        {
            var trackChannel = testTrack.GetChannel();
            trackChannel.Play();
        });
        AddStep("Play sample", () =>
        {
            var sampleChannel = testSample.GetChannel();
            sampleChannel.Play();
        });
        AddStep("Play long sample", () =>
        {
            var longSampleChannel = testLongSample.GetChannel();
            longSampleChannel.Play();
        });
    }

    public override void Load()
    {
        base.Load();
        overlay = new AudioMixerVisualiser(audioManager)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Depth = float.MaxValue - 20
        };
        testTrack = trackStore.Get("test.mp3");
        testSample = sampleStore.Get("test.wav");
        testLongSample = sampleStore.Get("long.mp3");
    }
}
