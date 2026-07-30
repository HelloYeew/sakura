// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Sakura.Framework.IO;

namespace Sakura.Framework.Tests.IO;

[TestFixture]
public class NativeMemoryBufferTest
{
    [Test]
    public void CreateFrom_SeekableStream_CopiesEveryByte()
    {
        byte[] payload = randomBytes(200_000);

        using (var buffer = NativeMemoryBuffer.CreateFrom(new MemoryStream(payload))!)
        {
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(payload.Length));
            Assert.That(read(buffer), Is.EqualTo(payload));
        }
    }

    /// <summary>
    /// The growing path, taken for compressed or network sources that cannot report a length. It has
    /// to arrive at exactly the same bytes as the known-length path, including the final shrink.
    /// </summary>
    [Test]
    public void CreateFrom_NonSeekableStream_CopiesEveryByte()
    {
        byte[] payload = randomBytes(200_000);

        using (var buffer = NativeMemoryBuffer.CreateFrom(new NonSeekableStream(payload))!)
        {
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.Length, Is.EqualTo(payload.Length));
            Assert.That(read(buffer), Is.EqualTo(payload));
        }
    }

    [Test]
    public void CreateFrom_ReadsFromCurrentPositionOnly()
    {
        byte[] payload = { 1, 2, 3, 4, 5, 6 };
        var stream = new MemoryStream(payload) { Position = 2 };

        using (var buffer = NativeMemoryBuffer.CreateFrom(stream)!)
            Assert.That(read(buffer), Is.EqualTo(new byte[] { 3, 4, 5, 6 }));
    }

    [Test]
    public void CreateFrom_EmptyStream_ReturnsNull()
    {
        Assert.That(NativeMemoryBuffer.CreateFrom(new MemoryStream([])), Is.Null);
        Assert.That(NativeMemoryBuffer.CreateFrom(new NonSeekableStream([])), Is.Null);
    }

    [Test]
    public void CreateFromFile_CopiesFileContents()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        byte[] payload = randomBytes(70_000);

        try
        {
            File.WriteAllBytes(path, payload);

            using (var buffer = NativeMemoryBuffer.CreateFromFile(path)!)
                Assert.That(read(buffer), Is.EqualTo(payload));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Dispose_ReleasesTheBlock()
    {
        var buffer = NativeMemoryBuffer.CreateFrom(new MemoryStream([1, 2, 3]))!;

        Assert.That(buffer.IsFreed, Is.False);

        buffer.Dispose();

        Assert.That(buffer.IsFreed, Is.True);
        Assert.That(buffer.Pointer, Is.EqualTo(IntPtr.Zero));
    }

    /// <summary>
    /// The case the reference counting exists for: an audio store evicting and disposing a track
    /// while a channel created from it is still decoding out of the same block.
    /// </summary>
    [Test]
    public void Dispose_WhileConsumerHoldsReference_KeepsBlockAlive()
    {
        byte[] payload = { 7, 8, 9 };
        var buffer = NativeMemoryBuffer.CreateFrom(new MemoryStream(payload))!;

        Assert.That(buffer.AddReference(), Is.True);

        buffer.Dispose();

        Assert.That(buffer.IsFreed, Is.False);
        Assert.That(buffer.Pointer, Is.Not.EqualTo(IntPtr.Zero));
        Assert.That(read(buffer), Is.EqualTo(payload));

        Assert.That(buffer.Release(), Is.True);
        Assert.That(buffer.IsFreed, Is.True);
    }

    [Test]
    public void Release_OnlyFreesOnTheLastReference()
    {
        var buffer = NativeMemoryBuffer.CreateFrom(new MemoryStream([1]))!;

        buffer.AddReference();
        buffer.AddReference();

        Assert.That(buffer.Release(), Is.False);
        Assert.That(buffer.Release(), Is.False);
        Assert.That(buffer.Release(), Is.True);
    }

    [Test]
    public void AddReference_AfterFree_Fails()
    {
        var buffer = NativeMemoryBuffer.CreateFrom(new MemoryStream([1]))!;
        buffer.Dispose();

        Assert.That(buffer.AddReference(), Is.False);
    }

    [Test]
    public void Release_MoreTimesThanReferenced_DoesNotFreeTwice()
    {
        var buffer = NativeMemoryBuffer.CreateFrom(new MemoryStream([1]))!;

        Assert.That(buffer.Release(), Is.True);

        // A double free of unmanaged memory would corrupt the heap rather than throw, so the only
        // thing to assert is that the second release declines to do anything.
        Assert.That(buffer.Release(), Is.False);
        Assert.That(buffer.Release(), Is.False);
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        var buffer = NativeMemoryBuffer.CreateFrom(new MemoryStream([1]))!;

        buffer.Dispose();
        buffer.Dispose();

        Assert.That(buffer.IsFreed, Is.True);
    }

    private static byte[] randomBytes(int count)
    {
        // Deterministic so a failure is reproducible.
        var random = new Random(4242);
        byte[] bytes = new byte[count];
        random.NextBytes(bytes);
        return bytes;
    }

    private static byte[] read(NativeMemoryBuffer buffer)
    {
        byte[] copy = new byte[buffer.Length];
        Marshal.Copy(buffer.Pointer, copy, 0, copy.Length);
        return copy;
    }

    /// <summary>
    /// A stream that can be read but reports neither length nor position, forcing the growing path.
    /// Hands back small chunks so the copy loop is exercised across several reads.
    /// </summary>
    private class NonSeekableStream : Stream
    {
        private readonly byte[] data;
        private int position;

        public NonSeekableStream(byte[] data)
        {
            this.data = data;
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            int toRead = Math.Min(Math.Min(count, 7919), data.Length - position);

            if (toRead <= 0)
                return 0;

            Array.Copy(data, position, buffer, offset, toRead);
            position += toRead;
            return toRead;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
