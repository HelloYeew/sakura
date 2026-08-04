// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Tests.Graphics;

[TestFixture]
public class UploadHeadroomTest
{
    private HeadlessTextureManager textureManager = null!;
    private int originalLimit;
    private int originalDecodes;
    private TimeSpan originalTimeout;

    /// <summary>
    /// The outstanding count is process-global, so anything a test leaves queued would leak into the next
    /// one. Every queueing renderer a test makes is registered here and fully drained on teardown.
    /// </summary>
    private readonly System.Collections.Generic.List<QueueingRenderer> queueingRenderers = new System.Collections.Generic.List<QueueingRenderer>();

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        originalLimit = TextureUploads.MaxOutstandingUploads;
        originalDecodes = TextureUploads.MaxConcurrentDecodes;
        originalTimeout = TextureUploads.UploadHeadroomTimeout;
    }

    [TearDown]
    public void TearDown()
    {
        // Drain before restoring the limit, so the outstanding count is back to zero for the next test.
        foreach (var renderer in queueingRenderers)
            renderer.DrainAll();

        queueingRenderers.Clear();

        TextureUploads.MaxOutstandingUploads = originalLimit;
        TextureUploads.MaxConcurrentDecodes = originalDecodes;
        TextureUploads.UploadHeadroomTimeout = originalTimeout;
        textureManager.Dispose();

        Assert.That(TextureUploads.OutstandingUploads, Is.Zero, "a test must not leak outstanding uploads");
    }

    private QueueingRenderer queueing()
    {
        var renderer = new QueueingRenderer(textureManager);
        queueingRenderers.Add(renderer);
        return renderer;
    }

    /// <summary>
    /// Off by default. The in-app profile did not support enforcing it — the pool misses because renting and
    /// returning happen on different threads, which no concurrency limit addresses. See
    /// <c>ImageDecodeResidencyProbe.MeasureRentAndReturnThreadAffinity</c>.
    /// </summary>
    [Test]
    public void TheDefaultIsOff()
    {
        Assert.That(TextureUploads.MaxOutstandingUploads, Is.Zero);
    }

    /// <summary>
    /// The headless renderer runs uploads inline, so the slot is taken and given back within the call —
    /// which is also why the whole suite does not deadlock on this gate.
    /// </summary>
    [Test]
    public void AnUploadThatRunsImmediatelyReleasesItsSlot()
    {
        var renderer = new HeadlessRenderer(textureManager);

        Assert.That(TextureUploads.OutstandingUploads, Is.Zero, "nothing outstanding to begin with");

        Assert.That(create(renderer), Is.Not.Null);

        Assert.That(TextureUploads.OutstandingUploads, Is.Zero, "and nothing left outstanding after");
    }

    /// <summary>
    /// The gate proper: with one slot and one upload already queued and undrained, a second decode must
    /// wait, and must proceed the moment the first upload runs.
    /// </summary>
    [Test]
    public void ADecodeWaitsForHeadroomAndProceedsWhenTheQueueDrains()
    {
        TextureUploads.MaxOutstandingUploads = 1;
        TextureUploads.UploadHeadroomTimeout = TimeSpan.FromSeconds(30);

        var renderer = queueing();

        // Fills the single slot; nothing drains it yet.
        Assert.That(create(renderer), Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.PendingCount, Is.EqualTo(1));
            Assert.That(TextureUploads.OutstandingUploads, Is.EqualTo(1));
        });

        var second = Task.Run(() => create(renderer));

        Assert.That(second.Wait(TimeSpan.FromMilliseconds(300)), Is.False,
            "the second decode must block while the only slot is occupied");

        renderer.DrainOne();

        Assert.That(second.Wait(TimeSpan.FromSeconds(10)), Is.True,
            "and must be released as soon as the upload runs");
        Assert.That(second.Result, Is.Not.Null);
    }

    /// <summary>
    /// Zero or less means unbounded, so a decode never waits however deep the queue is.
    /// </summary>
    [Test]
    public void TheLimitCanBeDisabled()
    {
        TextureUploads.MaxOutstandingUploads = 0;
        TextureUploads.UploadHeadroomTimeout = TimeSpan.FromSeconds(30);

        var renderer = queueing();

        for (int i = 0; i < 5; i++)
            Assert.That(create(renderer), Is.Not.Null, $"decode {i} must not wait");

        Assert.That(renderer.PendingCount, Is.EqualTo(5), "all five are queued and none drained");
    }

    /// <summary>
    /// A queue nobody pumps must not wedge loading. The wait gives up and stops enforcing the limit until
    /// something drains, so a stalled queue costs one timeout in total rather than one per decode —
    /// degrading to the behaviour before this gate existed rather than hanging.
    /// </summary>
    [Test]
    public void AQueueThatNeverDrainsDoesNotWedgeLoading()
    {
        TextureUploads.MaxOutstandingUploads = 1;
        TextureUploads.UploadHeadroomTimeout = TimeSpan.FromMilliseconds(150);

        var renderer = queueing();

        Assert.That(create(renderer), Is.Not.Null);
        Assert.That(TextureUploads.OutstandingUploads, Is.EqualTo(1));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.That(onPoolThread(renderer), Is.Not.Null, "the decode must still complete");
        clock.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(clock.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(100), "after waiting for headroom");
            Assert.That(renderer.PendingCount, Is.EqualTo(2), "and its upload is queued like any other");
        });

        // The limit is no longer enforced, so the next decode is not charged the timeout a second time.
        clock.Restart();
        Assert.That(onPoolThread(renderer), Is.Not.Null);
        clock.Stop();

        Assert.That(clock.ElapsedMilliseconds, Is.LessThan(100), "a stalled queue costs one timeout, not one per decode");

        // ...and draining re-arms it, rather than leaving the limit off for the rest of the process.
        renderer.DrainOne();

        Assert.That(TextureUploads.OutstandingUploads, Is.EqualTo(2), "two of the three are still queued");

        var blocked = Task.Run(() => create(renderer));

        Assert.That(blocked.Wait(TimeSpan.FromMilliseconds(50)), Is.False, "the limit is enforced again once the queue moves");

        renderer.DrainAll();

        Assert.That(blocked.Wait(TimeSpan.FromSeconds(10)), Is.True);
    }

    /// <summary>
    /// The limit only applies to thread-pool callers. The upload queue is drained by the draw thread, so a
    /// load running on a dedicated framework thread — the draw thread most of all — must never be made to
    /// wait for uploads that only that thread can run.
    /// </summary>
    [Test]
    public void ANonPoolThreadIsNeverMadeToWait()
    {
        TextureUploads.MaxOutstandingUploads = 1;
        TextureUploads.UploadHeadroomTimeout = TimeSpan.FromSeconds(30);

        var renderer = queueing();

        // Occupy the only slot, from the pool so that it is genuinely subject to the limit.
        Assert.That(onPoolThread(renderer), Is.Not.Null);
        Assert.That(TextureUploads.OutstandingUploads, Is.EqualTo(1));

        Texture? fromDedicatedThread = null;
        var thread = new Thread(() => fromDedicatedThread = create(renderer)) { IsBackground = true };

        thread.Start();

        Assert.That(thread.Join(TimeSpan.FromSeconds(2)), Is.True,
            "a dedicated thread must not block on the limit — it would be a 30 second wait if it did");
        Assert.That(fromDedicatedThread, Is.Not.Null);
    }

    private static Texture? onPoolThread(IRenderer renderer)
        => Task.Run(() => create(renderer)).GetAwaiter().GetResult();

    private static Texture? create(IRenderer renderer)
        => TextureUploads.FromStream(
            new MemoryStream(new byte[16]),
            new TextureCreationOptions(),
            renderer,
            new StubLoader(),
            new SharedTextureStore(),
            static _ => { });

    /// <summary>
    /// Produces a small but valid image so <see cref="TextureUploads.FromStream"/> reaches the upload,
    /// which is the part under test.
    /// </summary>
    private class StubLoader : IImageLoader
    {
        public ImageRawData Load(Stream stream) => Load(stream, ImageLoadOptions.FullSize);

        public ImageRawData Load(Stream stream, int maxDimension) => Load(stream, ImageLoadOptions.MaxDimension(maxDimension));

        public ImageRawData Load(Stream stream, ImageLoadOptions options) => ImageRawData.Rent(4, 4);
    }

    /// <summary>
    /// Stands in for a real backend: uploads are queued for a draw thread rather than run inline, so the
    /// test controls exactly when a slot is released. Re-listing <see cref="IRenderer"/> re-implements
    /// <see cref="IRenderer.ScheduleTextureUpload"/>, which <see cref="HeadlessRenderer"/> otherwise takes
    /// from the interface's default (run immediately).
    /// </summary>
    private class QueueingRenderer : HeadlessRenderer, IRenderer
    {
        private readonly ConcurrentQueue<Action> pending = new ConcurrentQueue<Action>();

        public QueueingRenderer(HeadlessTextureManager textureManager)
            : base(textureManager)
        {
        }

        public int PendingCount => pending.Count;

        public void ScheduleTextureUpload(Action upload, long approximateBytes) => pending.Enqueue(upload);

        public void DrainOne()
        {
            if (pending.TryDequeue(out var upload))
                upload();
        }

        public void DrainAll()
        {
            while (pending.TryDequeue(out var upload))
                upload();
        }
    }
}
