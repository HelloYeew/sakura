// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.Headless;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Audio;

[TestFixture]
public class AudioStoreTest
{
    private string tempDir = null!;
    private NativeStorage storage = null!;
    private HeadlessAudioManager audioManager = null!;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "sakura-audiostore-test", Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        storage = new NativeStorage(tempDir);
        audioManager = new HeadlessAudioManager();
    }

    [TearDown]
    public void TearDown()
    {
        audioManager.Dispose();

        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    private void writeFile(string name) => File.WriteAllBytes(Path.Combine(tempDir, name), new byte[] { 1, 2, 3, 4 });

    /// <summary>
    /// A filesystem-backed store should hand the native audio library a path and let it read the
    /// file itself, rather than buffering the whole encoded file into memory for the component's
    /// lifetime.
    /// </summary>
    [Test]
    public void Get_FileBackedStorage_LoadsFromPath()
    {
        writeFile("a.mp3");

        var store = new TestStore(storage, audioManager);
        var component = store.Get("a.mp3");

        Assert.That(component, Is.Not.Null);
        Assert.That(component!.LoadedFromPath, Is.Not.Null);
        Assert.That(File.Exists(component.LoadedFromPath!), Is.True);
        Assert.That(store.StreamLoads, Is.Zero);
    }

    /// <summary>
    /// Storage that is not on the filesystem (embedded resources) has no path to hand over, so the
    /// stream path must still work.
    /// </summary>
    [Test]
    public void Get_NonFilesystemStorage_FallsBackToStream()
    {
        var embedded = new EmbeddedResourceStorage(typeof(AudioStoreTest).Assembly, "Sakura.Framework.Tests.Resources");
        var store = new TestStore(embedded, audioManager);
        var component = store.Get("Tracks/test.mp3");

        Assert.That(component, Is.Not.Null);
        Assert.That(component!.LoadedFromPath, Is.Null);
        Assert.That(store.StreamLoads, Is.EqualTo(1));
    }

    /// <summary>
    /// A store that declines the path overload (the default) keeps using streams even on disk.
    /// </summary>
    [Test]
    public void Get_StoreWithoutPathSupport_UsesStream()
    {
        writeFile("a.mp3");

        var store = new TestStore(storage, audioManager) { SupportsPathLoading = false };

        Assert.That(store.Get("a.mp3"), Is.Not.Null);
        Assert.That(store.StreamLoads, Is.EqualTo(1));
    }

    [Test]
    public void Get_SameName_ReturnsCachedInstance()
    {
        writeFile("a.mp3");

        var store = new TestStore(storage, audioManager);

        var first = store.Get("a.mp3");

        Assert.That(store.Get("a.mp3"), Is.SameAs(first));
        Assert.That(store.Created, Has.Count.EqualTo(1));
    }

    [Test]
    public void Get_MissingFile_ReturnsNull()
    {
        Assert.That(new TestStore(storage, audioManager).Get("nope.mp3"), Is.Null);
    }

    [Test]
    public void Eviction_DisposesLeastRecentlyUsed()
    {
        var store = new TestStore(storage, audioManager) { CacheLimit = 2 };

        var first = load(store, "a.mp3");
        var second = load(store, "b.mp3");
        load(store, "c.mp3");

        Assert.That(first.IsDisposed, Is.True);
        Assert.That(second.IsDisposed, Is.False);
    }

    /// <summary>
    /// The use-after-free this guards against: the currently playing track is precisely the one
    /// nothing has asked the store for lately, so LRU order picks it first. Disposing it would free
    /// decoder state (and, in the BASS backend, the memory block) underneath a live channel.
    /// </summary>
    [Test]
    public void Eviction_SkipsComponentsWithActiveChannels()
    {
        var store = new TestStore(storage, audioManager) { CacheLimit = 2 };

        var playing = load(store, "a.mp3");
        playing.ActiveChannels = 1;

        var second = load(store, "b.mp3");
        load(store, "c.mp3");

        Assert.That(playing.IsDisposed, Is.False, "a component reporting live channels must never be evicted");
        Assert.That(second.IsDisposed, Is.True, "the next evictable entry should have been taken instead");
    }

    /// <summary>
    /// When everything left is in use, the cache is allowed to sit over its cap rather than corrupt
    /// playback. It must come back down once the components are idle again.
    /// </summary>
    [Test]
    public void Eviction_AllInUse_LeavesCacheOverCapacityThenRecovers()
    {
        var store = new TestStore(storage, audioManager) { CacheLimit = 1, ComponentsStartInUse = true };

        var first = load(store, "a.mp3");
        var second = load(store, "b.mp3");

        Assert.That(first.IsDisposed, Is.False);
        Assert.That(second.IsDisposed, Is.False, "nothing evictable means the cache sits over its cap");

        first.ActiveChannels = 0;
        second.ActiveChannels = 0;

        load(store, "c.mp3");

        Assert.That(first.IsDisposed, Is.True, "the cache should come back down once components go idle");
        Assert.That(second.IsDisposed, Is.True);
    }

    /// <summary>
    /// With everything cached in use, the entry that just got created is the only evictable one —
    /// and evicting it would hand the caller a disposed component.
    /// </summary>
    [Test]
    public void Eviction_NeverEvictsTheEntryItWasTriggeredBy()
    {
        var store = new TestStore(storage, audioManager) { CacheLimit = 1, ComponentsStartInUse = true };

        load(store, "a.mp3");
        var justAdded = load(store, "b.mp3");

        Assert.That(justAdded.IsDisposed, Is.False);
        Assert.That(store.Get("b.mp3"), Is.SameAs(justAdded));
    }

    [Test]
    public void Dispose_DisposesEveryCachedComponent()
    {
        var store = new TestStore(storage, audioManager);

        var first = load(store, "a.mp3");
        var second = load(store, "b.mp3");

        store.Dispose();

        Assert.That(first.IsDisposed, Is.True);
        Assert.That(second.IsDisposed, Is.True);
    }

    private TestComponent load(TestStore store, string name)
    {
        writeFile(name);
        var component = store.Get(name);
        Assert.That(component, Is.Not.Null);
        return component!;
    }

    private class TestComponent : IHasActiveChannels, IDisposable
    {
        public string? LoadedFromPath { get; init; }
        public int ActiveChannels { get; set; }
        public bool IsDisposed { get; private set; }

        public bool HasActiveChannels => ActiveChannels > 0;

        public void Dispose() => IsDisposed = true;
    }

    private class TestStore : AudioStore<TestComponent>
    {
        public int CacheLimit { get; init; } = int.MaxValue;
        public bool SupportsPathLoading { get; init; } = true;

        /// <summary>
        /// Marks components as in use from the moment they are created, so eviction sees them as
        /// busy on the very pass that their own creation triggers.
        /// </summary>
        public bool ComponentsStartInUse { get; init; }

        public int StreamLoads { get; private set; }

        public readonly List<TestComponent> Created = new List<TestComponent>();

        public TestStore(Storage storage, IAudioManager audioManager)
            : base(storage, audioManager)
        {
        }

        protected override int MaxCachedComponents => CacheLimit;

        protected override TestComponent CreateComponent(Stream stream)
        {
            // Prove the stream is actually usable, since the point of the path overload is to avoid
            // needing it at all.
            Assert.That(stream.ReadByte(), Is.Not.EqualTo(-1));

            StreamLoads++;
            var component = new TestComponent { ActiveChannels = ComponentsStartInUse ? 1 : 0 };
            Created.Add(component);
            return component;
        }

        protected override TestComponent? CreateComponent(string filePath)
        {
            if (!SupportsPathLoading)
                return null;

            var component = new TestComponent { LoadedFromPath = filePath, ActiveChannels = ComponentsStartInUse ? 1 : 0 };
            Created.Add(component);
            return component;
        }
    }
}
