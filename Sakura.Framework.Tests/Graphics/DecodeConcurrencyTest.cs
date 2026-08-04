// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Tests.Graphics;

[TestFixture]
public class DecodeConcurrencyTest
{
    private HeadlessTextureManager textureManager = null!;
    private int originalLimit;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        originalLimit = TextureUploads.MaxConcurrentDecodes;
    }

    [TearDown]
    public void TearDown()
    {
        TextureUploads.MaxConcurrentDecodes = originalLimit;
        textureManager.Dispose();
    }

    [Test]
    public void TheDefaultIsOneAtATime()
    {
        Assert.That(TextureUploads.MaxConcurrentDecodes, Is.EqualTo(1));
    }

    /// <summary>
    /// Zero or less would deadlock a <see cref="SemaphoreSlim"/> built from it, so it clamps.
    /// </summary>
    [Test]
    public void TheLimitCannotBeSetBelowOne()
    {
        TextureUploads.MaxConcurrentDecodes = 0;
        Assert.That(TextureUploads.MaxConcurrentDecodes, Is.EqualTo(1));

        TextureUploads.MaxConcurrentDecodes = -5;
        Assert.That(TextureUploads.MaxConcurrentDecodes, Is.EqualTo(1));
    }

    /// <summary>
    /// The gate actually gates: a decoder that blocks inside <c>Load</c> keeps every other caller out, and
    /// the observed peak overlap never exceeds the configured limit.
    /// </summary>
    [Test]
    public void ConcurrentCallersNeverExceedTheLimit()
    {
        TextureUploads.MaxConcurrentDecodes = 2;

        var loader = new BlockingLoader();

        var tasks = new Task[8];

        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => TextureUploads.FromStream(
                new MemoryStream(new byte[16]),
                new TextureCreationOptions(),
                new HeadlessRenderer(textureManager),
                loader,
                new SharedTextureStore(),
                static _ => { }));
        }

        Assert.That(Task.WaitAll(tasks, TimeSpan.FromSeconds(30)), Is.True, "the gate must not deadlock");

        Assert.Multiple(() =>
        {
            Assert.That(loader.PeakConcurrent, Is.LessThanOrEqualTo(2), "never more decoders at once than the limit");
            Assert.That(loader.TotalCalls, Is.EqualTo(8), "and every caller still got its turn");
        });
    }

    /// <summary>
    /// Raising the limit replaces the gate. A decode already in flight holds a permit on the old instance,
    /// so this is the case where a naive implementation releases into a semaphore that never issued it.
    /// </summary>
    [Test]
    public void ChangingTheLimitMidFlightDoesNotCorruptTheGate()
    {
        TextureUploads.MaxConcurrentDecodes = 1;

        var loader = new BlockingLoader();
        var entered = new ManualResetEventSlim(false);
        loader.OnEntered = () => entered.Set();

        var first = Task.Run(() => TextureUploads.FromStream(
            new MemoryStream(new byte[16]), new TextureCreationOptions(),
            new HeadlessRenderer(textureManager), loader, new SharedTextureStore(), static _ => { }));

        Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True, "the first decode has to be in flight");

        // Swap the gate out from under it.
        TextureUploads.MaxConcurrentDecodes = 4;

        loader.Unblock();
        Assert.That(first.Wait(TimeSpan.FromSeconds(10)), Is.True);

        // The new gate must still admit exactly its limit and no more.
        loader.Reset();
        loader.Block();

        var tasks = new Task[6];

        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => TextureUploads.FromStream(
                new MemoryStream(new byte[16]), new TextureCreationOptions(),
                new HeadlessRenderer(textureManager), loader, new SharedTextureStore(), static _ => { }));
        }

        Assert.That(Task.WaitAll(tasks, TimeSpan.FromSeconds(30)), Is.True);
        Assert.That(loader.PeakConcurrent, Is.LessThanOrEqualTo(4));
    }

    /// <summary>
    /// A loader that records how many callers are inside it at once, and can be held there on demand.
    /// Returns nothing decodable, which <see cref="TextureUploads.FromStream"/> treats as a failed decode —
    /// enough to exercise the gate without needing a real image or GPU.
    /// </summary>
    private class BlockingLoader : IImageLoader
    {
        private int concurrent;
        private int peak;
        private int calls;
        private ManualResetEventSlim? hold;

        public int PeakConcurrent => Volatile.Read(ref peak);
        public int TotalCalls => Volatile.Read(ref calls);

        public Action? OnEntered;

        public void Block() => hold = new ManualResetEventSlim(false);

        public void Unblock() => hold?.Set();

        public void Reset()
        {
            Volatile.Write(ref peak, 0);
            Volatile.Write(ref calls, 0);
        }

        public BlockingLoader()
        {
            Block();

            // Released shortly after each entry, so the peak is observable without the test having to
            // coordinate every caller.
            Task.Run(async () =>
            {
                await Task.Delay(150).ConfigureAwait(false);
                Unblock();
            });
        }

        public ImageRawData Load(Stream stream) => Load(stream, ImageLoadOptions.FullSize);

        public ImageRawData Load(Stream stream, int maxDimension) => Load(stream, ImageLoadOptions.MaxDimension(maxDimension));

        public ImageRawData Load(Stream stream, ImageLoadOptions options)
        {
            Interlocked.Increment(ref calls);

            int now = Interlocked.Increment(ref concurrent);

            int observed = Volatile.Read(ref peak);
            while (now > observed)
            {
                int previous = Interlocked.CompareExchange(ref peak, now, observed);
                if (previous == observed)
                    break;

                observed = previous;
            }

            OnEntered?.Invoke();

            try
            {
                hold?.Wait(TimeSpan.FromSeconds(5));
                return default;
            }
            finally
            {
                Interlocked.Decrement(ref concurrent);
            }
        }
    }
}
