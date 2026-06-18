// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using System.Text;
using NUnit.Framework;
using Sakura.Framework.IO;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.IO;

[TestFixture]
public class DiskCacheTest
{
    private string tempDir = null!;
    private NativeStorage storage = null!;
    private DiskCache cache = null!;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "sakura-diskcache-test", Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        storage = new NativeStorage(tempDir);
        cache = new DiskCache(storage);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [Test]
    public void TryRead_MissingKey_ReturnsFalse()
    {
        Assert.That(cache.TryRead("does-not-exist", out byte[] data), Is.False);
        Assert.That(data, Is.Null);
    }

    [Test]
    public void WriteThenRead_RoundTripsBytes()
    {
        byte[] payload = { 1, 2, 3, 4, 250, 0, 99 };

        cache.Write("key1", payload);

        Assert.That(cache.TryRead("key1", out byte[] read), Is.True);
        Assert.That(read, Is.EqualTo(payload));
    }

    [Test]
    public void Write_OverwritesExistingEntryAtomically()
    {
        cache.Write("key1", new byte[] { 1, 2, 3 });
        cache.Write("key1", new byte[] { 9, 8 });

        Assert.That(cache.TryRead("key1", out byte[] read), Is.True);
        Assert.That(read, Is.EqualTo(new byte[] { 9, 8 }));
    }

    [Test]
    public void WriteThenRead_EmptyPayload_RoundTrips()
    {
        cache.Write("empty", System.Array.Empty<byte>());

        Assert.That(cache.TryRead("empty", out byte[] read), Is.True);
        Assert.That(read, Is.Empty);
    }

    [Test]
    public void CorruptedPayload_IsTreatedAsMiss()
    {
        cache.Write("key1", new byte[] { 1, 2, 3, 4 });

        // Corrupt the stored file so the payload no longer matches its checksum.
        string full = storage.GetFullPath("key1");
        byte[] bytes = File.ReadAllBytes(full);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(full, bytes);

        Assert.That(cache.TryRead("key1", out byte[] read), Is.False);
        Assert.That(read, Is.Null);
    }

    [Test]
    public void TruncatedFile_IsTreatedAsMiss()
    {
        cache.Write("key1", new byte[] { 1, 2, 3, 4, 5, 6 });

        string full = storage.GetFullPath("key1");
        byte[] bytes = File.ReadAllBytes(full);
        File.WriteAllBytes(full, bytes[..(bytes.Length / 2)]);

        Assert.That(cache.TryRead("key1", out _), Is.False);
    }

    [Test]
    public void StringPair_RoundTrips()
    {
        cache.WriteStrings("pair", "hello", "world");

        Assert.That(cache.TryReadStrings("pair", out string a, out string b), Is.True);
        Assert.That(a, Is.EqualTo("hello"));
        Assert.That(b, Is.EqualTo("world"));
    }

    [Test]
    public void StringPair_HandlesUnicodeAndEmpty()
    {
        cache.WriteStrings("pair", "main0 → MSL", string.Empty);

        Assert.That(cache.TryReadStrings("pair", out string a, out string b), Is.True);
        Assert.That(a, Is.EqualTo("main0 → MSL"));
        Assert.That(b, Is.Empty);
    }

    [Test]
    public void TryReadStrings_MissingKey_ReturnsFalse()
    {
        Assert.That(cache.TryReadStrings("nope", out string a, out string b), Is.False);
        Assert.That(a, Is.Null);
        Assert.That(b, Is.Null);
    }

    [Test]
    public void HashString_IsStableAndContentSensitive()
    {
        string h1 = DiskCache.HashString("abc");
        string h2 = DiskCache.HashString("abc");
        string h3 = DiskCache.HashString("abd");

        Assert.That(h1, Is.EqualTo(h2));
        Assert.That(h1, Is.Not.EqualTo(h3));
        Assert.That(h1, Has.Length.EqualTo(32)); // 16-byte MD5 as hex
    }

    [Test]
    public void HashBytes_MatchesEquivalentStringContent()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("payload");
        Assert.That(DiskCache.HashBytes(bytes), Is.EqualTo(DiskCache.HashString("payload")));
    }

    [Test]
    public void DifferentKeys_DoNotCollide()
    {
        cache.Write("a", new byte[] { 1 });
        cache.Write("b", new byte[] { 2 });

        cache.TryRead("a", out byte[] ra);
        cache.TryRead("b", out byte[] rb);

        Assert.That(ra, Is.EqualTo(new byte[] { 1 }));
        Assert.That(rb, Is.EqualTo(new byte[] { 2 }));
    }
}
