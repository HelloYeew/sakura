// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Logging;

/// <summary>
/// Tests for <see cref="Logger"/>
/// </summary>
[TestFixture]
public class LoggerTest
{
    private string tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"sakura-logger-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        Logger.Storage = new NativeStorage(tempDirectory);
        Logger.Initialize(LogLevel.Debug, logToConsole: false);
    }

    [TearDown]
    public void TearDown()
    {
        Logger.Shutdown();

        try
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
        catch (IOException)
        {
            // A lingering file handle must not fail the run.
        }
    }

    private string readLogs()
    {
        var files = new DirectoryInfo(tempDirectory).GetFiles("*.log", SearchOption.AllDirectories);

        return string.Join("\n", files.Select(f =>
        {
            using var stream = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }));
    }

    [Test]
    public void QueuedMessagesSurviveShutdown()
    {
        Logger.Log("first message");
        Logger.Log("second message");

        // Shutdown cancels the writer's wait. Anything already queued must still be flushed rather than
        // discarded along with the cancellation.
        Logger.Shutdown();

        string contents = readLogs();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contents, Does.Contain("first message"));
            Assert.That(contents, Does.Contain("second message"));
        }
    }

    [Test]
    public void ShutdownTerminatesPromptlyWhileIdle()
    {
        // The writer is parked on WaitToReadAsync here. If cancellation didn't break it out (or spun),
        // Shutdown would hang — so this is the regression test for the loop rewrite itself.
        Assert.That(() => Logger.Shutdown(), Throws.Nothing);
    }

    [Test]
    public void ABurstIsDeliveredInOrder()
    {
        for (int i = 0; i < 200; i++)
            Logger.Log($"burst {i}");

        Logger.Shutdown();

        string contents = readLogs();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contents, Does.Contain("burst 0"));
            Assert.That(contents, Does.Contain("burst 199"));
            Assert.That(contents.IndexOf("burst 0", StringComparison.Ordinal),
                Is.LessThan(contents.IndexOf("burst 199", StringComparison.Ordinal)),
                "the channel must preserve write order");
        }
    }

    [Test]
    public void MultiLineMessagesStillSplitIntoLines()
    {
        // The single-line fast path must not change behaviour for messages that do contain newlines.
        Logger.Log("line one\nline two\nline three");

        Logger.Shutdown();

        string contents = readLogs();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contents, Does.Contain("line one"));
            Assert.That(contents, Does.Contain("line two"));
            Assert.That(contents, Does.Contain("line three"));
        }
    }
}
