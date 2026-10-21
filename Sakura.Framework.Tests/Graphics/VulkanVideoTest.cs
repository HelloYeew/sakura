// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Uniforms;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.Maths;
using Sakura.Framework.Statistic;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Video frames drawn through the Vulkan renderer come out in the colors their samples encode. The
/// frames are built by hand, a different color in each quadrant, and drawn the way a video sprite draws
/// them, so a plane in the wrong slot or channel, Cb and Cr swapped, or rows read with the wrong stride
/// each show up as a wrong color in a known place.
/// </summary>
[TestFixture]
public unsafe class VulkanVideoTest
{
    internal const int SURFACE_SIZE = 64;

    /// <summary>
    /// Limited-range BT.709 samples (Y, Cb, Cr) for the top-left, top-right, bottom-left and bottom-right
    /// Quadrants: roughly red, green, blue and mid grey.
    /// </summary>
    internal static readonly (byte Y, byte Cb, byte Cr)[] Quadrants = [(81, 90, 240), (145, 54, 34), (41, 240, 110), (126, 128, 128)];

    /// <summary>
    /// The same Quadrants with every Cb and Cr Inverted around the centre, so a texture uploaded with one
    /// and then the other changes color everywhere.
    /// </summary>
    internal static readonly (byte Y, byte Cb, byte Cr)[] Inverted = [(81, 166, 16), (145, 202, 222), (41, 16, 146), (126, 128, 128)];

    private static readonly GlobalStatistic<int> stat_staging_buffers_created = GlobalStatistics.Get<int>("Vulkan", "Staging Buffers Created", StatisticKind.Cumulative);

    internal static float[] MatrixFor(int width, int height) => VideoDecoder.ConversionMatrixFor(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_MPEG, width, height);

    [TestCase(VideoPlaneLayout.Nv12)]
    [TestCase(VideoPlaneLayout.Yuv420P)]
    public void PlanesConvertToTheColorsTheyEncode(VideoPlaneLayout layout)
    {
        using var capture = PixelCapture.Create(PixelCapture.VULKAN, SURFACE_SIZE, SURFACE_SIZE);
        using var frame = new SyntheticFrame(layout, SURFACE_SIZE, SURFACE_SIZE, rowPadding: 0);

        frame.Fill(Quadrants);

        AssertQuadrants(drawFrames(capture, layout, frame), Quadrants, MatrixFor(SURFACE_SIZE, SURFACE_SIZE), $"{layout}, tightly packed rows");
    }

    /// <summary>
    /// Rows the way a decoder pads them, and an odd frame size, whose chroma planes round up. The padding
    /// is a whole number of texels for every plane in one case, which the upload copies past in one go,
    /// and an odd number of bytes in the other, which splits an interleaved chroma texel and has to be
    /// packed row by row.
    /// </summary>
    [TestCase(VideoPlaneLayout.Nv12, 32)]
    [TestCase(VideoPlaneLayout.Nv12, 7)]
    [TestCase(VideoPlaneLayout.Yuv420P, 7)]
    public void PaddedRowsAreReadWithTheirStride(VideoPlaneLayout layout, int rowPadding)
    {
        const int width = 61;
        const int height = 45;

        using var capture = PixelCapture.Create(PixelCapture.VULKAN, SURFACE_SIZE, SURFACE_SIZE);
        using var frame = new SyntheticFrame(layout, width, height, rowPadding);

        frame.Fill(Quadrants);

        AssertQuadrants(drawFrames(capture, layout, frame), Quadrants, MatrixFor(width, height), $"{layout}, {width}x{height}, {rowPadding} bytes of padding per row");
    }

    /// <summary>
    /// A frame too large to share a frame's stream buffer goes through a staging buffer of its own, and
    /// the same texture is uploaded into again on every frame, as a video's pooled textures are. Each frame
    /// must show its own colors, and once the first frame has made one, the staging buffers go round
    /// rather than being made again.
    /// </summary>
    [Test]
    public void LargeFramesReuseTheirStagingBuffers()
    {
        const int width = 1280;
        const int height = 720;

        using var capture = PixelCapture.Create(PixelCapture.VULKAN, SURFACE_SIZE, SURFACE_SIZE);
        using var frame = new SyntheticFrame(VideoPlaneLayout.Nv12, width, height, rowPadding: 64);

        var renderer = capture.Renderer;
        var shaders = VideoShaderSet.Create(renderer);
        var texture = renderer.CreateVideoTexture(width, height, VideoPlaneLayout.Nv12);
        float[] matrix = MatrixFor(width, height);
        int createdAfterFirstFrame = 0;

        try
        {
            for (int i = 0; i < 6; i++)
            {
                var colors = i % 2 == 0 ? Quadrants : Inverted;
                frame.Fill(colors);

                capture.Begin(new Vector4(0, 0, 0, 1));
                texture.Upload(frame.Frame);
                DrawVideo(renderer, shaders.For(VideoPlaneLayout.Nv12), texture, matrix, false, 1);
                var screen = capture.End();

                AssertQuadrants(screen, colors, matrix, $"frame {i}");

                if (i == 0)
                    createdAfterFirstFrame = stat_staging_buffers_created.Value;
            }

            Assert.That(stat_staging_buffers_created.Value, Is.EqualTo(createdAfterFirstFrame), "staging buffers created after the first frame");
        }
        finally
        {
            texture.Dispose();
            shaders.Dispose();
        }
    }

    /// <summary>
    /// A tiled fill draws the frame with texture coordinates past 1, which must wrap around to the frame's
    /// other side; any other fill clamps them to its edge.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void TiledFillRepeatsTheFrameAndOtherFillsClamp(bool tiling)
    {
        using var capture = PixelCapture.Create(PixelCapture.VULKAN, SURFACE_SIZE, SURFACE_SIZE);
        using var frame = new SyntheticFrame(VideoPlaneLayout.Nv12, SURFACE_SIZE, SURFACE_SIZE, rowPadding: 0);

        // Left half the first color, right half the second.
        var halves = new[] { Quadrants[0], Quadrants[1], Quadrants[0], Quadrants[1] };
        frame.Fill(halves);

        float[] matrix = MatrixFor(SURFACE_SIZE, SURFACE_SIZE);

        // Twice across: the right half of the screen samples u from 1 to 2.
        var screen = drawFrames(capture, VideoPlaneLayout.Nv12, frame, tiling, uMax: 2);

        // u = 1.25: the left half again when wrapped, the right edge when clamped.
        var sample = screen[SURFACE_SIZE * 5 / 8, SURFACE_SIZE / 2];
        var expected = expectedColor(matrix, tiling ? halves[0] : halves[1]);

        assertColor(sample, expected, tiling ? "tiled: wrapped around to the left half" : "clamped: the frame's right edge");
    }

    private static PixelSurface drawFrames(PixelCapture capture, VideoPlaneLayout layout, SyntheticFrame frame, bool tiling = false, float uMax = 1)
    {
        var renderer = capture.Renderer;
        var shaders = VideoShaderSet.Create(renderer);
        var texture = renderer.CreateVideoTexture(frame.Frame->width, frame.Frame->height, layout);

        try
        {
            capture.Begin(new Vector4(0, 0, 0, 1));
            texture.Upload(frame.Frame);
            DrawVideo(renderer, shaders.For(layout), texture, MatrixFor(frame.Frame->width, frame.Frame->height), tiling, uMax);
            return capture.End();
        }
        finally
        {
            texture.Dispose();
            shaders.Dispose();
        }
    }

    /// <summary>
    /// What <c>VideoDrawNode</c> does, over the whole surface.
    /// </summary>
    internal static void DrawVideo(IRenderer renderer, IShader shader, INativeVideoTexture texture, float[] matrix, bool tiling, float uMax)
    {
        renderer.FlushBatch();

        shader.Use();
        shader.SetUniformBlock("ProjectionBlock", new ProjectionBlock { Projection = renderer.ProjectionMatrix });
        texture.BindPlanes(tiling);
        shader.SetUniformBlock("VideoBlock", VideoBlock.FromAffine(matrix));

        Span<SakuraVertex> quad =
        [
            vertex(0, 0, 0, 0),
            vertex(SURFACE_SIZE, 0, uMax, 0),
            vertex(SURFACE_SIZE, SURFACE_SIZE, uMax, 1),
            vertex(0, SURFACE_SIZE, 0, 1),
        ];

        renderer.ApplyCurrentClip(quad);
        ((ICustomShaderRenderer)renderer).DrawVerticesRaw(quad);

        renderer.RestoreMainShader();
    }

    private static SakuraVertex vertex(float x, float y, float u, float v) => new SakuraVertex
    {
        Position = new Vector2(x, y),
        Color = new Vector4(1, 1, 1, 1),
        TexCoords = new Vector2(u, v),
        ClipData = new Vector4(0, 0, -1, -1),
    };

    internal static void AssertQuadrants(PixelSurface screen, (byte Y, byte Cb, byte Cr)[] colors, float[] matrix, string because)
    {
        Assert.Multiple(() =>
        {
            for (int q = 0; q < 4; q++)
            {
                // The middle of the quadrant, far from any edge the filtering blends across.
                int x = SURFACE_SIZE / 4 + q % 2 * SURFACE_SIZE / 2;
                int y = SURFACE_SIZE / 4 + q / 2 * SURFACE_SIZE / 2;

                assertColor(screen[x, y], expectedColor(matrix, colors[q]), $"{because}, quadrant {q}");
            }
        });
    }

    private static void assertColor(Vector4 actual, Vector4 expected, string because)
    {
        // An R8 sample, converted and written to an 8-bit sRGB target, then decoded again.
        const float tolerance = 0.02f;

        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(tolerance), $"{because}: red");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(tolerance), $"{because}: green");
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(tolerance), $"{because}: blue");
            Assert.That(actual.W, Is.EqualTo(1f).Within(tolerance), $"{because}: alpha");
        });
    }

    /// <summary>
    /// The video shaders' arithmetic: the affine conversion, clamped, then linearised with gamma 2.2, which
    /// is the space the surface is read back in.
    /// </summary>
    private static Vector4 expectedColor(float[] m, (byte Y, byte Cb, byte Cr) sample)
    {
        float y = sample.Y / 255f;
        float cb = sample.Cb / 255f;
        float cr = sample.Cr / 255f;

        float channel(int row) => MathF.Pow(Math.Clamp(m[row] * y + m[4 + row] * cb + m[8 + row] * cr + m[12 + row], 0f, 1f), 2.2f);

        return new Vector4(channel(0), channel(1), channel(2), 1);
    }

    /// <summary>
    /// Writes each quadrant's samples into <paramref name="frame"/>'s planes, chroma at half resolution.
    /// Padding is filled with a value no quadrant uses, so reading it shows up.
    /// </summary>
    internal static void FillQuadrants(AVFrame* frame, VideoPlaneLayout layout, (byte Y, byte Cb, byte Cr)[] colors)
    {
        int width = frame->width;
        int height = frame->height;
        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;

        int quadrant(int x, int y) => (x >= width / 2 ? 1 : 0) + (y >= height / 2 ? 2 : 0);

        for (uint plane = 0; plane < (layout == VideoPlaneLayout.Nv12 ? 2u : 3u); plane++)
            new Span<byte>(frame->data[plane], frame->linesize[plane] * (plane == 0 ? height : chromaHeight)).Fill(255);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                frame->data[0][y * frame->linesize[0] + x] = colors[quadrant(x, y)].Y;
        }

        for (int y = 0; y < chromaHeight; y++)
        {
            for (int x = 0; x < chromaWidth; x++)
            {
                var color = colors[quadrant(x * 2, y * 2)];

                if (layout == VideoPlaneLayout.Nv12)
                {
                    frame->data[1][y * frame->linesize[1] + x * 2] = color.Cb;
                    frame->data[1][y * frame->linesize[1] + x * 2 + 1] = color.Cr;
                }
                else
                {
                    frame->data[1][y * frame->linesize[1] + x] = color.Cb;
                    frame->data[2][y * frame->linesize[2] + x] = color.Cr;
                }
            }
        }
    }

    /// <summary>
    /// A decoded frame built by hand in native memory, each plane's rows padded by a given number of bytes.
    /// </summary>
    private sealed class SyntheticFrame : IDisposable
    {
        public readonly AVFrame* Frame;

        private readonly VideoPlaneLayout layout;

        public SyntheticFrame(VideoPlaneLayout layout, int width, int height, int rowPadding)
        {
            this.layout = layout;
            int chromaWidth = (width + 1) / 2;
            int chromaHeight = (height + 1) / 2;

            Frame = (AVFrame*)NativeMemory.AllocZeroed((nuint)sizeof(AVFrame));
            Frame->width = width;
            Frame->height = height;

            allocatePlane(0, width + rowPadding, height);

            if (layout == VideoPlaneLayout.Nv12)
                allocatePlane(1, chromaWidth * 2 + rowPadding, chromaHeight);
            else
            {
                allocatePlane(1, chromaWidth + rowPadding, chromaHeight);
                allocatePlane(2, chromaWidth + rowPadding, chromaHeight);
            }
        }

        public void Fill((byte Y, byte Cb, byte Cr)[] colors) => FillQuadrants(Frame, layout, colors);

        private void allocatePlane(uint plane, int stride, int rows)
        {
            Frame->data[plane] = (byte*)NativeMemory.Alloc((nuint)(stride * rows));
            Frame->linesize[plane] = stride;
        }

        public void Dispose()
        {
            for (uint plane = 0; plane < 3; plane++)
                NativeMemory.Free(Frame->data[plane]);

            NativeMemory.Free(Frame);
        }
    }
}
