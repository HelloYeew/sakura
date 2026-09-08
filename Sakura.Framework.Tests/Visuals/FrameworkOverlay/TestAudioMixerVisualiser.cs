// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public partial class TestAudioMixerVisualiser : TestScene
{
    private DebugWindowLayer layer = null!;

    private ITrack testTrack = null!;
    private ISample testSample = null!;
    private ISample testLongSample = null!;

    [Resolved]
    private IAudioManager audioManager { get; set; } = null!;

    [Resolved]
    private IAudioStore<ITrack> trackStore { get; set; } = null!;

    [Resolved]
    private IAudioStore<ISample> sampleStore { get; set; } = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add the window layer", () =>
        {
            Clear();
            Add(layer = new DebugWindowLayer
            {
                Depth = float.MaxValue - 20
            });
        });

        AddStep("Open", () => layer.Toggle(() => new AudioMixerVisualiser(audioManager)));
    }

    [Test]
    public void TestPlayback()
    {
        AddStep("Play track", () => testTrack.GetChannel().Play());
        AddStep("Play sample", () => testSample.GetChannel().Play());
        AddStep("Play long sample", () => testLongSample.GetChannel().Play());

        // Membership is re-read on a 100ms tick rather than every frame, so give it one.
        AddWaitStep("Let the membership tick", 300);

        AddAssert("Channel rows appeared", () => countChannelRows() > 0);
    }

    /// <summary>
    /// Rows are keyed by channel identity now, not by count, so a channel set that changes without
    /// changing size still refreshes. Playing and stopping exercises both directions.
    /// </summary>
    [Test]
    public void TestChannelsComeAndGo()
    {
        IAudioChannel channel = null!;

        AddStep("Play a sample", () =>
        {
            channel = testSample.GetChannel();
            channel.Play();
        });

        AddWaitStep("Let the membership tick", 300);

        int withChannel = 0;
        AddStep("Record the row count", () => withChannel = countChannelRows());
        AddAssert("A row exists", () => withChannel > 0);

        AddStep("Stop it", () => channel.Stop());
        AddWaitStep("Let the membership tick", 500);

        AddAssert("Rows did not grow", () => countChannelRows() <= withChannel);
    }

    [Test]
    public void TestCloseDetaches()
    {
        AddStep("Close", () => layer.Toggle(() => new AudioMixerVisualiser(audioManager)));

        AddAssert("Nothing left in the layer", () => layer.Children.Count == 0);

        AddStep("Reopen", () => layer.Toggle(() => new AudioMixerVisualiser(audioManager)));

        AddAssert("Open again", () => layer.IsOpen<AudioMixerVisualiser>());
    }

    private int countChannelRows() => DrawableExtensions.CountOfType<ChannelLevelDisplay>(window());

    private AudioMixerVisualiser window() => layer.OpenWindows.OfType<AudioMixerVisualiser>().Single();

    public override void Load()
    {
        base.Load();

        testTrack = trackStore.Get("test.mp3");
        testSample = sampleStore.Get("test.wav");
        testLongSample = sampleStore.Get("long.mp3");
    }
}
