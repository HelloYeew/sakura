// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vulkan;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
using Path = System.IO.Path;
using SDL;
using static SDL.SDL3;

namespace Sakura.Framework.Tests.Platform;

/// <summary>
/// The Vulkan renderer through a real window: the window system's surface, a swapchain, presentation, two
/// resizes and two present mode changes (which also change the frames in flight), all under the validation
/// layer. A resize has to rebuild the swapchain; a present mode change either rebuilds it or, where the
/// driver can switch modes per present, switches without one.
/// </summary>
/// <remarks>
/// Linux only. The window has to live on the thread the test runs on, which macOS forbids for anything but
/// the process's main thread, and Windows CI has no Vulkan driver. CI runs it on Mesa's lavapipe twice: under
/// Xvfb, and on a headless Weston compositor with <c>SDL_VIDEODRIVER=wayland</c>, which is how both Linux
/// window systems get tested without Linux hardware. When <c>SDL_VIDEODRIVER</c> names one, the test checks
/// that the window really was opened on it.
/// </remarks>
[TestFixture]
[Platform("Linux")]
public class VulkanWindowSmokeTest
{
    private static readonly GlobalStatistic<long> stat_validation_messages = GlobalStatistics.Get<long>("Vulkan", "Validation Messages", StatisticKind.Cumulative);
    private static readonly GlobalStatistic<int> stat_swapchain_rebuilds = GlobalStatistics.Get<int>("Vulkan", "Swapchain Rebuilds", StatisticKind.Cumulative);
    private static readonly GlobalStatistic<int> stat_present_mode_switches = GlobalStatistics.Get<int>("Vulkan", "Present Mode Switches", StatisticKind.Cumulative);

    [Test]
    public void RendersPresentsAndResizesUnderValidation()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            Assert.Ignore("No display to open a window on; CI runs this under Xvfb.");

        if (!VulkanRenderer.IsSupported())
            Assert.Ignore("No Vulkan driver that can present to a window.");

        long validationBefore = stat_validation_messages.Value;
        string storagePath = Path.Combine(Path.GetTempPath(), $"sakura-vulkan-smoke-{Guid.NewGuid()}");

        try
        {
            using var host = new VulkanOnlyHost(storagePath);
            var app = new ScriptedApp(host);
            host.Run(app);

            long frames = host.DrawFrameStatistics?.TotalFrames ?? 0;

            string? requestedDriver = Environment.GetEnvironmentVariable("SDL_VIDEODRIVER");

            Assert.Multiple(() =>
            {
                if (!string.IsNullOrEmpty(requestedDriver))
                    Assert.That(app.VideoDriver, Is.EqualTo(requestedDriver), "the window system SDL_VIDEODRIVER asked for");

                Assert.That(app.Failure, Is.Null, "the script");
                Assert.That(app.Completed, Is.True, "the script ran to the end");
                Assert.That(host.Renderer, Is.InstanceOf<VulkanRenderer>(), "the window rendered through Vulkan");
                Assert.That(frames, Is.GreaterThan(30), "frames were drawn");
                Assert.That(app.StepsNotFollowed, Is.Empty, "steps the swapchain didn't follow (no rebuild, and no present mode switch for a mode change)");
                Assert.That(stat_validation_messages.Value - validationBefore, Is.Zero, "validation messages (the log has them)");
            });
        }
        finally
        {
            if (Directory.Exists(storagePath))
                Directory.Delete(storagePath, true);
        }
    }

    /// <summary>
    /// A desktop host that can only pick Vulkan, so a broken driver fails the test instead of quietly
    /// falling back to OpenGL. Its settings live in a fresh directory, so every run starts from the defaults
    /// and leaves nothing behind.
    /// </summary>
    private class VulkanOnlyHost : DesktopAppHost
    {
        private readonly string storagePath;

        public VulkanOnlyHost(string storagePath)
            : base("sakura-vulkan-smoke-test")
        {
            this.storagePath = storagePath;
        }

        public override IEnumerable<string> UserStoragePaths => [storagePath];

        protected override IEnumerable<RendererType> GetPreferredRenderers() => [RendererType.Vulkan];

        protected override IRenderer? CreateRendererForType(RendererType type, bool allowSoftware)
            => type == RendererType.Vulkan ? base.CreateRendererForType(type, allowSoftware) : null;
    }

    private unsafe partial class ScriptedApp : App
    {
        // Above the window's minimum size, which a smaller request is clamped to.
        private static readonly string[] steps = ["resize to 1024x700", "VSync", "resize to 900x640", "uncapped"];

        private readonly AppHost host;
        private Box box = null!;
        private int step;
        private int rebuildsAtLastStep;
        private int switchesAtLastStep;

        public bool Completed { get; private set; }
        public Exception? Failure { get; private set; }
        public List<string> StepsNotFollowed { get; } = [];

        /// <summary>
        /// The SDL video driver the window was opened with.
        /// </summary>
        public string? VideoDriver { get; private set; }

        protected override Assembly ResourceAssembly => typeof(VulkanWindowSmokeTest).Assembly;
        protected override string ResourceRootNamespace => "Sakura.Framework.Tests.Resources";

        public ScriptedApp(AppHost host)
        {
            this.host = host;
        }

        public override void Load()
        {
            base.Load();

            Add(box = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(100),
                Color = Color.LightPink,
            });
        }

        public override void Update()
        {
            base.Update();

            box.Rotation = (float)(Clock.CurrentTime / 10);

            VideoDriver ??= SDL_GetCurrentVideoDriver();

            // A step per second, in time rather than updates: multi-threaded updates run far faster than
            // frames are drawn.
            if (Clock.CurrentTime < (step + 1) * 1000)
                return;

            // A second is plenty for the draw thread to have followed the previous step.
            int rebuilds = stat_swapchain_rebuilds.Value;
            int switches = stat_present_mode_switches.Value;

            if (step > 0)
            {
                bool rebuilt = rebuilds != rebuildsAtLastStep;
                bool switched = switches != switchesAtLastStep;
                bool modeChange = steps[step - 1] is "VSync" or "uncapped";

                if (!rebuilt && !(modeChange && switched))
                    StepsNotFollowed.Add(steps[step - 1]);
            }

            rebuildsAtLastStep = rebuilds;
            switchesAtLastStep = switches;

            try
            {
                switch (step++)
                {
                    case 0:
                        resize(1024, 700);
                        break;

                    // The default limiter presents without vsync, so both of these change the present mode.
                    case 1:
                        host.FrameLimiter.Value = FrameSync.VSync;
                        break;

                    case 2:
                        resize(900, 640);
                        break;

                    case 3:
                        host.FrameLimiter.Value = FrameSync.Unlimited;
                        break;

                    default:
                        Completed = true;
                        host.Exit();
                        break;
                }
            }
            catch (Exception e)
            {
                Failure = e;
                host.Exit();
            }
        }

        /// <summary>
        /// Resizes the window on the thread SDL requires. Runs inline when that is already this thread
        /// (single-threaded execution).
        /// </summary>
        private static void resize(int width, int height)
        {
            var size = GCHandle.Alloc(new[] { width, height });

            try
            {
                SDL_RunOnMainThread(&resizeOnMainThread, GCHandle.ToIntPtr(size), true);
            }
            finally
            {
                size.Free();
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void resizeOnMainThread(IntPtr data)
        {
            int[] size = (int[])GCHandle.FromIntPtr(data).Target!;

            int count;
            var windows = SDL_GetWindows(&count);

            if (count > 0)
                SDL_SetWindowSize(windows[0], size[0], size[1]);

            SDL_free(windows);
        }
    }
}
