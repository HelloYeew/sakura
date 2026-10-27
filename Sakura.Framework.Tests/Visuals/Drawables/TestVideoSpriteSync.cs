// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.Testing;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

/// <summary>
/// A <see cref="VideoSprite"/> following its clock (<see cref="VideoSprite.SyncToClock"/>), as a song's video follows the
/// song: at its speed, and moved with it.
/// </summary>
[TestFixture]
public partial class TestVideoSpriteSync : TestScene
{
    [Resolved]
    private VideoStore videoStore { get; set; } = null!;

    /// <summary>
    /// test.avi plays at 25 fps.
    /// </summary>
    private const double frame_ms = 40;

    /// <summary>
    /// Steps are timed on the test's clock, which runs ahead of real time headless, and the decoder runs in real time.
    /// </summary>
    private const double step_timeout = 100000;

    private VideoSprite sprite = null!;
    private VideoDecoder decoder = null!;
    private SongClock song = null!;

    private int generation;
    private readonly Stopwatch wait = new Stopwatch();

    /// <summary>
    /// Real time at a rate, moved and stopped by the test: a song.
    /// </summary>
    private sealed class SongClock : IFrameBasedClock
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private double baseTime;
        private double baseReal;
        private double rate = 1;
        private bool running;

        private double now => running ? baseTime + (watch.Elapsed.TotalMilliseconds - baseReal) * rate : baseTime;

        public double CurrentTime { get; private set; }
        public double Rate => rate;
        public bool IsRunning => running;
        public double ElapsedFrameTime { get; private set; }
        public double FramesPerSecond => 0;

        public void ProcessFrame()
        {
            double last = CurrentTime;
            CurrentTime = now;
            ElapsedFrameTime = CurrentTime - last;
        }

        private void rebase()
        {
            baseTime = now;
            baseReal = watch.Elapsed.TotalMilliseconds;
        }

        public void Play(double from, double atRate = 1)
        {
            baseTime = from;
            baseReal = watch.Elapsed.TotalMilliseconds;
            rate = atRate;
            running = true;
        }

        public void Stop()
        {
            rebase();
            running = false;
        }

        public void Move(double to)
        {
            rebase();
            baseTime = to;
        }
    }

    /// <summary>
    /// Moves the song on before the sprite reads it, as a game's music clock is processed ahead of what it drives.
    /// </summary>
    private sealed partial class SongVideo : VideoSprite
    {
        private readonly SongClock song;

        public SongVideo(VideoDecoder decoder, SongClock song)
            : base(decoder)
        {
            this.song = song;
            Clock = song;
            SyncToClock = true;
        }

        public override void UpdateSubTree()
        {
            song.ProcessFrame();
            base.UpdateSubTree();
        }
    }

    private void createVideo(string name)
    {
        AddStep($"add {name}, following a song", () =>
        {
            Clear();
            song = new SongClock();
            decoder = videoStore.GetDecoder(name)!;
            Add(sprite = new SongVideo(decoder, song)
            {
                RelativeSizeAxes = Axes.Both
            });
        });
        AddUntilStep("the video is ready", () => sprite.IsLoaded && sprite.Duration > 0, step_timeout);
    }

    private void waitFor(string description, double milliseconds)
    {
        AddStep($"{description}: start", () => wait.Restart());
        AddUntilStep(description, () => wait.Elapsed.TotalMilliseconds > milliseconds, double.MaxValue);
    }

    private void noteSeeks() => AddStep("note the seeks so far", () => generation = decoder.SeekGeneration);

    /// <summary>
    /// Whether the frame up is the song's: a frame goes up once the song reaches it and stays until the next, so it's a
    /// frame behind at most, and never ahead. Three are allowed, for a decoder thread held up for a moment.
    /// </summary>
    private bool inStep
    {
        get
        {
            double behind = song.CurrentTime - sprite.DisplayedFrameTime;
            return behind >= 0 && behind < frame_ms * 3;
        }
    }

    private void assertInStep()
    {
        int checkedFrames = 0;
        double worst = 0;

        AddUntilStep("the frame shown is the song's", () => inStep, step_timeout);

        AddStep("watch it: start", () =>
        {
            checkedFrames = 0;
            worst = 0;
            wait.Restart();
        });
        AddUntilStep("watch it for half a second", () =>
        {
            checkedFrames++;

            if (!inStep)
                worst = Math.Max(worst, Math.Abs(song.CurrentTime - sprite.DisplayedFrameTime));

            return wait.Elapsed.TotalMilliseconds > 500;
        }, double.MaxValue);
        AddStep("it stayed the song's", () => Assert.That(worst, Is.Zero, $"The frame shown was {worst:F0} ms off the song at worst, over {checkedFrames} frames."));
    }

    /// <summary>
    /// A song played faster or slower than normal, as a replay at another speed is. The video has to play at its speed:
    /// on its own clock it ran at normal speed and was sought back into place every quarter of a second.
    /// </summary>
    [TestCase(2.0)]
    [TestCase(0.5)]
    public void TestPlaysAtTheSongsSpeed(double rate)
    {
        createVideo("test.avi");

        AddStep($"play the song from 20 s at {rate}×", () => song.Play(20000, rate));
        assertInStep();
        noteSeeks();

        waitFor("play on for 2 s", 2000);
        AddAssert("never sought", () => decoder.SeekGeneration == generation);
        assertInStep();
    }

    /// <summary>
    /// A retry on a video whose first keyframe is a few frames in (late-keyframe.avi's is 160 ms): the seek back to the
    /// start can only land on that keyframe. The video used to take the first frame after a seek for the seek's target,
    /// so it ran that far ahead of the song for the rest of it.
    /// </summary>
    [Test]
    public void TestARetryStaysInStep()
    {
        createVideo("late-keyframe.avi");

        AddStep("play the song from 3 s", () => song.Play(3000));
        waitFor("play for a second", 1000);
        assertInStep();

        AddStep("retry: back to the start, stopped for the count-in", () =>
        {
            song.Stop();
            song.Move(0);
        });
        AddUntilStep("a frame from the start is up", () => sprite.DisplayedFrameTime < 500, step_timeout);
        waitFor("the count-in", 500);

        AddStep("the song starts", () => song.Play(0));
        waitFor("play for a second", 1000);
        assertInStep();
    }

    /// <summary>
    /// Clocks driven by audio move back a little now and then (a resync, a change of speed). Seeking for that started
    /// the decoder over; holding the frame until the song catches up is invisible.
    /// </summary>
    [Test]
    public void TestASmallStepBackIsNotASeek()
    {
        createVideo("test.avi");

        AddStep("play the song from 10 s", () => song.Play(10000));
        waitFor("play for a second", 1000);
        assertInStep();
        noteSeeks();

        AddStep("step the song back 50 ms", () => song.Move(song.CurrentTime - 50));
        waitFor("play on", 500);
        AddAssert("never sought", () => decoder.SeekGeneration == generation);
        assertInStep();
    }

    [Test]
    public void TestAJumpIsFollowed()
    {
        createVideo("test.avi");

        AddStep("play the song from 30 s", () => song.Play(30000));
        waitFor("play for a second", 1000);
        noteSeeks();

        AddStep("jump back to 5 s", () => song.Move(5000));
        AddUntilStep("sought", () => decoder.SeekGeneration != generation, step_timeout);
        assertInStep();

        noteSeeks();
        AddStep("jump ahead to 60 s", () => song.Move(60000));
        AddUntilStep("sought", () => decoder.SeekGeneration != generation, step_timeout);
        assertInStep();
    }

    /// <summary>
    /// A song moved past the end of its video, as one whose preview starts after the video ends: the video goes
    /// straight to its last frame, rather than playing its way there all at once.
    /// </summary>
    [Test]
    public void TestStartingPastTheEndShowsTheLastFrame()
    {
        int framesShown = 0;
        double lastShown = double.NaN;

        createVideo("late-keyframe.avi");

        AddStep("play the song from 5 s past the video's end", () => song.Play(sprite.Duration + 5000));
        AddUntilStep("the last frame comes up", () =>
        {
            if (!double.IsNaN(sprite.DisplayedFrameTime) && sprite.DisplayedFrameTime != lastShown)
            {
                lastShown = sprite.DisplayedFrameTime;
                framesShown++;
            }

            return sprite.DisplayedFrameTime > sprite.Duration - frame_ms * 3;
        }, step_timeout);
        AddStep("on the way, nothing else was", () => Assert.That(framesShown, Is.LessThanOrEqualTo(2), $"{framesShown} frames came up on the way to the last."));
    }

    /// <summary>
    /// A song longer than its video: past the end, the last frame stays up and nothing is sought.
    /// </summary>
    [Test]
    public void TestPastTheEndHoldsTheLastFrame()
    {
        createVideo("late-keyframe.avi");

        AddStep("play the song from just before the video ends", () => song.Play(sprite.Duration - 500));
        waitFor("play past the end", 1500);
        noteSeeks();
        waitFor("play on", 500);
        AddAssert("never sought", () => decoder.SeekGeneration == generation);
        AddAssert("the last frame is up", () => sprite.DisplayedFrameTime > sprite.Duration - frame_ms * 3);
    }
}
