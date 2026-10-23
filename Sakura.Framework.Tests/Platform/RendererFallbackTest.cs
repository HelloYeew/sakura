// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Configurations;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vulkan;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
using Path = System.IO.Path;

namespace Sakura.Framework.Tests.Platform;

/// <summary>
/// A renderer that fails to start doesn't end the app: the host disposes it, destroys the window, creates
/// the window again for its next choice, and runs on that. The first renderer is made to fail once it is
/// fully set up, so what gets torn down is a working renderer with its surface or context, not a half-made
/// one.
/// </summary>
/// <remarks>
/// Linux only, for the reason <see cref="VulkanWindowSmokeTest"/> is. CI runs it under Xvfb on Mesa, where
/// OpenGL can't open a window at all: the renderer asks for an sRGB-capable visual, and Xvfb's llvmpipe
/// offers none. That is a real-world failure too (an X server without sRGB visuals), so where OpenGL fails
/// on its own it stands in for the injected failure.
/// </remarks>
[TestFixture]
[Platform("Linux")]
public class RendererFallbackTest
{
    private static readonly GlobalStatistic<long> stat_validation_messages = GlobalStatistics.Get<long>("Vulkan", "Validation Messages", StatisticKind.Cumulative);

    [SetUp]
    public void RequireWindowAndVulkan()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            Assert.Ignore("No display to open a window on; CI runs this under Xvfb.");

        if (!VulkanRenderer.IsSupported())
            Assert.Ignore("No Vulkan driver that can present to a window.");
    }

    /// <summary>
    /// OpenGL, offered first, fails (made to, or on its own); the app comes up on Vulkan, in a window made
    /// for Vulkan after the OpenGL one was destroyed.
    /// </summary>
    [Test]
    public void FailingOpenGLFallsBackToVulkan()
    {
        long validationBefore = stat_validation_messages.Value;

        runHost(RendererType.OpenGL, RendererType.Vulkan, host =>
        {
            Assert.That(host.Failures.Select(f => f.Type), Is.EqualTo(new[] { RendererType.OpenGL }), "OpenGL failed, once");
            Assert.That(host.Renderer, Is.InstanceOf<VulkanRenderer>(), "the app ran on Vulkan");
            Assert.That(host.DrawFrameStatistics?.TotalFrames ?? 0, Is.GreaterThan(30), "frames were drawn after the fallback");
        });

        Assert.That(stat_validation_messages.Value - validationBefore, Is.Zero, "validation messages (the log has them)");
    }

    /// <summary>
    /// Vulkan, offered first, fails after starting. Its teardown (device, swapchain, surface) has to be
    /// clean whatever comes next. Where OpenGL works the app runs on it; where it doesn't, startup ends
    /// with an error naming both failures rather than a crash.
    /// </summary>
    [Test]
    public void FailingVulkanIsTornDownAndTheNextTried()
    {
        long validationBefore = stat_validation_messages.Value;
        Exception? startupFailure = null;
        FailingHost? finished = null;

        try
        {
            runHost(RendererType.Vulkan, RendererType.OpenGL, host => finished = host);
        }
        catch (InvalidOperationException e) when (e.Message.Contains("No renderer could start", StringComparison.Ordinal))
        {
            startupFailure = e;
        }

        Assert.Multiple(() =>
        {
            Assert.That(stat_validation_messages.Value - validationBefore, Is.Zero, "validation messages from the Vulkan teardown (the log has them)");

            if (startupFailure != null)
            {
                var failures = ((AggregateException)startupFailure.InnerException!).InnerExceptions;
                Assert.That(failures, Has.Count.EqualTo(2), "both failures are reported");
                Assert.That(failures[0].Message, Does.StartWith("Injected"), "Vulkan's, first");
            }
            else
            {
                Assert.That(finished!.Failures.Select(f => f.Type), Is.EqualTo(new[] { RendererType.Vulkan }), "Vulkan failed, once");
                Assert.That(finished.Renderer, Is.InstanceOf<GLRenderer>(), "the app ran on OpenGL");
            }
        });
    }

    /// <summary>
    /// With Automatic and only a software Vulkan device, which is what CI has, OpenGL goes first: on a machine
    /// whose GPU lacks a Vulkan driver, OpenGL may still have the GPU. Vulkan is used once nothing else is left.
    /// </summary>
    [Test]
    public void SoftwareVulkanWaitsBehindOpenGL()
    {
        if (VulkanRenderer.HasHardwareDevice())
            Assert.Ignore("This machine's Vulkan runs on a GPU; CI's runs on the CPU.");

        runOrderHost(_ => { }, (host, _) =>
        {
            Assert.That(host.Requests[0], Is.EqualTo((RendererType.Vulkan, false, false)), "Vulkan was passed over while OpenGL was left");
            Assert.That(host.Requests[1].Type, Is.EqualTo(RendererType.OpenGL));

            if (host.Renderer is VulkanRenderer)
                Assert.That(host.Requests[^1], Is.EqualTo((RendererType.Vulkan, true, true)), "Vulkan, once it was the last left");
            else
                Assert.That(host.Renderer, Is.InstanceOf<GLRenderer>());
        });
    }

    /// <summary>
    /// A renderer recorded as having crashed the app while starting goes after the others, and the record
    /// is cleared once it does start.
    /// </summary>
    [Test]
    public void CrashedRendererIsTriedLast()
    {
        runOrderHost(record => File.WriteAllText(record, "Vulkan\n"), (host, record) =>
        {
            Assert.That(host.Requests[0].Type, Is.EqualTo(RendererType.OpenGL), "OpenGL first, ahead of the Vulkan that crashed");

            bool stillRecorded = File.Exists(record) && File.ReadAllText(record).Contains("Vulkan", StringComparison.Ordinal);
            Assert.That(stillRecorded, Is.EqualTo(host.Renderer is not VulkanRenderer), "Vulkan's crash is forgotten only once it starts");
        });
    }

    private static void runHost(RendererType first, RendererType second, Action<FailingHost> check)
    {
        string storagePath = Path.Combine(Path.GetTempPath(), $"sakura-renderer-fallback-{Guid.NewGuid()}");

        try
        {
            using var host = new FailingHost(storagePath, first, second);
            host.Run(new ClosingApp(host));
            Assert.Multiple(() => check(host));
        }
        finally
        {
            if (Directory.Exists(storagePath))
                Directory.Delete(storagePath, true);
        }
    }

    /// <summary>
    /// A desktop host offering two renderers, the first of which throws once it has been set up, and which
    /// records every renderer that failed. Its settings live in a fresh directory, as in
    /// <see cref="VulkanWindowSmokeTest"/>. The first is configured as though the player chose it, which is
    /// what lets Vulkan go first on CI's software device rather than wait behind OpenGL.
    /// </summary>
    private class FailingHost : DesktopAppHost
    {
        private readonly string storagePath;
        private readonly RendererType first;
        private readonly RendererType second;

        public List<(RendererType Type, Exception Failure)> Failures { get; } = [];

        public FailingHost(string storagePath, RendererType first, RendererType second)
            : base("sakura-renderer-fallback-test")
        {
            this.storagePath = storagePath;
            this.first = first;
            this.second = second;
        }

        public override IEnumerable<string> UserStoragePaths => [storagePath];

        protected override IEnumerable<RendererType> GetPreferredRenderers() => [first, second];

        protected override void SetupForRun()
        {
            base.SetupForRun();
            FrameworkConfigManager.Get<RendererType>(FrameworkSetting.RendererType).Value = first;
        }

        protected override void SetupRenderer()
        {
            base.SetupRenderer();

            bool isFirst = first == RendererType.Vulkan ? Renderer is VulkanRenderer : Renderer is GLRenderer;

            if (isFirst && Failures.Count == 0)
                throw new InvalidOperationException($"Injected: {first} failed after starting.");
        }

        protected override bool OnRendererFailed(Exception exception)
        {
            Failures.Add((Renderer is VulkanRenderer ? RendererType.Vulkan : RendererType.OpenGL, exception));
            return base.OnRendererFailed(exception);
        }
    }

    /// <param name="prepare">Given the path of the host's crash record, before the host runs.</param>
    /// <param name="check">Given the host once it has run, and the path of its crash record.</param>
    private static void runOrderHost(Action<string> prepare, Action<OrderHost, string> check)
    {
        string storagePath = Path.Combine(Path.GetTempPath(), $"sakura-renderer-order-{Guid.NewGuid()}");
        string record = Path.Combine(storagePath, OrderHost.NAME, RendererStartupRecord.FILENAME);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(record)!);
            prepare(record);

            using var host = new OrderHost(storagePath);
            host.Run(new ClosingApp(host));
            Assert.Multiple(() => check(host, record));
        }
        finally
        {
            if (Directory.Exists(storagePath))
                Directory.Delete(storagePath, true);
        }
    }

    /// <summary>
    /// A desktop host on Automatic, offering Vulkan then OpenGL as Linux does, which records each renderer it
    /// was asked for, whether a software device was allowed, and whether it made one.
    /// </summary>
    private class OrderHost : DesktopAppHost
    {
        public const string NAME = "sakura-renderer-order-test";

        private readonly string storagePath;

        public List<(RendererType Type, bool AllowSoftware, bool Created)> Requests { get; } = [];

        public OrderHost(string storagePath)
            : base(NAME)
        {
            this.storagePath = storagePath;
        }

        public override IEnumerable<string> UserStoragePaths => [storagePath];

        protected override IEnumerable<RendererType> GetPreferredRenderers() => [RendererType.Vulkan, RendererType.OpenGL];

        protected override IRenderer? CreateRendererForType(RendererType type, bool allowSoftware)
        {
            var renderer = base.CreateRendererForType(type, allowSoftware);
            Requests.Add((type, allowSoftware, renderer != null));
            return renderer;
        }
    }

    /// <summary>
    /// Draws something for two seconds, then exits.
    /// </summary>
    private partial class ClosingApp : App
    {
        private readonly AppHost host;
        private Box box = null!;

        public ClosingApp(AppHost host)
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
                Size = new Vector2(100, 100),
                Color = Color.LightPink,
            });
        }

        public override void Update()
        {
            base.Update();

            box.Rotation = (float)(Clock.CurrentTime / 10);

            if (Clock.CurrentTime > 2000)
                host.Exit();
        }
    }
}
