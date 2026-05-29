// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities; // For Precision.AlmostEquals if needed

namespace Sakura.Framework.Tests.Visuals.Drawables;

[TestFixture]
public class TestVideoSprite : TestScene
{
    private VideoSprite videoSprite;

    [Resolved]
    private VideoStore videoStore { get; set; } = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Clear scene", () => Clear());
    }

    private void createVideo()
    {
        AddStep("Add VideoSprite", () =>
        {
            videoSprite = new VideoSprite(videoStore.GetDecoder("test.avi"))
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Color = Color.White,
                FillMode = TextureFillMode.Fit
            };
            Add(videoSprite);
        });

        AddAssert("VideoSprite is loaded", () => videoSprite.IsLoaded);
    }

    [Test]
    public void TestBasicPlayback()
    {
        createVideo();
    }

    [Test]
    public void TestPlaybackControls()
    {
        createVideo();

        AddAssert("Video is playing by default", () => videoSprite.IsPlaying);

        AddWaitStep("Wait 500ms", 500);
        AddAssert("Time has advanced", () => videoSprite.CurrentTime > 0);

        AddStep("Pause video", () => videoSprite.Pause());
        AddAssert("Video is paused", () => !videoSprite.IsPlaying);

        double pausedTime = 0;
        AddStep("Record paused time", () => pausedTime = videoSprite.CurrentTime);
        AddWaitStep("Wait 500ms", 500);
        AddAssert("Time did not advance", () => Precision.AlmostEquals(pausedTime, videoSprite.CurrentTime));

        AddStep("Resume video", () => videoSprite.Play());
        AddAssert("Video is playing again", () => videoSprite.IsPlaying);

        AddStep("Stop video", () => videoSprite.Stop());
        AddAssert("Video stopped and time reset", () => !videoSprite.IsPlaying && videoSprite.CurrentTime == 0);
    }

    [Test]
    public void TestSeeking()
    {
        createVideo();

        AddStep("Pause video", () => videoSprite.Pause());

        AddStep("Seek to 2000ms", () => videoSprite.Seek(2000));
        AddAssert("Time is exactly 2000ms", () => Precision.AlmostEquals(videoSprite.CurrentTime, 2000));

        AddWaitStep("Observe new frame", 500);

        AddStep("Seek to 5000ms", () => videoSprite.Seek(5000));
        AddAssert("Time is exactly 5000ms", () => Precision.AlmostEquals(videoSprite.CurrentTime, 5000));
    }

    [Test]
    public void TestTransformationsAndColor()
    {
        createVideo();

        AddStep("Rotate video 45 degrees", () => videoSprite.Rotation = 45f);
        AddStep("Scale down video", () => videoSprite.Scale = new Vector2(0.5f));
        AddStep("Tint video Cyan", () => videoSprite.Color = Color.Cyan);

        AddWaitStep("Wait to observe transforms", 1000);

        AddStep("Reset transformations", () =>
        {
            videoSprite.Rotation = 0f;
            videoSprite.Scale = Vector2.One;
            videoSprite.Color = Color.White;
        });
    }

    [Test]
    public void TestSafeDisposal()
    {
        createVideo();

        AddWaitStep("Let video play briefly", 500);

        AddStep("Remove from scene", () => Remove(videoSprite));

        AddStep("Force Dispose", () => videoSprite.Dispose());

        AddWaitStep("Wait to ensure no background crashes", 500);
    }

    [Test]
    public void TestTextureFillModes()
    {
        createVideo();

        AddStep("Set FillMode to Stretch", () => videoSprite.FillMode = TextureFillMode.Stretch);
        AddWaitStep("Observe Stretch mode", 500);

        AddStep("Set FillMode to Fit", () => videoSprite.FillMode = TextureFillMode.Fit);
        AddWaitStep("Observe Fit mode", 500);

        AddStep("Set FillMode to Fill", () => videoSprite.FillMode = TextureFillMode.Fill);
        AddWaitStep("Observe Fill mode", 500);

        AddStep("Set FillMode to Tile", () => videoSprite.FillMode = TextureFillMode.Tile);
        AddWaitStep("Observe Tile mode", 500);
    }
}
