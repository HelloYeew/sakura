// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Tests.Text;

/// <summary>
/// Font data lives in unmanaged memory, not on the managed heap.
/// </summary>
[TestFixture]
public class FontDataResidencyTest
{
    private const string font_file = "Comfortaa-Regular.ttf";

    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;
    private Storage fonts = null!;

    private static long fontBytes => NativeMemoryTracker.BytesFor(NativeMemoryCategory.Fonts);

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        store = new RendererFontStore(new HeadlessRenderer(textureManager));

        fonts = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources")
            .GetStorageForDirectory("Fonts");
    }

    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    /// <summary>
    /// The size of the font file on its own, for comparing against what the tracker reports.
    /// </summary>
    private long fontFileLength()
    {
        using var stream = fonts.GetStream(font_file)!;
        return stream.Length;
    }

    /// <summary>
    /// Forces the store's <see cref="System.Lazy{T}"/> to materialise, since loading is deferred until a
    /// font is first asked for.
    /// </summary>
    private Font load(string alias)
    {
        store.AddFont(fonts, font_file, alias);
        return store.Get(FontUsage.Default.With(family: alias));
    }

    [Test]
    public void ALoadedFontIsAccountedAsUnmanagedFontMemory()
    {
        long before = fontBytes;

        load("Residency");

        Assert.That(fontBytes - before, Is.EqualTo(fontFileLength()), "every byte of the face, and no managed copy of it");
    }

    /// <summary>
    /// The acceptance criterion, in the portable form: nothing font-sized reaches the large object heap.
    /// The bundled test font is 111 KB, comfortably over the 85 KB LOH threshold, so a managed copy of it
    /// would land there and show up here.
    /// </summary>
    [Test]
    public void LoadingAFontPutsNothingOnTheLargeObjectHeap()
    {
        long expected = fontFileLength();

        long before = liveLargeObjectHeapBytes();

        load("NoLoh");

        Assert.That(liveLargeObjectHeapBytes() - before, Is.LessThan(expected), "a pinned managed copy of the face would show up here");
    }

    /// <summary>
    /// Live large-object-heap size, with the heap settled first so the figure reflects what is retained
    /// rather than what happens to be uncollected.
    /// </summary>
    private static long liveLargeObjectHeapBytes()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        // GenerationInfo index 3 is the large object heap.
        return GC.GetGCMemoryInfo().GenerationInfo[3].SizeAfterBytes;
    }

    /// <summary>
    /// The statistic exists because the absence of any number for this is why a ~192 MB platform emoji
    /// font went unnoticed at startup.
    /// </summary>
    [Test]
    public void TheFontsCategoryIsPublishedAsAStatistic()
    {
        load("Published");

        Assert.That(GlobalStatistics.Get<long>("Native Memory", nameof(NativeMemoryCategory.Fonts)).Value,
            Is.EqualTo(fontBytes));
    }

    [Test]
    public void DisposingTheStoreReleasesTheFontData()
    {
        long before = fontBytes;

        load("Released");
        Assert.That(fontBytes, Is.GreaterThan(before));

        store.Dispose();

        Assert.That(fontBytes, Is.EqualTo(before), "the block is freed, not merely unpinned");
    }

    /// <summary>
    /// A font registered under more than one key appears more than once in the store's cache values, so
    /// the store's own teardown reaches it twice. <c>FT_Done_Face</c> on an already-destroyed face is a
    /// double free rather than a no-op, and a second release of the byte block would be one too.
    /// </summary>
    [Test]
    public void DisposingAFontTwiceReleasesItOnce()
    {
        long before = fontBytes;

        var font = load("Doubled");
        font.Dispose();

        Assert.That(fontBytes, Is.EqualTo(before));

        Assert.DoesNotThrow(() => font.Dispose(), "the second dispose must be a no-op, not a double free");
        Assert.That(fontBytes, Is.EqualTo(before), "and must not subtract the block's size a second time");
    }

    #region Mapped fonts

    /// <summary>
    /// Copies the bundled font out to a real file, since mapping needs one and the test resources are
    /// embedded. Returns its path.
    /// </summary>
    private string materialiseFontOnDisk()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"mapped-{TestContext.CurrentContext.Test.Name}.ttf");

        using (var source = fonts.GetStream(font_file)!)
        using (var destination = File.Create(path))
            source.CopyTo(destination);

        return path;
    }

    /// <summary>
    /// The whole point: a mapped face shapes text correctly. FreeType and HarfBuzz read the tables straight
    /// out of the page cache, so if the pointer or the length were wrong this is where it would show.
    /// </summary>
    [Test]
    public void AMappedFontShapesText()
    {
        string path = materialiseFontOnDisk();

        try
        {
            store.AddFontFromFile(path, alias: "Mapped");

            var shaped = store.Shape(FontUsage.Default.With(family: "Mapped"), "hello", 1f);

            Assert.Multiple(() =>
            {
                Assert.That(shaped.Glyphs, Has.Count.EqualTo(5), "five glyphs from a mapped face");
                Assert.That(shaped.Glyphs[0].Texture, Is.Not.Null, "and they rasterised");
            });
        }
        finally
        {
            store.Dispose();
            File.Delete(path);
        }
    }

    /// <summary>
    /// A mapped face is counted separately from allocated font memory, because file-backed pages are a
    /// ceiling the OS may never fault in rather than memory the process has committed — putting them in
    /// <c>Native Memory -> Total</c> would overstate a figure that is read against the process footprint.
    /// </summary>
    [Test]
    public void AMappedFontIsCountedAsMappedRatherThanAllocated()
    {
        string path = materialiseFontOnDisk();

        try
        {
            long allocatedBefore = fontBytes;
            long mappedBefore = NativeFileMapping.MappedBytes;

            store.AddFontFromFile(path, alias: "MappedCount");
            store.Get(FontUsage.Default.With(family: "MappedCount"));

            Assert.Multiple(() =>
            {
                Assert.That(NativeFileMapping.MappedBytes - mappedBefore, Is.EqualTo(new FileInfo(path).Length), "the whole face, as a mapping");
                Assert.That(fontBytes, Is.EqualTo(allocatedBefore), "and nothing copied into unmanaged memory");
            });

            store.Dispose();

            Assert.That(NativeFileMapping.MappedBytes, Is.EqualTo(mappedBefore), "unmapped on dispose");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// An embedded resource has no filesystem path, so it still has to be copied — and must still work.
    /// This is the routing, not a fallback for a failure.
    /// </summary>
    [Test]
    public void AnEmbeddedFontStillLoadsByCopy()
    {
        long mappedBefore = NativeFileMapping.MappedBytes;

        load("Embedded");

        Assert.Multiple(() =>
        {
            Assert.That(fontBytes, Is.GreaterThan(0), "copied into unmanaged memory");
            Assert.That(NativeFileMapping.MappedBytes, Is.EqualTo(mappedBefore), "an embedded resource cannot be mapped");
        });
    }

    #endregion

    /// <summary>
    /// The store reads fonts through a stream, and a stream that cannot report its length takes
    /// <see cref="NativeMemoryBuffer"/>'s grow-by-doubling path. That path is the one the old code handled
    /// with <c>CopyTo(new MemoryStream())</c> plus <c>ToArray()</c>, which cost a font about 2.7x its own
    /// size in transient large-object-heap traffic — inside a layout pass, not at startup.
    /// </summary>
    [Test]
    public void AFontFromANonSeekableStreamStillLoadsExactly()
    {
        long expected = fontFileLength();

        using var source = fonts.GetStream(font_file)!;
        using var unseekable = new UnseekableStream(source);

        var buffer = NativeMemoryBuffer.CreateFrom(unseekable, NativeMemoryCategory.Fonts);

        Assert.That(buffer, Is.Not.Null);
        Assert.That(buffer!.Length, Is.EqualTo(expected), "the growing path must land on the exact byte count");

        buffer.Dispose();
    }

    /// <summary>
    /// A stream that refuses to seek or report a length, which is what a compressed or network font source
    /// looks like.
    /// </summary>
    private class UnseekableStream : Stream
    {
        private readonly Stream inner;

        public UnseekableStream(Stream inner)
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
