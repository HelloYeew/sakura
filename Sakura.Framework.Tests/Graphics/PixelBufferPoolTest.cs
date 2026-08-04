// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Tests.Graphics;

[TestFixture]
public class PixelBufferPoolTest
{
    private const int width = 400;
    private const int height = 300;

    /// <summary>
    /// The exact shape the decode path uses, and the one that regressed: rent on a pool thread, release on a
    /// different dedicated thread, repeatedly. Counts distinct arrays, so a miss cannot hide behind
    /// equal-looking sizes.
    /// </summary>
    [Test]
    public void ABufferReleasedOnAnotherThreadIsReusedByTheNextRental()
    {
        const int cycles = 20;

        var seen = new HashSet<byte[]>();
        var toRelease = new System.Collections.Concurrent.BlockingCollection<ImageRawData>();

        var releaseThread = new Thread(() =>
        {
            foreach (var raw in toRelease.GetConsumingEnumerable())
                raw.Dispose();
        })
        { IsBackground = true };

        releaseThread.Start();

        for (int i = 0; i < cycles; i++)
        {
            var raw = Task.Run(() => ImageRawData.Rent(width, height)).GetAwaiter().GetResult();

            byte[]? array = raw.BackingArray;
            Assert.That(array, Is.Not.Null);
            seen.Add(array!);

            // Hand it to the other thread and wait for it to come back to the pool before renting again,
            // mirroring a decode that waits on its upload.
            toRelease.Add(raw);

            while (toRelease.Count > 0)
                Thread.Sleep(1);

            Thread.Sleep(2);
        }

        toRelease.CompleteAdding();
        Assert.That(releaseThread.Join(TimeSpan.FromSeconds(10)), Is.True);

        // ArrayPool<byte>.Shared measured between 2 and 40 distinct arrays for this shape across runs; a
        // pool without a thread-static layer measured 2 or 3. A handful of slack for scheduling, but nowhere
        // near one per rental.
        Assert.That(seen, Has.Count.LessThanOrEqualTo(4),
            $"a buffer released on another thread must be reused, got {seen.Count} distinct arrays for {cycles} rentals");
    }

    /// <summary>
    /// The simplest property, and the one that must never regress: same thread, rent and release in a loop,
    /// exactly one array.
    /// </summary>
    [Test]
    public void SameThreadRentalsReuseOneBuffer()
    {
        var seen = new HashSet<byte[]>();

        for (int i = 0; i < 20; i++)
        {
            var raw = ImageRawData.Rent(width, height);

            seen.Add(raw.BackingArray!);
            raw.Dispose();
        }

        Assert.That(seen, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// A rental larger than the pool's largest bucket must still succeed — it is simply not pooled — so an
    /// unusually large image cannot fail to load.
    /// </summary>
    [Test]
    public void AnOversizedRentalStillSucceeds()
    {
        // 5000x5000 RGBA is 100 MB, past the 64 MB the pool keeps buckets for.
        using var raw = ImageRawData.Rent(5000, 5000);

        Assert.Multiple(() =>
        {
            Assert.That(raw.IsValid, Is.True);
            Assert.That(raw.Data.Length, Is.EqualTo(5000 * 5000 * 4));
        });
    }

    /// <summary>
    /// Disposal is idempotent across struct copies, which matters more with a dedicated pool than with the
    /// shared one: returning the same array twice would hand it out to two owners at once.
    /// </summary>
    [Test]
    public void DoubleDisposeDoesNotReturnTheSameArrayTwice()
    {
        var raw = ImageRawData.Rent(width, height);
        var copy = raw;

        byte[] array = raw.BackingArray!;

        raw.Dispose();
        copy.Dispose();

        // If the array had been returned twice, the pool would hold it twice and hand it to both of these.
        var first = ImageRawData.Rent(width, height);
        var second = ImageRawData.Rent(width, height);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(first.BackingArray, Is.SameAs(array), "the single return is reused");
                Assert.That(second.BackingArray, Is.Not.SameAs(first.BackingArray), "and was not handed out twice");
            });
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }
}
