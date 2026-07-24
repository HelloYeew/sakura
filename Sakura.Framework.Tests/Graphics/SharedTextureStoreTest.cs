// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Test for <see cref="SharedTextureStore"/>
/// </summary>
[TestFixture]
public class SharedTextureStoreTest
{
    private static Texture dummy() => new Texture(1, 1);

    [Test]
    public void CreatesOnceThenReuses()
    {
        var store = new SharedTextureStore();
        int creates = 0;

        var a = store.AddOrAcquire("k", () => { creates++; return dummy(); });
        bool hit = store.TryAcquire("k", out var b);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(creates, Is.EqualTo(1));
            Assert.That(hit, Is.True);
            Assert.That(b, Is.SameAs(a));
            Assert.That(store.Count, Is.EqualTo(1));
        }
    }

    [Test]
    public void DisposesOnlyWhenLastReferenceReleased()
    {
        var store = new SharedTextureStore();
        var tex = store.AddOrAcquire("k", dummy); // count 1
        store.TryAcquire("k", out _); // count 2

        int disposed = 0;
        void release() => store.Release("k", _ => disposed++);

        release(); // count 1
        using (Assert.EnterMultipleScope())
        {
            Assert.That(disposed, Is.Zero, "still referenced");
            Assert.That(store.Count, Is.EqualTo(1));
        }

        release(); // count 0
        using (Assert.EnterMultipleScope())
        {
            Assert.That(disposed, Is.EqualTo(1), "disposed once at zero");
            Assert.That(store.Count, Is.Zero);
        }
    }

    [Test]
    public void ReAcquireAfterReleaseRecreates()
    {
        var store = new SharedTextureStore();
        int creates = 0;

        store.AddOrAcquire("k", () =>
        {
            creates++;
            return dummy();
        });
        store.Release("k", _ => { });
        store.AddOrAcquire("k", () =>
        {
            creates++;
            return dummy();
        });

        Assert.That(creates, Is.EqualTo(2));
    }

    [Test]
    public void ReleaseUnknownKeyIsNoOp()
    {
        var store = new SharedTextureStore();
        int disposed = 0;

        Assert.DoesNotThrow(() => store.Release("missing", _ => disposed++));
        Assert.That(disposed, Is.Zero);
    }
}
