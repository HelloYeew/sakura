// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Verifies decoded H.264 frames use a matching texture layout and skip unnecessary conversion,
/// including hardware-decoded NV12 frames.
/// </summary>
[TestFixture]
public class VideoPlaneLayoutTest
{
    /// <summary>
    /// H.264 rather than the <c>test.avi</c> the other video tests use: that file's codec falls to
    /// software decode, which produces YUV420P and so never exercises this path at all.
    /// </summary>
    private const string video_resource = "Sakura.Framework.Tests.Resources.Videos.test-h264.mp4";

    private const int decode_timeout_ms = 10000;

    /// <summary>
    /// Frames to let through before reading the timings. The averages seed on their first sample, and
    /// the first decode of a process carries the JIT of everything below it — so one frame says
    /// nothing about whether the conversion step is doing work.
    /// </summary>
    private const int frames_to_settle = 20;

    [Test]
    public void HardwareFramesAreSampledInTheLayoutTheyArriveIn()
    {
        var convertTime = GlobalStatistics.Get<double>("Video", "Convert Time");
        var frameFormat = GlobalStatistics.Get<string>("Video", "Frame Format");
        convertTime.Clear();

        using var textureManager = new HeadlessTextureManager();
        var renderer = new HeadlessRenderer(textureManager);

        using var stream = typeof(VideoPlaneLayoutTest).Assembly.GetManifestResourceStream(video_resource);
        Assert.That(stream, Is.Not.Null, $"missing embedded test video {video_resource}");

        using var decoder = new VideoDecoder(renderer, textureManager, stream!) { Looping = true };
        decoder.Start();

        var timeout = Stopwatch.StartNew();
        int seen = 0;
        VideoPlaneLayout? layout = null;

        while (timeout.ElapsedMilliseconds < decode_timeout_ms && seen < frames_to_settle)
        {
            var frames = decoder.GetDecodedFrames().ToList();

            if (frames.Count == 0)
            {
                Thread.Sleep(5);
                continue;
            }

            seen += frames.Count;
            layout = frames[^1].NativeTexture.Layout;

            // Hand them straight back, so the pool keeps turning over and decoding continues.
            decoder.ReturnFrames(frames);
        }

        Assert.That(layout, Is.Not.Null, $"no frame decoded within {decode_timeout_ms} ms (decoder state {decoder.State})");

        string format = frameFormat.Value;

        using (Assert.EnterMultipleScope())
        {
            // The layout must follow the format the decoder actually produced, whichever that is —
            // a machine with no hardware decoder for H.264 still runs this, on the software path.
            var expected = format == "nv12" ? VideoPlaneLayout.Nv12 : VideoPlaneLayout.Yuv420P;
            Assert.That(layout, Is.EqualTo(expected), $"frame format {format} was given a {layout} texture");

            // Both formats reaching here are directly samplable, so the conversion step should be a
            // comparison and a return. Anything approaching a real sws_scale of even this 160x120 frame
            // would sit well above this.
            Assert.That(convertTime.Value, Is.LessThan(0.02),
                $"conversion is doing work on an already-samplable {format} frame");

            // On a machine where VideoToolbox took the stream, the format is not in doubt: this is the
            // assumption the whole plan rests on, so it is asserted rather than inferred.
            if (decoder.ActiveHardwareDevice == FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX)
            {
                Assert.That(format, Is.EqualTo("nv12"));
                Assert.That(layout, Is.EqualTo(VideoPlaneLayout.Nv12));
            }
        }
    }

    /// <summary>
    /// Turning hardware acceleration off reopens the codec, and the frames come back as YUV420P
    /// instead of NV12 — a plane set the existing pool physically cannot hold. The pool has to follow,
    /// and frames have to keep arriving while it does. Before layouts existed this was merely a leak
    /// (the pool warmed a second set and never retired the first); with them, it is the one way to hand
    /// a two-plane frame to a three-plane texture.
    /// </summary>
    [Test]
    public void TogglingHardwareAccelerationRebuildsThePoolForTheNewLayout()
    {
        using var textureManager = new HeadlessTextureManager();
        var renderer = new HeadlessRenderer(textureManager);

        using var stream = typeof(VideoPlaneLayoutTest).Assembly.GetManifestResourceStream(video_resource);
        Assert.That(stream, Is.Not.Null, $"missing embedded test video {video_resource}");

        using var decoder = new VideoDecoder(renderer, textureManager, stream!) { Looping = true };
        decoder.Start();

        var before = awaitLayout(decoder);
        Assert.That(before, Is.Not.Null, "no frame decoded before the toggle");

        if (decoder.ActiveHardwareDevice == FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            Assert.Ignore("no hardware decoder for H.264 here, so there is no layout to switch away from");

        Assert.That(before, Is.EqualTo(VideoPlaneLayout.Nv12));

        decoder.HardwareAcceleration.Value = false;

        // The toggle is routed through the decoder's command queue, so it takes effect on the decode
        // thread a moment later; keep draining until frames come back in the software layout.
        var after = awaitLayout(decoder, VideoPlaneLayout.Yuv420P);

        Assert.That(after, Is.EqualTo(VideoPlaneLayout.Yuv420P),
            "frames kept arriving in the hardware layout after hardware decode was turned off");
    }

    /// <summary>
    /// Drains frames until one arrives, optionally waiting for a particular layout. Frames are handed
    /// straight back, so the pool keeps turning over, and the decoded thread is never blocked on it.
    /// </summary>
    private static VideoPlaneLayout? awaitLayout(VideoDecoder decoder, VideoPlaneLayout? expected = null)
    {
        var timeout = Stopwatch.StartNew();
        VideoPlaneLayout? seen = null;

        while (timeout.ElapsedMilliseconds < decode_timeout_ms)
        {
            var frames = decoder.GetDecodedFrames().ToList();

            if (frames.Count == 0)
            {
                Thread.Sleep(5);
                continue;
            }

            seen = frames[^1].NativeTexture.Layout;
            decoder.ReturnFrames(frames);

            if (expected == null || seen == expected)
                return seen;
        }

        return seen;
    }
}
