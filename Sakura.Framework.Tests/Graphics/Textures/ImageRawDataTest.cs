// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests for <see cref="ImageRawData"/>'s ownership of pooled pixel memory.
/// </summary>
/// <remarks>
/// The hazard being guarded against is a pooled array returned twice, which silently corrupts the pool
/// for the whole process and <see cref="ImageRawData"/> is a struct, so it is trivially copied.
/// </remarks>
[TestFixture]
public class ImageRawDataTest
{
    [Test]
    public void RentSizesTheBufferForTheImage()
    {
        using var raw = ImageRawData.Rent(64, 32);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.Width, Is.EqualTo(64));
            Assert.That(raw.Height, Is.EqualTo(32));
            Assert.That(raw.Data.Length, Is.EqualTo(64 * 32 * 4), "the span must be exactly the image, not the whole rental");
            Assert.That(raw.IsValid, Is.True);
        }
    }

    [Test]
    public void DisposeInvalidatesTheData()
    {
        var raw = ImageRawData.Rent(8, 8);
        raw.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.IsValid, Is.False);
            Assert.That(raw.Data.IsEmpty, Is.True);
        }
    }

    /// <summary>
    /// The reason the rental lives behind an owner object rather than in a field: a struct copy must not
    /// be able to return the same pooled array a second time.
    /// </summary>
    [Test]
    public void DisposingACopyDoesNotReturnTheRentalTwice()
    {
        var original = ImageRawData.Rent(16, 16);
        var copy = original;

        original.Dispose();
        copy.Dispose();
        copy.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(original.IsValid, Is.False);
            Assert.That(copy.IsValid, Is.False, "both views of the same rental are released together");
        }
    }

    [Test]
    public void WritableSpanRoundTripsThroughData()
    {
        using var raw = ImageRawData.Rent(2, 1);

        var writable = raw.GetWritableSpan();
        for (int i = 0; i < writable.Length; i++)
            writable[i] = (byte)(i + 1);

        Assert.That(raw.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    [Test]
    public void CopyFromCopiesTheSource()
    {
        byte[] source = { 9, 8, 7, 6, 5, 4, 3, 2 };

        using var raw = ImageRawData.CopyFrom(2, 1, source);

        Assert.That(raw.Data.ToArray(), Is.EqualTo(source));
    }

    /// <summary>
    /// A pooled array arrives dirty, so a short source must not leave another image's pixels visible in
    /// the tail.
    /// </summary>
    [Test]
    public void CopyFromZeroFillsAShortSource()
    {
        using var raw = ImageRawData.CopyFrom(2, 1, new byte[] { 1, 2, 3, 4 });

        Assert.That(raw.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 0, 0, 0, 0 }));
    }

    [Test]
    public void CopyFromIgnoresExcessSource()
    {
        using var raw = ImageRawData.CopyFrom(1, 1, new byte[] { 1, 2, 3, 4, 250, 250 });

        Assert.That(raw.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void CallerSuppliedMemoryIsNotPooledOrFreed()
    {
        byte[] mine = new byte[4];
        var raw = new ImageRawData(1, 1, mine);

        raw.Dispose();

        // The array is still the caller's to use; only the view of it went away.
        Assert.DoesNotThrow(() => mine[0] = 1);
    }

    [Test]
    public void DefaultInstanceIsSafeToUse()
    {
        ImageRawData raw = default;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw.IsValid, Is.False);
            Assert.That(raw.Data.IsEmpty, Is.True);
            Assert.That(raw.GetWritableSpan().IsEmpty, Is.True);
        }

        Assert.DoesNotThrow(() => raw.Dispose());
    }

    [Test]
    public void ConstructorRejectsNullData()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new ImageRawData(1, 1, null!));
    }
}
