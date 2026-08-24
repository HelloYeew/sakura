// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using NUnit.Framework;
using Sakura.Framework.Configurations;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Configurations;

[TestFixture]
public class ConfigManagerTest
{
    private string tempDir = null!;
    private NativeStorage storage = null!;
    private static readonly string[] expected = new[] { "Mode = Slow", "Enabled = False", "Amount = 1" };

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "sakura-configmanager-test", Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        storage = new NativeStorage(tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    private string readFile() => File.ReadAllText(Path.Combine(tempDir, "test.ini"));

    private void writeFile(string content) => File.WriteAllText(Path.Combine(tempDir, "test.ini"), content);

    [Test]
    public void MissingFileWritesDefaults()
    {
        var manager = new TestConfigManager(storage);
        manager.Load();

        Assert.That(readFile(), Does.Contain("Mode = Fast"));
        Assert.That(readFile(), Does.Contain("Enabled = True"));
    }

    [Test]
    public void ValuesRoundTrip()
    {
        var manager = new TestConfigManager(storage);
        manager.Load();

        manager.Get(TestSetting.Mode, TestMode.Fast).Value = TestMode.Slow;
        manager.Get(TestSetting.Amount, 0.0).Value = 0.5;
        manager.Flush();

        var reloaded = new TestConfigManager(storage);
        reloaded.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.Get(TestSetting.Mode, TestMode.Fast).Value, Is.EqualTo(TestMode.Slow));
            Assert.That(reloaded.Get(TestSetting.Amount, 0.0).Value, Is.EqualTo(0.5));
        }
    }

    /// <summary>
    /// Writing a file shorter than the one already on disk used to leave the tail of the old content behind,
    /// because the write stream was opened with <see cref="FileMode.OpenOrCreate"/> rather than truncating.
    /// </summary>
    [Test]
    public void ShorterSaveTruncatesPreviousContent()
    {
        var manager = new TestConfigManager(storage);
        manager.Load();

        manager.Get(TestSetting.Amount, 0.0).Value = 123456.789;
        manager.Flush();

        Assert.That(readFile(), Does.Contain("Amount = 123456.789"));

        manager.Get(TestSetting.Amount, 0.0).Value = 1;
        manager.Flush();

        Assert.That(readFile(), Does.Not.Contain("123456"));
        Assert.That(readFile().Trim().Split('\n'), Has.Length.EqualTo(3));
    }

    [Test]
    public void CorruptFileIsIgnoredAndRewritten()
    {
        writeFile("Mode = Slow\nEnabled = False\nAmount = 1\nunt = 1\n\nt = 1\n");

        var manager = new TestConfigManager(storage);
        manager.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manager.Get(TestSetting.Mode, TestMode.Fast).Value, Is.EqualTo(TestMode.Slow));
            Assert.That(manager.Get(TestSetting.Enabled, true).Value, Is.False);

            Assert.That(readFile().Trim().Split('\n'), Is.EqualTo(expected));
        }
    }

    [Test]
    public void UnparsableValueFallsBackToDefaultAndRewrites()
    {
        writeFile("Mode = NotAMode\nEnabled = True\nAmount = 1\n");

        var manager = new TestConfigManager(storage);
        manager.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manager.Get(TestSetting.Mode, TestMode.Fast).Value, Is.EqualTo(TestMode.Fast));
            Assert.That(readFile(), Does.Contain("Mode = Fast"));
        }
    }

    /// <summary>
    /// A setting the running build doesn't know about must survive a load/save cycle rather than being silently dropped.
    /// </summary>
    [Test]
    public void SettingsRegisteredAfterLoadStillPickUpTheirStoredValue()
    {
        writeFile("Mode = Slow\nEnabled = False\nAmount = 4.25\n");

        var manager = new TestConfigManager(storage, registerDefaults: false);
        manager.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manager.Get(TestSetting.Amount, 0.0).Value, Is.EqualTo(4.25));
            Assert.That(manager.Get(TestSetting.Mode, TestMode.Fast).Value, Is.EqualTo(TestMode.Slow));
        }
    }

    [Test]
    public void MismatchedTypeThrows()
    {
        var manager = new TestConfigManager(storage);

        Assert.Throws<System.InvalidCastException>(() => manager.Get(TestSetting.Enabled, TestMode.Fast));
    }

    private enum TestMode
    {
        Fast,
        Slow
    }

    [SettingSource("test.ini")]
    private enum TestSetting
    {
        Mode,
        Enabled,
        Amount
    }

    /// <summary>
    /// A setting the file never mentioned is written back with its default, rather than staying absent
    /// until something unrelated happens to change.
    /// </summary>
    [Test]
    public void MissingSettingIsWrittenBackOnLoad()
    {
        writeFile("Mode = Slow\nAmount = 5\n");

        var manager = new TestConfigManager(storage);

        // No Flush: Load must repair the file by itself. Flushing would write the file unconditionally
        // and the assertion would hold whether the repair exists — which is exactly how the
        // first version of this test passed against the unfixed code.
        manager.Load();

        string written = readFile();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(written, Does.Contain("Enabled = True"), "The absent setting should have been written back.");
            Assert.That(written, Does.Contain("Mode = Slow"), "A value the file did have must survive the rewrite.");
            Assert.That(written, Does.Contain("Amount = 5"));
        }
    }

    /// <summary>
    /// The value written back is the default, since that is what a missing line means.
    /// </summary>
    [Test]
    public void MissingSettingComesBackAtItsDefault()
    {
        writeFile("Enabled = False\n");

        var manager = new TestConfigManager(storage);
        manager.Load();

        Assert.That(manager.Get<TestMode>(TestSetting.Mode).Value, Is.EqualTo(TestMode.Fast));
        Assert.That(readFile(), Does.Contain("Mode = Fast"));
    }

    /// <summary>
    /// A complete file is not rewritten just for being read.
    /// </summary>
    [Test]
    public void CompleteFileIsNotRewrittenOnLoad()
    {
        var manager = new TestConfigManager(storage);
        manager.Load();
        manager.Flush();

        string canonical = readFile();

        File.SetLastWriteTimeUtc(Path.Combine(tempDir, "test.ini"), new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var second = new TestConfigManager(storage);
        second.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(readFile(), Is.EqualTo(canonical));
            Assert.That(File.GetLastWriteTimeUtc(Path.Combine(tempDir, "test.ini")), Is.EqualTo(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                "Loading a file that needs no repair should not write to disk at all.");
        }
    }

    private class TestConfigManager : ConfigManager<TestSetting>
    {
        public TestConfigManager(Storage storage, bool registerDefaults = true)
            : base(storage)
        {
            if (!registerDefaults)
                return;

            Get(TestSetting.Mode, TestMode.Fast);
            Get(TestSetting.Enabled, true);
            Get(TestSetting.Amount, 1.0);
        }
    }
}
