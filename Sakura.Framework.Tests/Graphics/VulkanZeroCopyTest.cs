// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using FFmpeg.AutoGen;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vulkan;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Frames FFmpeg holds on the Vulkan renderer's own device are drawn in place, and FFmpeg and the
/// renderer take turns on them correctly.
/// </summary>
/// <remarks>
/// <para>
/// No driver this runs on decodes video, so FFmpeg allocates its frames on the renderer's device and
/// uploads known pixels into them. Everything after the decode is what a real Vulkan decode goes through:
/// the shared device context, FFmpeg's frame images, the per-plane views, and the semaphore and layout
/// handover between FFmpeg's submissions and the renderer's.
/// </para>
/// <para>
/// FFmpeg only ever writes into the frame here, never reads it back. FFmpeg 8.1 takes upload and download
/// staging buffers from one pool per frames context, so a download after an upload copies into a buffer
/// created for the other direction, a validation error that is FFmpeg's and not the renderer's. Playback
/// never mixes the two: zero-copy transfers nothing, and the copying path only downloads.
/// </para>
/// <para>
/// Needs an FFmpeg built with Vulkan, and a device with Vulkan 1.3, timeline semaphores and
/// synchronization2; it is ignored otherwise. Any validation message fails it, which is what catches a
/// wrong layout or a semaphore value used twice.
/// </para>
/// </remarks>
[TestFixture]
public unsafe class VulkanZeroCopyTest
{
    private const int size = VulkanVideoTest.SURFACE_SIZE;

    private static readonly GlobalStatistic<long> stat_validation_messages = GlobalStatistics.Get<long>("Vulkan", "Validation Messages", StatisticKind.Cumulative);

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [Test]
    public void FramesOnTheRenderersDeviceAreSampledInPlace()
    {
        using var capture = PixelCapture.Create(PixelCapture.VULKAN, size, size);
        var renderer = (VulkanRenderer)capture.Renderer;

        // The capture only watches its own frames. FFmpeg's submissions come in between them, and a
        // layout the renderer left wrong only shows up in FFmpeg's next barrier, so the whole test is
        // watched.
        long validationBefore = stat_validation_messages.Value;

        AVBufferRef* device = renderer.CreateVideoDecodeDevice();

        if (device == null)
            Assert.Ignore("This FFmpeg has no Vulkan support, or the device can't share frames (it needs Vulkan 1.3 with timeline semaphores and synchronization2).");

        AVBufferRef* framesContext = null;
        AVFrame* hardware = null;
        AVFrame* software = null;

        var shaders = VideoShaderSet.Create(renderer);
        INativeVideoTexture? texture = null;

        try
        {
            framesContext = ffmpeg.av_hwframe_ctx_alloc(device);
            var frames = (AVHWFramesContext*)framesContext->data;
            frames->format = AVPixelFormat.AV_PIX_FMT_VULKAN;
            frames->sw_format = AVPixelFormat.AV_PIX_FMT_NV12;
            frames->width = size;
            frames->height = size;

            Assert.That(ffmpeg.av_hwframe_ctx_init(framesContext), Is.Zero, "av_hwframe_ctx_init: FFmpeg couldn't make NV12 frames on the renderer's device");

            hardware = ffmpeg.av_frame_alloc();
            Assert.That(ffmpeg.av_hwframe_get_buffer(framesContext, hardware, 0), Is.Zero, "av_hwframe_get_buffer");

            software = allocateSoftwareFrame();

            float[] matrix = VulkanVideoTest.MatrixFor(size, size);

            // FFmpeg writes the frame on the renderer's graphics queue, under its lock.
            VulkanVideoTest.FillQuadrants(software, VideoPlaneLayout.Nv12, VulkanVideoTest.Quadrants);
            Assert.That(ffmpeg.av_hwframe_transfer_data(hardware, software, 0), Is.Zero, "uploading the first frame");

            Assert.That(renderer.CanSampleHardwareFrame(hardware), Is.True, "a frame on the renderer's own device");

            texture = renderer.CreateVideoTexture(size, size, VideoPlaneLayout.Nv12, fromHardwareFrame: true);
            Assert.That(texture, Is.TypeOf<VulkanHardwareVideoTexture>());
            Assert.That(texture.PlaneBytes, Is.Zero, "the planes are the decoder's");

            capture.Begin(new Vector4(0, 0, 0, 1));
            texture.Upload(hardware);
            Assert.That(texture.Available, Is.True, "the frame was mapped");
            VulkanVideoTest.DrawVideo(renderer, shaders.For(VideoPlaneLayout.Nv12), texture, matrix, false, 1);
            VulkanVideoTest.AssertQuadrants(capture.End(), VulkanVideoTest.Quadrants, matrix, "drawn in place");

            // FFmpeg uses the frame again between two draws of it, as decoding a later frame from this one
            // would: its barrier starts from the layout the renderer said it left the image in, and moves
            // it to another, and its semaphore advances. The next draw must acquire it afresh.
            Assert.That(ffmpeg.av_hwframe_transfer_data(hardware, software, 0), Is.Zero, "rewriting the frame");

            capture.Begin(new Vector4(0, 0, 0, 1));
            VulkanVideoTest.DrawVideo(renderer, shaders.For(VideoPlaneLayout.Nv12), texture, matrix, false, 1);
            VulkanVideoTest.AssertQuadrants(capture.End(), VulkanVideoTest.Quadrants, matrix, "drawn again after FFmpeg used it");

            // FFmpeg writes new pixels into the image the texture is still showing: it has to wait for
            // the renderer's last use, and the renderer's next draw for its write.
            VulkanVideoTest.FillQuadrants(software, VideoPlaneLayout.Nv12, VulkanVideoTest.Inverted);
            Assert.That(ffmpeg.av_hwframe_transfer_data(hardware, software, 0), Is.Zero, "uploading the second frame");

            capture.Begin(new Vector4(0, 0, 0, 1));
            VulkanVideoTest.DrawVideo(renderer, shaders.For(VideoPlaneLayout.Nv12), texture, matrix, false, 1);
            VulkanVideoTest.AssertQuadrants(capture.End(), VulkanVideoTest.Inverted, matrix, "redrawn after FFmpeg wrote into it");

            // And once more, after the renderer's last draw.
            Assert.That(ffmpeg.av_hwframe_transfer_data(hardware, software, 0), Is.Zero, "rewriting the frame after the last draw");

            Assert.That(stat_validation_messages.Value - validationBefore, Is.Zero, "validation messages, FFmpeg's submissions on the shared device included; the log has them");
        }
        finally
        {
            texture?.Dispose();
            shaders.Dispose();

            // One more frame, so the texture's views and its reference to the frame are let go before
            // FFmpeg destroys the images under them.
            capture.Begin(new Vector4(0, 0, 0, 1));
            capture.End();

            ffmpeg.av_frame_free(&hardware);
            ffmpeg.av_frame_free(&software);
            ffmpeg.av_buffer_unref(&framesContext);
            ffmpeg.av_buffer_unref(&device);
        }
    }

    private static AVFrame* allocateSoftwareFrame()
    {
        var frame = ffmpeg.av_frame_alloc();
        frame->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
        frame->width = size;
        frame->height = size;

        Assert.That(ffmpeg.av_frame_get_buffer(frame, 0), Is.Zero, "av_frame_get_buffer");
        return frame;
    }
}
