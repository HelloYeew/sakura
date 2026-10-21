// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

/// <summary>
/// A <see cref="VideoMirror"/> drawing a <see cref="VideoSprite"/>'s frames again, here under a blur.
/// </summary>
[TestFixture]
public partial class TestVideoMirror : TestScene
{
    [Resolved]
    private VideoStore videoStore { get; set; } = null!;

    private VideoSprite sprite = null!;
    private BufferedContainer blur = null!;
    private PausableMirror mirror = null!;
    private FrameWatch watch = null!;

    private void createVideoAndMirror()
    {
        AddStep("add a video and a blurred mirror of it", () =>
        {
            sprite = new VideoSprite(videoStore.GetDecoder("test.avi")!)
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.5f,
                FillMode = TextureFillMode.Fit
            };

            // Children update last-added first, so the watch (added first) looks once both have updated,
            // and the mirror (added last) updates before the video: a frame behind it, as a mirror
            // elsewhere in a scene can be.
            Add(watch = new FrameWatch(sprite));
            Add(sprite);

            // Masking, so that moving the mirror out of it takes it off the screen.
            Add(new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Child = blur = new BufferedContainer
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.5f,
                    FrameBufferScale = new Vector2(0.25f),
                    BlurSigma = new Vector2(20),
                    CacheDrawnFrameBuffer = true,
                    Child = mirror = new PausableMirror
                    {
                        RelativeSizeAxes = Axes.Both,
                        FillMode = TextureFillMode.Fill,
                        Source = sprite
                    }
                }
            });
        });

        AddUntilStep("the mirror shows a frame", () => mirror.Shown != null);
    }

    [Test]
    public void TestTheMirrorFollowsTheVideo()
    {
        IVideoTexture? first = null;

        createVideoAndMirror();

        AddStep("note the frame", () => first = mirror.Shown);
        AddUntilStep("the video moves on, and the mirror with it", () => mirror.Shown != first && mirror.Shown == sprite.DisplayedTexture);
    }

    /// <summary>
    /// A frame behind, the mirror's draw nodes hold frames the video has moved off. The video has to keep
    /// those out of its pool, or the mirror would draw whatever the decoder writes into them next.
    /// </summary>
    [Test]
    public void TestTheVideoKeepsTheFramesTheMirrorHolds()
    {
        createVideoAndMirror();

        AddStep("watch the mirror's frames", () => watch.Mirror = mirror);
        AddUntilStep("the video plays through its pool several times", () => watch.Changes > 40);
        AddAssert("every frame the mirror held was kept", () => !watch.MirrorFrameDropped);
    }

    /// <summary>
    /// Off the screen, the mirror is neither updated nor redrawn, so its draw nodes go on holding the frames
    /// they had. Those must not stay out of the video's pool while it is away: the video would be left a
    /// few frames short, which is the difference between smooth and stuttering.
    /// </summary>
    [Test]
    public void TestAMirrorOffTheScreenLetsGoOfItsFrames()
    {
        createVideoAndMirror();

        AddStep("move the mirror off the screen", () =>
        {
            blur.X = 10000;
            watch.WatchFor(mirror.Shown);
        });
        AddUntilStep("the frame it held comes back from the pool", () => watch.Reused);
    }

    [Test]
    public void TestAMirrorThatStopsUpdatingLetsGoOfItsFrames()
    {
        createVideoAndMirror();

        AddStep("stop the mirror", () =>
        {
            mirror.Paused = true;
            watch.WatchFor(mirror.Shown);
        });
        AddUntilStep("the frame it held comes back from the pool", () => watch.Reused);
    }

    [Test]
    public void TestTheMirrorOutlivesTheVideo()
    {
        IVideoTexture? frame = null;

        createVideoAndMirror();

        AddStep("remove the video", () =>
        {
            frame = mirror.Shown;
            Remove(sprite);
        });
        AddUntilStep("the mirror lets go of the frame", () => !mirror.Holds(frame!));
        AddWaitStep("keep drawing the mirror", 10);
    }

    /// <summary>
    /// Watches a video's frames, once it has updated.
    /// </summary>
    private partial class FrameWatch : Container
    {
        private readonly VideoSprite sprite;
        private readonly Queue<IVideoTexture> recent = new Queue<IVideoTexture>();
        private IVideoTexture? last;
        private IVideoTexture? watched;
        private bool movedOff;

        /// <summary>
        /// When set, checks every frame that the video still keeps every recent frame this mirror holds.
        /// </summary>
        public PausableMirror? Mirror { get; set; }

        public bool MirrorFrameDropped { get; private set; }

        public int Changes { get; private set; }

        /// <summary>
        /// Whether the frame given to <see cref="WatchFor"/> came back after the video had moved off it.
        /// </summary>
        public bool Reused { get; private set; }

        public FrameWatch(VideoSprite sprite)
        {
            this.sprite = sprite;
        }

        public void WatchFor(IVideoTexture? frame)
        {
            watched = frame;
            movedOff = sprite.DisplayedTexture != frame;
        }

        public override void Update()
        {
            base.Update();

            foreach (var held in recent)
            {
                if (Mirror != null && Mirror.Holds(held) && !sprite.StillReferenced(held))
                    MirrorFrameDropped = true;
            }

            var frame = sprite.DisplayedTexture;

            if (frame == last)
                return;

            last = frame;
            Changes++;

            if (frame != null)
            {
                recent.Enqueue(frame);

                // More than the pool holds, so every frame the mirror can still be drawing is in here.
                if (recent.Count > 12)
                    recent.Dequeue();
            }

            if (watched == null)
                return;

            if (frame != watched)
                movedOff = true;
            else if (movedOff)
                Reused = true;
        }
    }

    private partial class PausableMirror : VideoMirror
    {
        /// <summary>
        /// Stops updating, leaving the draw nodes on the frame they hold.
        /// </summary>
        public bool Paused { get; set; }

        /// <summary>
        /// The frame it read from the video when it last updated, which is the one it draws.
        /// </summary>
        public IVideoTexture? Shown { get; private set; }

        public override void Update()
        {
            if (Paused)
                return;

            base.Update();
            Shown = Source?.DisplayedTexture;
        }
    }
}
