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
