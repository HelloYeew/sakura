// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Statistics;

[TestFixture]
public class NativeMemoryTrackerTest
{
    [SetUp]
    public void SetUp() => NativeMemoryTracker.Reset();

    [TearDown]
    public void TearDown() => NativeMemoryTracker.Reset();

    [Test]
    public void ALeaseIsCountedUntilItIsDisposed()
    {
        var lease = NativeMemoryTracker.Add(NativeMemoryCategory.Textures, 1024);

        Assert.Multiple(() =>
        {
            Assert.That(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Textures), Is.EqualTo(1024));
            Assert.That(NativeMemoryTracker.TotalBytes, Is.EqualTo(1024));
        });

        lease.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Textures), Is.Zero);
            Assert.That(NativeMemoryTracker.TotalBytes, Is.Zero);
        });
    }

    [Test]
    public void CategoriesAreCountedSeparatelyAndSumToTheTotal()
    {
        using var textures = NativeMemoryTracker.Add(NativeMemoryCategory.Textures, 100);
        using var frameBuffers = NativeMemoryTracker.Add(NativeMemoryCategory.FrameBuffers, 200);
        using var audio = NativeMemoryTracker.Add(NativeMemoryCategory.Audio, 400);

        Assert.Multiple(() =>
        {
            Assert.That(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Textures), Is.EqualTo(100));
            Assert.That(NativeMemoryTracker.BytesFor(NativeMemoryCategory.FrameBuffers), Is.EqualTo(200));
            Assert.That(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Audio), Is.EqualTo(400));
            Assert.That(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Video), Is.Zero);
            Assert.That(NativeMemoryTracker.TotalBytes, Is.EqualTo(700), "the total is the point — a category on its own does not answer 'what grew'");
        });
    }

    /// <summary>
    /// Releasing twice must not subtract twice. A counter that can go too low is worse than one that is
    /// merely approximate, because it makes a real leak look like it resolved itself.
    /// </summary>
    [Test]
    public void DisposingALeaseTwiceCountsOnce()
    {
        var lease = NativeMemoryTracker.Add(NativeMemoryCategory.Audio, 500);
        using var other = NativeMemoryTracker.Add(NativeMemoryCategory.Audio, 300);

        lease.Dispose();
        lease.Dispose();

        Assert.That(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Audio), Is.EqualTo(300));
    }

    [Test]
    public void ADisposedLeaseReportsNoBytes()
    {
        var lease = NativeMemoryTracker.Add(NativeMemoryCategory.Video, 64);
        Assert.That(lease.Bytes, Is.EqualTo(64));

        lease.Dispose();
        Assert.That(lease.Bytes, Is.Zero);
    }

    /// <summary>
    /// A failed or empty allocation should not need special-casing at the call site.
    /// </summary>
    [TestCase(0)]
    [TestCase(-1)]
    public void ANonPositiveSizeIsAcceptedAndCountsNothing(long size)
    {
        using var lease = NativeMemoryTracker.Add(NativeMemoryCategory.Other, size);

        Assert.Multiple(() =>
        {
            Assert.That(lease.Bytes, Is.Zero);
            Assert.That(NativeMemoryTracker.TotalBytes, Is.Zero);
        });
    }

    /// <summary>
    /// The peak is what distinguishes a footprint that spiked and recovered from one that never spiked —
    /// the two are identical readings otherwise, and only one of them explains a stutter.
    /// </summary>
    [Test]
    public void ThePeakSurvivesTheAllocationBeingReleased()
    {
        var lease = NativeMemoryTracker.Add(NativeMemoryCategory.Textures, 8192);
        lease.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(NativeMemoryTracker.TotalBytes, Is.Zero);
            Assert.That(NativeMemoryTracker.PeakTotalBytes, Is.EqualTo(8192));
        });
    }

    [Test]
    public void ThePeakTracksTheTotalRatherThanAnySingleCategory()
    {
        using var a = NativeMemoryTracker.Add(NativeMemoryCategory.Textures, 1000);
        using var b = NativeMemoryTracker.Add(NativeMemoryCategory.Audio, 1000);

        Assert.That(NativeMemoryTracker.PeakTotalBytes, Is.EqualTo(2000));
    }

    /// <summary>
    /// Statistics are what the Ctrl+F3 overlay and any external reader actually see, so the in-memory
    /// counters agreeing among themselves is not enough.
    /// </summary>
    [Test]
    public void EveryCategoryIsPublishedToGlobalStatistics()
    {
        using var lease = NativeMemoryTracker.Add(NativeMemoryCategory.FrameBuffers, 4096);

        Assert.Multiple(() =>
        {
            Assert.That(GlobalStatistics.Get<long>("Native Memory", "FrameBuffers").Value, Is.EqualTo(4096));
            Assert.That(GlobalStatistics.Get<long>("Native Memory", "Total").Value, Is.EqualTo(4096));
            Assert.That(GlobalStatistics.Get<long>("Native Memory", "Peak Total").Value, Is.EqualTo(4096));

            // Present even at zero: a category that only appears once it is non-zero reads as "not
            // measured" exactly when someone is looking for what grew.
            Assert.That(GlobalStatistics.Get<long>("Native Memory", "Video").Value, Is.Zero);
        });
    }

    /// <summary>
    /// Allocation happens on the draw thread while release can arrive from a finalizer thread or the
    /// native disposal queue, so the counters are maintained with interlocked arithmetic rather than under
    /// a lock. This is the test that would catch that being downgraded to plain arithmetic.
    /// </summary>
    [Test]
    public void ConcurrentLeasesAccountExactly()
    {
        const int threads = 8;
        const int per_thread = 500;
        const long size = 16;

        var leases = new List<NativeMemoryLease>[threads];

        Parallel.For(0, threads, t =>
        {
            var taken = new List<NativeMemoryLease>(per_thread);

            for (int i = 0; i < per_thread; i++)
                taken.Add(NativeMemoryTracker.Add(NativeMemoryCategory.Textures, size));

            leases[t] = taken;
        });

        Assert.That(NativeMemoryTracker.TotalBytes, Is.EqualTo(threads * per_thread * size));

        Parallel.ForEach(leases, taken =>
        {
            foreach (var lease in taken)
                lease.Dispose();
        });

        Assert.That(NativeMemoryTracker.TotalBytes, Is.Zero, "every concurrent release must land exactly once");
    }

    /// <summary>
    /// The finalizer is the net for wrappers that have none of their own — <c>D3D11Texture</c> is the
    /// framework's real case, since its resources are COM objects that are already finalizable. Without
    /// it, an undisposed texture would inflate the counter for the rest of the process.
    /// </summary>
    [Test]
    public void AnAbandonedLeaseIsReclaimedByItsFinalizer()
    {
        takeAndAbandon();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(NativeMemoryTracker.TotalBytes, Is.Zero);

        // Kept out of the test body so the lease cannot stay alive in a local slot the JIT has not
        // released — which would make this pass or fail depending on build configuration.
        static void takeAndAbandon() => NativeMemoryTracker.Add(NativeMemoryCategory.Textures, 2048);
    }

    [Test]
    public void ResetClearsEveryCategoryAndThePeak()
    {
        using var lease = NativeMemoryTracker.Add(NativeMemoryCategory.Audio, 777);

        NativeMemoryTracker.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(NativeMemoryTracker.TotalBytes, Is.Zero);
            Assert.That(NativeMemoryTracker.PeakTotalBytes, Is.Zero);
            Assert.That(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Audio), Is.Zero);
        });
    }

    /// <summary>
    /// A lease taken before a reset must not push a counter negative when it is disposed afterwards. The
    /// tracker floors at zero rather than trusting that every add and release pair up across a reset,
    /// because one mismatch would otherwise corrupt the total for every category that shares it.
    /// </summary>
    [Test]
    public void ALeaseDisposedAfterAResetCannotDriveTheTotalNegative()
    {
        var lease = NativeMemoryTracker.Add(NativeMemoryCategory.Textures, 4096);

        NativeMemoryTracker.Reset();

        lease.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(NativeMemoryTracker.TotalBytes, Is.Zero);
            Assert.That(NativeMemoryTracker.BytesFor(NativeMemoryCategory.Textures), Is.Zero);
        });
    }
}
