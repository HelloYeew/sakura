// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vulkan;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// A renderer that pixel tests draw into and read back, so the same assertions run against the CPU
/// reference rasterizer and against a real GPU backend.
/// </summary>
/// <remarks>
/// A test calls <see cref="Begin"/>, draws through <see cref="Renderer"/> (directly, or by setting a root
/// and calling <see cref="IRenderer.Draw"/>), and reads the result with <see cref="End"/>.
/// </remarks>
public abstract class PixelCapture : IDisposable
{
    public const string HEADLESS = "Headless";
    public const string VULKAN = "Vulkan";

    public abstract IRenderer Renderer { get; }

    /// <summary>
    /// How far a channel may stray from the exact value on top of the test's own tolerance. The reference
    /// rasterizer computes in floats; a GPU stores 8-bit sRGB, which rounds every intermediate buffer.
    /// </summary>
    public abstract float Quantization { get; }

    /// <summary>
    /// Starts a frame on a screen cleared to <paramref name="background"/> (linear color).
    /// </summary>
    public abstract void Begin(Vector4 background);

    /// <summary>
    /// Finishes the frame and returns the screen, in linear color with row 0 at the top.
    /// </summary>
    public abstract PixelSurface End();

    public virtual void Dispose()
    {
    }

    public static PixelCapture Create(string kind, int width, int height) => kind switch
    {
        HEADLESS => new HeadlessPixelCapture(width, height),
        VULKAN => new VulkanPixelCapture(width, height),
        _ => throw new ArgumentException($"Unknown renderer '{kind}'.", nameof(kind)),
    };

    private sealed class HeadlessPixelCapture : PixelCapture
    {
        private readonly HeadlessRenderer renderer = new HeadlessRenderer(new HeadlessTextureManager());

        public HeadlessPixelCapture(int width, int height)
        {
            renderer.EnablePixelCapture(width, height);
        }

        public override IRenderer Renderer => renderer;

        public override float Quantization => 0;

        public override void Begin(Vector4 background) => renderer.Screen.Clear(background);

        public override PixelSurface End() => renderer.Screen;
    }

    /// <summary>
    /// Renders offscreen through Vulkan, with no window, so it runs on any machine with a Vulkan driver;
    /// CI uses Mesa's software one. Without a driver the test is ignored. Any validation-layer message
    /// during the test fails it.
    /// </summary>
    private sealed class VulkanPixelCapture : PixelCapture
    {
        private static readonly GlobalStatistic<long> stat_validation_messages = GlobalStatistics.Get<long>("Vulkan", "Validation Messages", StatisticKind.Cumulative);

        private readonly VulkanRenderer renderer = new VulkanRenderer();
        private long validationMessagesBefore;

        public VulkanPixelCapture(int width, int height)
        {
            if (!VulkanRenderer.IsSupportedOffscreen())
            {
                Assert.Ignore("No Vulkan driver here. On macOS, set SDL_VULKAN_LIBRARY and VK_DRIVER_FILES as the README's "
                              + "\"Test Vulkan on macOS\" section describes; CI installs Mesa's lavapipe on Linux.");
            }

            renderer.ShaderStorage = new EmbeddedResourceStorage(typeof(App).Assembly, "Sakura.Framework.Resources").GetStorageForDirectory("Shaders");
            renderer.InitializeOffscreen(width, height);
        }

        public override IRenderer Renderer => renderer;

        public override float Quantization => 0.01f;

        public override void Begin(Vector4 background)
        {
            validationMessagesBefore = stat_validation_messages.Value;
            renderer.OffscreenClearColor = background;
            renderer.StartFrame();
        }

        public override PixelSurface End()
        {
            if (renderer.IsFrameOpen)
                renderer.Draw(new FramedClock(new ManualClock()));

            var screen = renderer.ReadOffscreen();

            long messages = stat_validation_messages.Value - validationMessagesBefore;

            if (messages > 0)
                Assert.Fail($"The Vulkan validation layer reported {messages} message(s) while rendering; the log has them.");

            return screen;
        }

        public override void Dispose()
        {
            long before = stat_validation_messages.Value;
            renderer.Dispose();

            // Destroying the device with anything still alive on it is a validation error too.
            long messages = stat_validation_messages.Value - before;

            if (messages > 0)
                Assert.Fail($"The Vulkan validation layer reported {messages} message(s) while the renderer was disposed; the log has them.");
        }
    }
}
