// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Video;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Verifies that VideoToolbox zero-copy frames contain an 8-bit bi-planar 4:2:0
/// <c>CVPixelBuffer</c> in <c>data[3]</c>, with full-resolution luma and half-resolution chroma.
/// </summary>
/// <remarks>
/// Runs headlessly using CoreVideo metadata. A frame reaching the probe also confirms that
/// <see cref="IRenderer.CanSampleHardwareFrame"/> accepted it without readback.
/// </remarks>
[TestFixture]
public class VideoZeroCopyPixelBufferTest
{
    private const string video_resource = "Sakura.Framework.Tests.Resources.Videos.test-h264.mp4";
    private const int decode_timeout_ms = 10000;

    /// <summary>
    /// How long to let frames drain for in one window. Long enough for the pool to be retired and
    /// re-warmed for a new shape and to refill, short enough that three of them do not stall the suite.
    /// </summary>
    private const int settle_window_ms = 1000;

    /// <summary>
    /// <c>kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange</c> ('420v') and its full-range sibling
    /// ('420f') — the two formats <c>sakura_metal_create_plane_texture_from_pixel_buffer</c> accepts.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    private const uint pixel_format_420v = 0x34323076;
    // ReSharper disable once InconsistentNaming
    private const uint pixel_format_420f = 0x34323066;

    [Test]
    public void VideoToolboxFramesCarryASamplableBiPlanarPixelBuffer()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("VideoToolbox and CoreVideo are macOS-only.");

        using var textureManager = new HeadlessTextureManager();
        var renderer = new ZeroCopyProbeRenderer(textureManager);

        using var stream = typeof(VideoZeroCopyPixelBufferTest).Assembly.GetManifestResourceStream(video_resource);
        Assert.That(stream, Is.Not.Null, $"missing embedded test video {video_resource}");

        // Asked for explicitly: the setting ships off until the Metal path has been verified on a GPU,
        // and a test of the path must not be at the mercy of that default.
        using var decoder = new VideoDecoder(renderer, textureManager, stream!) { Looping = true };
        decoder.ZeroCopy.Value = true;
        decoder.Start();

        var timeout = Stopwatch.StartNew();

        while (timeout.ElapsedMilliseconds < decode_timeout_ms && renderer.Probe.Frames == 0)
        {
            var frames = decoder.GetDecodedFrames().ToList();

            if (frames.Count == 0)
            {
                Thread.Sleep(5);
                continue;
            }

            // HeadlessRenderer.ScheduleToDrawThread runs inline, so the pool warmed on this thread and
            // the upload is a direct call — which is what lets the probe see the frame at all.
            foreach (var f in frames)
                f.NativeTexture.FlushIfPending();

            decoder.ReturnFrames(frames);
        }

        if (decoder.ActiveHardwareDevice != AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX)
            Assert.Ignore("no VideoToolbox decoder for H.264 here, so there is no hardware frame to inspect");

        var probe = renderer.Probe;

        Assert.That(probe.Frames, Is.GreaterThan(0),
            $"no frame reached the zero-copy texture within {decode_timeout_ms} ms (decoder state {decoder.State})");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(probe.PixelBuffer, Is.Not.EqualTo(IntPtr.Zero),
                "a VideoToolbox frame reached the texture with nothing in data[3]");

            Assert.That(probe.PixelFormat, Is.AnyOf(pixel_format_420v, pixel_format_420f),
                $"pixel buffer format 0x{probe.PixelFormat:X8} is not 8-bit bi-planar 4:2:0, so the R8/RG8 plane views would mis-sample it");

            Assert.That(probe.PlaneCount, Is.EqualTo(2), "NV12 must present exactly two planes");

            // The luma plane is the frame; the chroma plane is half of it in each axis, rounded up.
            // The native side reads these from CoreVideo rather than re-deriving them, and this is what
            // says that was the right call.
            Assert.That(probe.LumaWidth, Is.EqualTo(probe.FrameWidth));
            Assert.That(probe.LumaHeight, Is.EqualTo(probe.FrameHeight));
            Assert.That(probe.ChromaWidth, Is.EqualTo((probe.FrameWidth + 1) / 2));
            Assert.That(probe.ChromaHeight, Is.EqualTo((probe.FrameHeight + 1) / 2));

            // The layout has to be the one video_nv12.frag samples, or the wrong shader variant is
            // picked for the texture.
            Assert.That(probe.Layout, Is.EqualTo(VideoPlaneLayout.Nv12));
        }
    }

    /// <summary>
    /// Turning zero-copy off mid-playback has to move frames back onto the readback path without a gap.
    /// The two kinds of texture are not interchangeable — one borrows a pixel buffer, the other owns
    /// planes and copies into them — so the pool has to be rebuilt for the new shape, which is the
    /// whole reason the path is part of <c>VideoTextureShape</c> rather than a flag beside it.
    /// </summary>
    [Test]
    public void TogglingZeroCopyRebuildsThePoolForTheOtherPath()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("VideoToolbox is macOS-only.");

        using var textureManager = new HeadlessTextureManager();
        var renderer = new ZeroCopyProbeRenderer(textureManager);

        using var stream = typeof(VideoZeroCopyPixelBufferTest).Assembly.GetManifestResourceStream(video_resource);
        Assert.That(stream, Is.Not.Null, $"missing embedded test video {video_resource}");

        using var decoder = new VideoDecoder(renderer, textureManager, stream!) { Looping = true };
        decoder.ZeroCopy.Value = true;
        decoder.Start();

        drainUntil(decoder, () => renderer.Probe.Frames > 0);

        if (decoder.ActiveHardwareDevice != AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX)
            Assert.Ignore("no VideoToolbox decoder for H.264 here, so there is no zero-copy path to leave");

        // Counted on the probe, not on frames drained: frames arrive either way, and this assertion is
        // the only thing standing between "the toggle worked" and "the path was never taken at all".
        Assert.That(renderer.Probe.Frames, Is.GreaterThan(0), "no frame arrived on the zero-copy path");

        decoder.ZeroCopy.Value = false;

        // Frames decoded before the toggle can still be sitting in the queue, and they were routed on
        // the old decision — so drain those out first rather than counting them against the new one.
        // The decoder decides per frame and needs no codec reopen here, so one window is plenty.
        drainUntil(decoder, () => false, settle_window_ms);

        int seenAfterToggle = renderer.Probe.Frames;
        int drainedAfter = drainUntil(decoder, () => false, settle_window_ms);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(drainedAfter, Is.GreaterThan(0),
                "frames stopped arriving after zero-copy was turned off — the pool did not follow the new shape");

            Assert.That(renderer.Probe.Frames, Is.EqualTo(seenAfterToggle),
                "frames were still routed to the zero-copy texture after the toggle");
        }
    }

    /// <summary>
    /// Drains and flushes frames until <paramref name="stop"/> is met or the timeout expires, returning
    /// how many arrived. A predicate of <c>() =&gt; false</c> simply drains for the whole window.
    /// </summary>
    private static int drainUntil(VideoDecoder decoder, Func<bool> stop, int windowMs = settle_window_ms)
    {
        var timeout = Stopwatch.StartNew();
        int drained = 0;

        while (timeout.ElapsedMilliseconds < windowMs)
        {
            var frames = decoder.GetDecodedFrames().ToList();

            if (frames.Count == 0)
            {
                Thread.Sleep(5);
                continue;
            }

            foreach (var f in frames)
                f.NativeTexture.FlushIfPending();

            drained += frames.Count;
            decoder.ReturnFrames(frames);

            if (stop())
                break;
        }

        return drained;
    }

    /// <summary>
    /// What the probe saw. Written on the decoded thread (headless uploads run inline there) and read
    /// after it has stopped, so the counter is the only field needing interlocked access.
    /// </summary>
    private sealed class ProbeResult
    {
        public int Frames;
        public IntPtr PixelBuffer;
        public uint PixelFormat;
        public int PlaneCount;
        public int FrameWidth;
        public int FrameHeight;
        public int LumaWidth;
        public int LumaHeight;
        public int ChromaWidth;
        public int ChromaHeight;
        public VideoPlaneLayout Layout;
    }

    /// <summary>
    /// A headless renderer that claims the Metal zero-copy path for VideoToolbox frames, so the decoder
    /// takes the branch under test and hands the frame over untouched.
    /// </summary>
    private sealed class ZeroCopyProbeRenderer : HeadlessRenderer
    {
        public ProbeResult Probe { get; } = new ProbeResult();

        public ZeroCopyProbeRenderer(HeadlessTextureManager textureManager)
            : base(textureManager)
        {
        }

        public override unsafe bool CanSampleHardwareFrame(AVFrame* frame) =>
            frame != null && (AVPixelFormat)frame->format == AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX;

        public override INativeVideoTexture CreateVideoTexture(int width, int height, VideoPlaneLayout layout, bool fromHardwareFrame = false) =>
            fromHardwareFrame
                ? new ProbeVideoTexture(width, height, layout, Probe)
                : base.CreateVideoTexture(width, height, layout, fromHardwareFrame);
    }

    /// <summary>
    /// Stands where <c>MetalHardwareVideoTexture</c> would, and asks CoreVideo the same questions the
    /// native side asks before it maps a plane.
    /// </summary>
    private sealed class ProbeVideoTexture : INativeVideoTexture
    {
        private readonly ProbeResult probe;

        public int Width { get; }
        public int Height { get; }
        public VideoPlaneLayout Layout { get; }
        public bool Available { get; private set; }
        public TextureBindCounter Binds { get; } = new TextureBindCounter();

        /// <summary>Zero, like the real zero-copy texture: it would borrow, not allocate.</summary>
        public long PlaneBytes => 0;

        public ProbeVideoTexture(int width, int height, VideoPlaneLayout layout, ProbeResult probe)
        {
            Width = width;
            Height = height;
            Layout = layout;
            this.probe = probe;
        }

        public void BindPlanes(bool tiling) { }

        public unsafe void Upload(AVFrame* frame)
        {
            IntPtr pixelBuffer = (IntPtr)frame->data[3];

            probe.PixelBuffer = pixelBuffer;
            probe.FrameWidth = frame->width;
            probe.FrameHeight = frame->height;
            probe.Layout = Layout;

            if (pixelBuffer != IntPtr.Zero)
            {
                probe.PixelFormat = CVPixelBufferGetPixelFormatType(pixelBuffer);
                probe.PlaneCount = (int)CVPixelBufferGetPlaneCount(pixelBuffer);

                if (probe.PlaneCount > 0)
                {
                    probe.LumaWidth = (int)CVPixelBufferGetWidthOfPlane(pixelBuffer, 0);
                    probe.LumaHeight = (int)CVPixelBufferGetHeightOfPlane(pixelBuffer, 0);
                }

                if (probe.PlaneCount > 1)
                {
                    probe.ChromaWidth = (int)CVPixelBufferGetWidthOfPlane(pixelBuffer, 1);
                    probe.ChromaHeight = (int)CVPixelBufferGetHeightOfPlane(pixelBuffer, 1);
                }
            }

            Interlocked.Increment(ref probe.Frames);
            MarkAvailable();
        }

        public void MarkAvailable() => Available = true;
        public void Dispose() { }

        private const string core_video = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";

        [DllImport(core_video)]
        private static extern uint CVPixelBufferGetPixelFormatType(IntPtr pixelBuffer);

        [DllImport(core_video)]
        private static extern nuint CVPixelBufferGetPlaneCount(IntPtr pixelBuffer);

        [DllImport(core_video)]
        private static extern nuint CVPixelBufferGetWidthOfPlane(IntPtr pixelBuffer, nuint planeIndex);

        [DllImport(core_video)]
        private static extern nuint CVPixelBufferGetHeightOfPlane(IntPtr pixelBuffer, nuint planeIndex);
    }
}
