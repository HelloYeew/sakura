// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Textures.ImageSharp;
using Sakura.Framework.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sakura.Framework.Tests.Graphics.Textures;

[TestFixture]
[Explicit("measurement probe, run by name, will remove")]
public class ImageDecodeResidencyProbe
{
    private const int iterations = 20;
    private const int warmup = 3;

    /// <summary>
    /// A 1920x1080 photo-like JPEG. Noise rather than a flat fill, so the encoded file is a realistic
    /// megabyte or so instead of a handful of compressible bytes.
    /// </summary>
    private static byte[] encodedBackground()
    {
        var random = new Random(1234);

        using var image = new Image<Rgba32>(1920, 1080);

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);

                for (int x = 0; x < row.Length; x++)
                    row[x] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            }
        });

        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// The step that changed, in isolation: obtain the image's dimensions and leave a stream the decoder
    /// could read, without decoding. No full decode to hide the difference in.
    /// </summary>
    [Test]
    public void MeasureHeaderReadStrategies()
    {
        byte[] encoded = encodedBackground();

        TestContext.Out.WriteLine($"encoded file: {encoded.Length:N0} bytes, {iterations} iterations\n");

        foreach (int concurrency in new[] { 1, 8 })
        {
            TestContext.Out.WriteLine($"-- header read only, {concurrency} at a time --");

            report("identify + rewind (now)", concurrency, () => identifyAndRewind(encoded));
            report("rent first (was)", concurrency, () => rentFirst(encoded));
            report("copy first (non-seekable)", concurrency, () => copyFirst(encoded));

            TestContext.Out.WriteLine(string.Empty);
        }
    }

    /// <summary>
    /// The same choice end to end, through the real loader: a seekable stream takes the rewind path, a
    /// forward-only one has to be copied. Shows how much of the decode's total the header read is.
    /// </summary>
    [Test]
    public void MeasureFullDecode()
    {
        byte[] encoded = encodedBackground();
        var target = ImageLoadOptions.FillTarget(new Vector2(400, 300));

        TestContext.Out.WriteLine($"encoded file: {encoded.Length:N0} bytes, {iterations} decodes to a 400x300 fill\n");

        foreach (int concurrency in new[] { 1, 8 })
        {
            TestContext.Out.WriteLine($"-- full decode, {concurrency} at a time --");

            report("seekable", concurrency, () =>
            {
                using var stream = new MemoryStream(encoded, 0, encoded.Length, writable: false);
                new ImageSharpImageLoader().Load(stream, target).Dispose();
            });

            report("forward-only", concurrency, () =>
            {
                using var stream = new ForwardOnlyStream(new MemoryStream(encoded, 0, encoded.Length, writable: false));
                new ImageSharpImageLoader().Load(stream, target).Dispose();
            });

            TestContext.Out.WriteLine(string.Empty);
        }
    }

    /// <summary>
    /// Which <em>thread</em> rents and which returns, holding everything else constant.
    /// <para>
    /// <see cref="ArrayPool{T}.Shared"/> is <c>TlsOverPerCoreLockedStacksArrayPool</c>: its fast path is a
    /// <c>[ThreadStatic]</c> cache holding <b>one array per bucket per thread</b>, backed by per-core stacks.
    /// The decode path rents on whichever pool thread <c>LoadComponentAsync</c> happened to use and returns on
    /// the draw thread once the upload has run — so the renting thread's fast path is cold every time and the
    /// returning thread's is the only one being filled. Neither <c>MaxConcurrentDecodes</c> nor
    /// <c>MaxOutstandingUploads</c> changes that, which is the suspected reason an in-app profile showed
    /// ~149 MB of <c>ImageRawData.Rent</c> served by fresh allocation under both.
    /// </para>
    /// <para>
    /// Counts distinct array instances rather than bytes, so thread and task overhead cannot flatter or
    /// distort the result: perfect reuse is 1, a total miss is one per iteration.
    /// </para>
    /// </summary>
    [Test]
    public void MeasureRentAndReturnThreadAffinity()
    {
        const int size = 400 * 300 * 4;
        const int cycles = 40;

        TestContext.Out.WriteLine($"{cycles} rent/return cycles of {size:N0} bytes from ArrayPool<byte>.Shared\n");

        reportAffinity("rent and return on one thread", size, cycles, dedicatedRenter: true, dedicatedReturner: true, sameThread: true);
        reportAffinity("one renter, one returner", size, cycles, dedicatedRenter: true, dedicatedReturner: true, sameThread: false);
        reportAffinity("pool threads rent, one returns", size, cycles, dedicatedRenter: false, dedicatedReturner: true, sameThread: false);
        reportAffinity("pool threads rent and return", size, cycles, dedicatedRenter: false, dedicatedReturner: false, sameThread: false);

        // The candidate fix. ArrayPool<T>.Create returns a ConfigurableArrayPool: a per-bucket lock over a
        // shared array of buffers, with no [ThreadStatic] layer at all, so which thread rents and which
        // returns stops mattering. Same worst-case shape as the row above it.
        TestContext.Out.WriteLine(string.Empty);

        var configurable = ArrayPool<byte>.Create(maxArrayLength: 64 * 1024 * 1024, maxArraysPerBucket: 4);

        reportAffinity("ArrayPool.Create, pool threads rent, one returns", size, cycles,
            dedicatedRenter: false, dedicatedReturner: true, sameThread: false, pool: configurable);
    }

    private static void reportAffinity(string label, int size, int cycles, bool dedicatedRenter, bool dedicatedReturner, bool sameThread, ArrayPool<byte>? pool = null)
    {
        var shared = pool ?? ArrayPool<byte>.Shared;

        // Reference equality, so this counts distinct array instances the pool handed out.
        var seen = new HashSet<byte[]>();

        var returner = new BlockingCollection<byte[]>();

        var returnThread = new Thread(() =>
        {
            foreach (byte[] array in returner.GetConsumingEnumerable())
                shared.Return(array);
        })
        { IsBackground = true };

        if (dedicatedReturner && !sameThread)
            returnThread.Start();

        for (int i = 0; i < cycles; i++)
        {
            byte[] rented;

            if (dedicatedRenter)
                rented = shared.Rent(size);
            else
                rented = Task.Run(() => shared.Rent(size)).GetAwaiter().GetResult();

            lock (seen)
                seen.Add(rented);

            if (sameThread)
                shared.Return(rented);
            else if (dedicatedReturner)
                returner.Add(rented);
            else
                Task.Run(() => shared.Return(rented)).GetAwaiter().GetResult();
        }

        returner.CompleteAdding();

        if (dedicatedReturner && !sameThread)
            returnThread.Join(TimeSpan.FromSeconds(10));

        TestContext.Out.WriteLine($"{label,-48} {seen.Count,3} distinct array(s) for {cycles} rentals");
    }

    private static int identifyAndRewind(byte[] encoded)
    {
        using var stream = new MemoryStream(encoded, 0, encoded.Length, writable: false);

        long origin = stream.Position;
        var info = Image.Identify(stream);
        stream.Position = origin;

        return info.Width;
    }

    private static int rentFirst(byte[] encoded)
    {
        // the removed path: rent a buffer the size of the whole encoded file, fill it, and read it twice.
        byte[] rented = ArrayPool<byte>.Shared.Rent(encoded.Length);

        try
        {
            using (var source = new MemoryStream(encoded, 0, encoded.Length, writable: false))
            {
                int read = 0;

                while (read < encoded.Length)
                {
                    int count = source.Read(rented, read, encoded.Length - read);
                    if (count <= 0)
                        break;

                    read += count;
                }
            }

            var info = Image.Identify(rented.AsSpan(0, encoded.Length));

            using var decodable = new MemoryStream(rented, 0, encoded.Length, writable: false);
            return info.Width;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static int copyFirst(byte[] encoded)
    {
        using var source = new ForwardOnlyStream(new MemoryStream(encoded, 0, encoded.Length, writable: false));
        using var ms = new MemoryStream();

        source.CopyTo(ms);

        var info = Image.Identify(ms.GetBuffer().AsSpan(0, (int)ms.Length));

        using var decodable = new MemoryStream(ms.GetBuffer(), 0, (int)ms.Length, writable: false);
        return info.Width;
    }

    private static void report(string label, int concurrency, Action operation)
    {
        for (int i = 0; i < warmup; i++)
            operation();

        collect();

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long lohBefore = GC.GetGCMemoryInfo().GenerationInfo[3].SizeAfterBytes;

        var clock = Stopwatch.StartNew();

        if (concurrency == 1)
        {
            for (int i = 0; i < iterations; i++)
                operation();
        }
        else
        {
            Parallel.For(0, iterations, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, _ => operation());
        }

        clock.Stop();

        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        collect();

        long loh = GC.GetGCMemoryInfo().GenerationInfo[3].SizeAfterBytes - lohBefore;

        TestContext.Out.WriteLine(
            $"{label,-26} {allocated / (double)iterations / 1024,10:N1} KiB each   "
            + $"live LOH delta {loh / (1024.0 * 1024.0),6:N2} MB   "
            + $"{clock.Elapsed.TotalMilliseconds,6:N0} ms total");
    }

    private static void collect()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
    }

    /// <summary>
    /// Stands in for an embedded resource or an archive entry: readable once, no length, no seeking.
    /// </summary>
    private class ForwardOnlyStream : Stream
    {
        private readonly Stream inner;

        public ForwardOnlyStream(Stream inner)
        {
            this.inner = inner;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
