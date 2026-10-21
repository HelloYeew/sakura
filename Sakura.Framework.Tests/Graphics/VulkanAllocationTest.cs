// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// The Vulkan renderer must not allocate on the managed heap per frame: on a phone, collections are the
/// first thing a player feels. The scene draws through every path the renderer has (batched quads,
/// masking with borders, edge effects, and a blurred, grayscaled buffered container: offscreen passes,
/// custom shaders, raw draws) frame after frame.
/// </summary>
[TestFixture]
public class VulkanAllocationTest
{
    private const int size = 256;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [Test]
    public void SteadyStateFramesAllocateNothing()
    {
        using var capture = PixelCapture.Create(PixelCapture.VULKAN, size, size);
        using var host = new HeadlessAppHost(nameof(SteadyStateFramesAllocateNothing));

        var root = buildScene(host);
        var node = root.GenerateDrawNodeSubtree(0);
        var clock = new FramedClock(new ManualClock());
        var background = new Vector4(0, 0, 0, 1);

        void frame()
        {
            capture.Begin(background);
            capture.Renderer.SetRoot(node);
            capture.Renderer.Draw(clock);
        }

        // Pipelines, descriptor pools and stream buffers are created or grown in the first frames.
        for (int i = 0; i < 30; i++)
            frame();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 120; i++)
            frame();

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero, $"{allocated} bytes allocated over 120 frames");

        // Still a correct frame afterwards, read back (allocating) only now, so the validation check runs.
        capture.Begin(background);
        capture.Renderer.SetRoot(node);
        capture.Renderer.Draw(clock);
        capture.End();
    }

    private static Container buildScene(AppHost host)
    {
        var root = new CachingRoot { Size = new Vector2(size, size) };

        // Children resolve the host as they load, so it is cached before any are added.
        root.Load();
        root.CompleteLoad();
        root.CacheHost(host);

        var masked = new Container
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(100, 100),
            Masking = true,
            CornerRadius = 12,
            BorderThickness = 3,
            BorderColor = Color.White,
            EdgeEffect = new EdgeEffectParameters { Type = EdgeEffectType.Shadow, Color = Color.Black, Radius = 8 },
        };

        masked.Add(new Box { RelativeSizeAxes = Axes.Both, Color = Color.Red });
        masked.Add(new Box { Position = new Vector2(40, 40), Size = new Vector2(100, 100), Rotation = 20, Color = Color.Blue });
        root.Add(masked);

        var glowing = new Container
        {
            Position = new Vector2(10, 140),
            Size = new Vector2(60, 60),
            Masking = true,
            CornerRadius = 30,
            EdgeEffect = new EdgeEffectParameters { Type = EdgeEffectType.Glow, Color = Color.Gold, Radius = 12 },
        };

        glowing.Add(new Box { RelativeSizeAxes = Axes.Both, Color = Color.Orange });
        root.Add(glowing);

        var buffered = new BufferedContainer
        {
            Position = new Vector2(130, 10),
            Size = new Vector2(110, 110),
            BlurSigma = new Vector2(3),
            GrayscaleStrength = 0.5f,
        };

        buffered.Add(new Box { RelativeSizeAxes = Axes.Both, Color = Color.DarkSlateGray });
        buffered.Add(new Box { Position = new Vector2(20, 20), Size = new Vector2(50, 50), Color = Color.Lime });
        root.Add(buffered);

        // Plenty of quads in one batch.
        for (int i = 0; i < 200; i++)
        {
            root.Add(new Box
            {
                Position = new Vector2(100 + i % 20 * 7, 140 + i / 20 * 11),
                Size = new Vector2(6, 10),
                Color = Color.FromArgb(200, (byte)(i * 13), (byte)(255 - i), 128),
            });
        }

        root.UpdateSubTree();

        return root;
    }

    private partial class CachingRoot : Container
    {
        public void CacheHost(AppHost value) => Cache(value);
    }
}
