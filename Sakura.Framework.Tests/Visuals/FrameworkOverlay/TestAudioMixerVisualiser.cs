// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

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

    [Resolved]
    private IAudioManager audioManager { get; set; }

    [Resolved]
    private IAudioStore<ITrack> trackStore { get; set; }

    [Resolved]
    private IAudioStore<ISample> sampleStore { get; set; }

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
        AddStep("Add overlay", () => Add(overlay));
        AddStep("Pop in overlay", () => overlay.ToggleVisibility());
        testTrack = trackStore.Get("test.mp3");
        testSample = sampleStore.Get("test.wav");
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
    }

    public TestAudioMixerVisualiser()
    {
    }
}
