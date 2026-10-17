// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Video;

namespace Sakura.Framework.Tests.Graphics;

[TestFixture]
public class VideoDecoderSeekTest
{
    private const string video_resource = "Sakura.Framework.Tests.Resources.Videos.late-keyframe.avi";

    private const int timeout_ms = 10000;

    /// <summary>
    /// How far after the target the first frame may land: the late-keyframe video's first keyframe
    /// is 160 ms in, so that is where a seek to the start resumes.
    /// </summary>
    private const double tolerance_ms = 500;

    /// <summary>
    /// A seek back to the start, made while the decoder is backed off on a full texture pool (as a
    /// paused sprite leaves it), must resume at the start.
    /// </summary>
    /// <remarks>
    /// late-keyframe.avi opens on four frames that aren't keyframes, as some beatmap videos do, so its
    /// index has no keyframe at or before 0 and a backward seek there fails. The decoder used to carry
    /// on from where it was, under the new generation, and the sprite then took that position for 0.
    /// </remarks>
    [Test]
    public void BackwardSeekToStartLandsOnTarget()
    {
        using var textureManager = new HeadlessTextureManager();
        var renderer = new HeadlessRenderer(textureManager);

        using var stream = typeof(VideoDecoderSeekTest).Assembly.GetManifestResourceStream(video_resource);
        Assert.That(stream, Is.Not.Null, $"missing embedded test video {video_resource}");

        using var decoder = new VideoDecoder(renderer, textureManager, stream!);
        decoder.Start();

        // Several rounds, since whether the decode thread is holding a packet the codec turned away
        // when the seek arrives depends on timing.
        for (int round = 0; round < 8; round++)
        {
            double from = 2500 + round % 5 * 1000;

            decoder.Seek(from);
            double reached = playUntil(decoder, from + 500);
            Assert.That(reached, Is.GreaterThanOrEqualTo(from), $"round {round}: never played forward to {from} ms");

            // Stop taking frames, as a paused sprite does, so the decode thread fills the pool and backs off.
            Thread.Sleep(200);

            decoder.Seek(0);
            double first = firstFrameOfCurrentGeneration(decoder);

            Assert.That(first, Is.LessThan(tolerance_ms), $"round {round}: seeking back to 0 from {reached:F0} ms resumed at {first:F0} ms");
        }
    }

    /// <summary>Consumes frames of the current generation until one at or past <paramref name="time"/>.</summary>
    private static double playUntil(VideoDecoder decoder, double time)
    {
        var timeout = Stopwatch.StartNew();
        double last = double.NaN;

        while (timeout.ElapsedMilliseconds < timeout_ms)
        {
            int generation = decoder.SeekGeneration;
            var frames = decoder.GetDecodedFrames().ToList();

            foreach (var f in frames.Where(f => f.Generation == generation))
                last = f.Time;

            decoder.ReturnFrames(frames);

            if (last >= time)
                return last;

            if (frames.Count == 0)
                Thread.Sleep(1);
        }

        return last;
    }

    private static double firstFrameOfCurrentGeneration(VideoDecoder decoder)
    {
        var timeout = Stopwatch.StartNew();

        while (timeout.ElapsedMilliseconds < timeout_ms)
        {
            int generation = decoder.SeekGeneration;
            var frames = decoder.GetDecodedFrames().ToList();
            var first = frames.FirstOrDefault(f => f.Generation == generation);

            decoder.ReturnFrames(frames);

            if (first != null)
                return first.Time;

            Thread.Sleep(1);
        }

        Assert.Fail($"no frame after the seek within {timeout_ms} ms (decoder state {decoder.State})");
        return double.NaN;
    }
}
