// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Tests for <see cref="NativeDisposalQueue"/>
/// </summary>
[TestFixture]
public class NativeDisposalQueueTest
{
    [SetUp]
    public void SetUp()
    {
        // The queue is process-wide, so drain anything another fixture left behind.
        NativeDisposalQueue.Process();
    }

    [Test]
    public void ProcessRunsQueuedRelease()
    {
        bool released = false;

        NativeDisposalQueue.Enqueue(() => released = true);

        Assert.That(NativeDisposalQueue.PendingCount, Is.EqualTo(1));
        Assert.That(NativeDisposalQueue.Process(), Is.EqualTo(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(released, Is.True);
            Assert.That(NativeDisposalQueue.PendingCount, Is.Zero);
        }
    }

    [Test]
    public void ProcessRunsEveryQueuedRelease()
    {
        int released = 0;

        for (int i = 0; i < 32; i++)
            NativeDisposalQueue.Enqueue(() => released++);

        Assert.That(NativeDisposalQueue.Process(), Is.EqualTo(32));
        Assert.That(released, Is.EqualTo(32));
    }

    [Test]
    public void AFailedReleaseDoesNotStopTheRest()
    {
        // A release runs against an already-orphaned resource, so there is nothing to retry against —
        // it must never be allowed to take down the frame or strand the rest of the queue.
        bool laterReleaseRan = false;

        NativeDisposalQueue.Enqueue(() => throw new InvalidOperationException("native release failed"));
        NativeDisposalQueue.Enqueue(() => laterReleaseRan = true);

        Assert.That(() => NativeDisposalQueue.Process(), Throws.Nothing);
        Assert.That(laterReleaseRan, Is.True);
    }

    [Test]
    public void ProcessOnEmptyQueueIsANoOp()
    {
        Assert.That(NativeDisposalQueue.Process(), Is.Zero);
    }

    [Test]
    public void NullReleaseIsIgnored()
    {
        NativeDisposalQueue.Enqueue(null);

        Assert.That(NativeDisposalQueue.PendingCount, Is.Zero);
    }

    [Test]
    public void FinalizerEnqueuesReleaseForAnUndisposedHandle()
    {
        // for MetalTexture/GLTexture: a wrapper around a native handle that was dropped
        // without being disposed. The handle must still reach the queue via finalization.
        int releasedHandle = 0;

        allocateAndAbandon(() => new HandleWrapper(0xBEEF, h => releasedHandle = h));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(NativeDisposalQueue.Process(), Is.EqualTo(1));
        Assert.That(releasedHandle, Is.EqualTo(0xBEEF), "the finalizer must hand the native handle to the queue");
    }

    [Test]
    public void DisposeReleasesImmediatelyAndSuppressesTheFinalizer()
    {
        int releaseCount = 0;

        var wrapper = new HandleWrapper(0x1234, _ => releaseCount++);
        wrapper.Dispose();

        // Released inline, not queued: Dispose is called on the draw thread where native calls are legal.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(releaseCount, Is.EqualTo(1));
            Assert.That(NativeDisposalQueue.PendingCount, Is.Zero);
        }

        // ReSharper disable once RedundantAssignment
        wrapper = null;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // The finalizer was suppressed and the handle already claimed, so nothing double-frees.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(NativeDisposalQueue.Process(), Is.Zero);
            Assert.That(releaseCount, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// Builds the object in a non-inlined scope so no stack slot keeps it alive past the call, which
    /// would make the collection below non-deterministic in a debug build.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void allocateAndAbandon(Func<HandleWrapper> create) => create();

    /// <summary>
    /// Mirrors the Dispose/finalizer contract implemented by <c>MetalTexture</c> and <c>GLTexture</c>:
    /// the handle is claimed atomically, Dispose releases inline and suppresses finalization, and the
    /// finalizer queues the release capturing only the handle (never <c>this</c>).
    /// </summary>
    private sealed class HandleWrapper : IDisposable
    {
        private int handle;
        private readonly Action<int> release;

        public HandleWrapper(int handle, Action<int> release)
        {
            this.handle = handle;
            this.release = release;
        }

        public void Dispose()
        {
            int claimed = Interlocked.Exchange(ref handle, 0);

            GC.SuppressFinalize(this);

            if (claimed != 0)
                release(claimed);
        }

        ~HandleWrapper()
        {
            int claimed = Interlocked.Exchange(ref handle, 0);
            if (claimed == 0)
                return;

            var releaseLocal = release;
            NativeDisposalQueue.Enqueue(() => releaseLocal(claimed));
        }
    }
}
