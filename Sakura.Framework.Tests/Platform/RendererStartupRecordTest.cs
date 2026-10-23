// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

/// <summary>
/// A new <see cref="RendererStartupRecord"/> on the same storage stands for the next launch, so whatever the
/// first one left unsettled is what a crash would have left.
/// </summary>
[TestFixture]
public class RendererStartupRecordTest
{
    private TemporaryStorage storage = null!;

    [SetUp]
    public void SetUp() => storage = new TemporaryStorage(Path.Combine(Path.GetTempPath(), $"sakura-renderer-record-{Guid.NewGuid()}"));

    [TearDown]
    public void TearDown() => storage.Dispose();

    private RendererStartupRecord nextLaunch() => new RendererStartupRecord(storage);

    [Test]
    public void RendererThatNeverSettledHasCrashed()
    {
        nextLaunch().Starting(RendererType.Vulkan);

        Assert.That(nextLaunch().Crashed, Is.EqualTo(new[] { RendererType.Vulkan }));
    }

    [Test]
    public void StartedOrCaughtFailureIsNoCrash()
    {
        var record = nextLaunch();
        record.Starting(RendererType.Vulkan);
        record.Failed();
        record.Starting(RendererType.OpenGL);
        record.Started(RendererType.OpenGL);

        Assert.Multiple(() =>
        {
            Assert.That(nextLaunch().Crashed, Is.Empty);
            Assert.That(storage.Exists(RendererStartupRecord.FILENAME), Is.False, "nothing is left behind");
        });
    }

    [Test]
    public void CrashIsKeptWhileAnotherRendererStarts()
    {
        nextLaunch().Starting(RendererType.Vulkan);

        var record = nextLaunch();
        record.Starting(RendererType.OpenGL);
        record.Started(RendererType.OpenGL);

        Assert.That(nextLaunch().Crashed, Is.EqualTo(new[] { RendererType.Vulkan }), "otherwise every other launch would crash");
    }

    [Test]
    public void CrashedRendererThatStartsIsCleared()
    {
        nextLaunch().Starting(RendererType.Vulkan);

        var record = nextLaunch();
        record.Starting(RendererType.Vulkan);
        record.Started(RendererType.Vulkan);

        Assert.That(nextLaunch().Crashed, Is.Empty);
    }

    [Test]
    public void ForgivenRendererIsCleared()
    {
        nextLaunch().Starting(RendererType.Vulkan);

        nextLaunch().Forgive(RendererType.Vulkan);

        Assert.That(nextLaunch().Crashed, Is.Empty);
    }

    [Test]
    public void CrashedRenderersMoveLastAndStay()
    {
        nextLaunch().Starting(RendererType.Vulkan);
        var record = nextLaunch();

        var several = new List<RendererType> { RendererType.Vulkan, RendererType.Direct3D11, RendererType.OpenGL };
        record.MoveCrashedLast(several);

        var single = new List<RendererType> { RendererType.Vulkan };
        record.MoveCrashedLast(single);

        Assert.Multiple(() =>
        {
            Assert.That(several, Is.EqualTo(new[] { RendererType.Direct3D11, RendererType.OpenGL, RendererType.Vulkan }));
            Assert.That(single, Is.EqualTo(new[] { RendererType.Vulkan }), "a platform with one renderer still tries it");
        });
    }

    [Test]
    public void UnknownLinesAreIgnored()
    {
        using (var writer = new StreamWriter(storage.GetStream(RendererStartupRecord.FILENAME, FileAccess.Write, FileMode.Create)))
            writer.Write("Vulkan\nnot a renderer\n\nAutomatic\nVulkan\n");

        Assert.That(nextLaunch().Crashed, Is.EqualTo(new[] { RendererType.Vulkan }));
    }
}
