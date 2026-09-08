// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// The header reads the engine's own statistics, which are registered on the engine's first update
    /// rather than at construction, so it has to keep re-reading them rather than snapshot on open.
    /// </summary>
    [Test]
    public void TestHeaderReportsTheEngine()
    {
        AddWaitStep("Let the header tick", 300);

        AddAssert("Engine line names the engine", () => window().EngineSummary.StartsWith("Engine: ", StringComparison.Ordinal));
        AddAssert("Engine line carries the volumes", () => window().EngineSummary.Contains("Master ", StringComparison.Ordinal));
        AddAssert("Device line carries a latency", () => window().DeviceSummary.Contains("Latency: ", StringComparison.Ordinal));
        AddAssert("Fault line said something", () => window().FaultSummary.Length > 0);
    }

    /// <summary>
    /// The header counts channels from the two mixer groups rather than reading
    /// <see cref="IAudioMixer.ActiveChannels"/> a second time, so that it cannot report a membership
    /// the rows underneath it disagree with — and so that it does not take the audio thread's lock
    /// again on its own tick. This scores the two staying in step, whether the engine under
    /// test routes anything into a mixer.
    /// </summary>
    [Test]
    public void TestHeaderChannelCountMatchesTheRows()
    {
        AddWaitStep("Let the membership and header tick", 300);

        AddAssert("Counts agree with the rows", () => headerChannelCount() == countChannelRows() - mixerCount());

        AddStep("Play a track", () => testTrack.GetChannel().Play());
        AddStep("Play a sample", () => testSample.GetChannel().Play());
        AddWaitStep("Let the membership and header tick", 300);

        AddAssert("Counts still agree with the rows", () => headerChannelCount() == countChannelRows() - mixerCount());
    }

    /// <summary>
    /// The two-channel figures the engine line ends with, added together.
    /// </summary>
    private int headerChannelCount()
    {
        var match = Regex.Match(window().EngineSummary, @"Channels: (\d+) track, (\d+) sample");

        Assert.That(match.Success, Is.True, $"the engine line lost its channel counts: {window().EngineSummary}");

        return int.Parse(match.Groups[1].Value) + int.Parse(match.Groups[2].Value);
    }

    /// <summary>
    /// A mixer gets a row of its own above its channels, so those rows have to come off the total
    /// before it can be compared against a channel count.
    /// </summary>
    private int mixerCount() => DrawableExtensions.CountOfType<MixerGroupDisplay>(window());

    [Test]
    public void TestCloseDetaches()
    {
        AddStep("Close", () => layer.Toggle(() => new AudioMixerVisualiser(audioManager)));

        AddUntilStep("Nothing left in the layer once the close animation ends", () => layer.Children.Count == 0);

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
