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

[TestFixture]
public class VideoPipelineStatisticsTest
{
    private const string video_resource = "Sakura.Framework.Tests.Resources.Videos.test.avi";

    /// <summary>
    /// How long to give the decode thread to produce its first frame before giving up.
    /// </summary>
    private const int decode_timeout_ms = 10000;

    [Test]
    public void DecodeStagesAreInstrumented()
    {
        var decodeTime = GlobalStatistics.Get<double>("Video", "Decode Time");
        var convertTime = GlobalStatistics.Get<double>("Video", "Convert Time");
        var uploadTime = GlobalStatistics.Get<double>("Video", "Upload Time");
        var frameFormat = GlobalStatistics.Get<string>("Video", "Frame Format");
        var decoderFormat = GlobalStatistics.Get<string>("Video", "Decoder Format");
        var frameBytes = GlobalStatistics.Get<long>("Video", "Frame Bytes");

        decodeTime.Clear();
        convertTime.Clear();
        uploadTime.Clear();
        frameBytes.Clear();

        using var textureManager = new HeadlessTextureManager();
        var renderer = new HeadlessRenderer(textureManager);

        using var stream = typeof(VideoPipelineStatisticsTest).Assembly.GetManifestResourceStream(video_resource);
        Assert.That(stream, Is.Not.Null, $"missing embedded test video {video_resource}");

        using var decoder = new VideoDecoder(renderer, textureManager, stream!);
        decoder.Start();

        DecodedFrame? frame = null;
        bool gotFrame = false;

        var timeout = Stopwatch.StartNew();

        while (timeout.ElapsedMilliseconds < decode_timeout_ms && !gotFrame)
        {
            var frames = decoder.GetDecodedFrames().ToList();

            if (frames.Count > 0)
            {
                frame = frames[0];
                gotFrame = true;
                decoder.ReturnFrames(frames.Skip(1));
            }
            else
                Thread.Sleep(5);
        }

        Assert.That(gotFrame, Is.True, $"no frame decoded within {decode_timeout_ms} ms (decoder state {decoder.State})");

        // HeadlessRenderer runs draw-thread work inline, so this is the same call the real draw
        // thread makes — and the only thing that feeds the upload stage.
        frame?.NativeTexture.FlushIfPending();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decodeTime.Value, Is.GreaterThan(0), "decode stage recorded nothing");
            Assert.That(uploadTime.Value, Is.GreaterThan(0), "upload stage recorded nothing");

            Assert.That(decoderFormat.Value, Is.Not.Null.And.Not.Empty);
            Assert.That(frameFormat.Value, Is.Not.Null.And.Not.Empty);

            // The test video is small, but a frame of any size is still several kilobytes; a zero
            // here means the size lookup failed rather than that the frame was cheap.
            Assert.That(frameBytes.Value, Is.GreaterThan(0));
            Assert.That(frameBytes.Value, Is.EqualTo(decoder.Width * decoder.Height * 3L / 2), "expected 12 bits per pixel for a 4:2:0 frame");
        }

        if (frame != null)
            decoder.ReturnFrames([frame]);
    }
}
