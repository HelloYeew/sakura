// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Audio.Headless;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// Testing for making <see cref="Sakura.Framework.Audio.IAudioManager.Update"/>
/// run on the audio thread. Normally user-facing action will run through
/// <see cref="Sakura.Framework.Audio.IAudioManager.EventScheduler"/>
/// </summary>
[TestFixture]
public class AudioEventSchedulerTest
{
    private HeadlessAudioManager manager;
    private ManualClock clock;
    private Scheduler scheduler;

    private const double channel_length_ms = 100;

    [SetUp]
    public void SetUp()
    {
        manager = new HeadlessAudioManager();
        clock = new ManualClock();
        scheduler = new Scheduler(clock);
    }

    [TearDown]
    public void TearDown() => manager.Dispose();

    private HeadlessAudioChannel createChannel()
    {
        var channel = new HeadlessAudioChannel(channel_length_ms, manager);
        manager.RegisterChannel(channel);
        return channel;
    }

    [Test]
    public void TestEventsWaitForTheEventScheduler()
    {
        manager.EventScheduler = scheduler;

        var channel = createChannel();

        int started = 0;
        int ended = 0;
        channel.OnStart += () => started++;
        channel.OnEnd += () => ended++;

        channel.Play();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(started, Is.Zero, "the caller's thread should not be handed the event inline");
            Assert.That(channel.IsRunning.Value, Is.True, "internal state is not deferred");
        }

        scheduler.Update();
        Assert.That(started, Is.EqualTo(1));

        // One audio frame long enough to run the channel off its end. This stands in for the frame
        // AppHost.PerformSoundUpdate runs on the audio thread.
        manager.Update(channel_length_ms * 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ended, Is.Zero, "the audio frame must not raise the event itself");
            Assert.That(channel.IsRunning.Value, Is.False, "the channel's own state still settles within the frame");
        }

        scheduler.Update();
        Assert.That(ended, Is.EqualTo(1));
    }

    /// <summary>
    /// No scheduler means no thread to marshal to, so events fire where they are raised. This is what
    /// a channel built outside a host gets, and what every audio test relies on.
    /// </summary>
    [Test]
    public void TestEventsFireInlineWithoutAnEventScheduler()
    {
        Assert.That(manager.EventScheduler, Is.Null);

        var channel = createChannel();

        int ended = 0;
        channel.OnEnd += () => ended++;

        channel.Play();
        manager.Update(channel_length_ms * 2);

        Assert.That(ended, Is.EqualTo(1));
    }

    /// <summary>
    /// The scheduler defers, it does not drop. A handler still attached when the frame ran must be
    /// reached even though the channel has moved on since.
    /// </summary>
    [Test]
    public void TestDeferredEventsSurviveDisposalOfTheChannel()
    {
        manager.EventScheduler = scheduler;

        var channel = createChannel();

        int ended = 0;
        channel.OnEnd += () => ended++;

        channel.Play();
        manager.Update(channel_length_ms * 2);

        // Disposal clears the channel's delegates. The pending event was captured before it was
        // scheduled precisely so this cannot swallow it.
        channel.Dispose();

        scheduler.Update();
        Assert.That(ended, Is.EqualTo(1));
    }
}
