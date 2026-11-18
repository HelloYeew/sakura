// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Textures.ImageSharp;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sakura.Framework.Tests.Graphics.Textures;

/// <summary>
/// Tests for <see cref="ITextureManager.TryGet(string, out Texture?)"/>, which answers a missing file without an error or a
/// placeholder, and for what <see cref="ITextureManager.Get(string)"/> logs when a file is missing or cannot be decoded.
/// </summary>
[TestFixture]
public class TextureManagerTryGetTest
{
    private const string present = "present.png";
    private const string missing = "missing.png";
    private const string broken = "broken.png";

    private TemporaryStorage storage = null!;
    private NativeTextureManager textures = null!;

    /// <summary>
    /// Starts the logger, so the tests can see what was logged.
    /// </summary>
    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    /// <summary>
    /// Creates a storage holding an 8×4 picture and a file that is not a picture, and a manager reading from it.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        storage = new TemporaryStorage(Path.Combine(Path.GetTempPath(), $"sakura-texture-tryget-{Guid.NewGuid()}"));

        using (var image = new Image<Rgba32>(8, 4))
            image.SaveAsPng(storage.GetFullPath(present));

        File.WriteAllText(storage.GetFullPath(broken), "not a picture");

        textures = new NativeTextureManager(new HeadlessRenderer(new HeadlessTextureManager()), storage, new ImageSharpImageLoader());
    }

    /// <summary>
    /// Disposes the manager and deletes the storage.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        textures.Dispose();
        storage.Dispose();
        TextureRegistry.Reset();
    }

    /// <summary>
    /// A file that is there loads, and is the same texture <see cref="ITextureManager.Get(string)"/> returns.
    /// </summary>
    [Test]
    public void TryGetLoadsAFileThatIsThere()
    {
        bool found = textures.TryGet(present, out var texture);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(texture!.Width, Is.EqualTo(8));
            Assert.That(texture.Height, Is.EqualTo(4));
            Assert.That(textures.Get(present), Is.SameAs(texture), "the two share one cache");
        }
    }

    /// <summary>
    /// A missing file is answered with false, and nothing is logged.
    /// </summary>
    [Test]
    public void TryGetAnswersAMissingFileWithoutAnError()
    {
        bool found = true;
        Texture? texture = null;

        var errors = errorsDuring(() => found = textures.TryGet(missing, out texture));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.False);
            Assert.That(texture, Is.Null);
            Assert.That(errors, Is.Empty);
        }
    }

    /// <summary>
    /// A null or empty path finds nothing, where <see cref="ITextureManager.Get(string)"/> returns the white pixel.
    /// </summary>
    [Test]
    public void TryGetFindsNothingForAnEmptyPath()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(textures.TryGet(string.Empty, out _), Is.False);
            Assert.That(textures.TryGet(null!, out _), Is.False);
        }
    }

    /// <summary>
    /// <see cref="ITextureManager.Get(string)"/> still answers a missing file with the placeholder and an error.
    /// </summary>
    [Test]
    public void GetStillReportsAMissingFile()
    {
        Texture texture = null!;

        var errors = errorsDuring(() => texture = textures.Get(missing));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture, Is.Not.SameAs(textures.WhitePixel), "a missing file is told apart from an empty path");
            Assert.That(errors, Has.Count.EqualTo(1));
        }
    }

    /// <summary>
    /// A file that is there but is not a picture fails, and the error carries the exception with its stack trace rather than
    /// only its message.
    /// </summary>
    [Test]
    public void AFileThatFailsToDecodeIsLoggedWithItsException()
    {
        bool found = true;

        var errors = errorsDuring(() => found = textures.TryGet(broken, out _));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.False);
            Assert.That(errors, Has.Count.EqualTo(1), "a file that is there but broken is a fault, even when asked with TryGet");
            Assert.That(errors.FirstOrDefault().Message, Does.Contain("Exception").And.Match(@"\n\s+at "), "the exception and its stack trace");
        }
    }

    /// <summary>
    /// The headless manager decodes nothing, but still tells a file that is there from one that is not.
    /// </summary>
    [Test]
    public void HeadlessTryGetAsksTheStorage()
    {
        using var withStorage = new HeadlessTextureManager(storage);
        using var withoutStorage = new HeadlessTextureManager();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withStorage.TryGet(present, out var texture), Is.True);
            Assert.That(texture, Is.SameAs(withStorage.WhitePixel));
            Assert.That(withStorage.TryGet(missing, out _), Is.False);
            Assert.That(withoutStorage.TryGet(present, out _), Is.False, "without a storage, no file is found");
        }
    }

    /// <summary>
    /// The errors logged about the test's files while <paramref name="action"/> runs.
    /// </summary>
    private static List<LogEntry> errorsDuring(Action action)
    {
        var errors = new ConcurrentQueue<LogEntry>();
        Action<LogEntry> handler = e =>
        {
            if (e.Level == LogLevel.Error && (e.Message.Contains(missing) || e.Message.Contains(broken)))
                errors.Enqueue(e);
        };

        Logger.NewEntry += handler;

        try
        {
            action();
            Logger.Flush(TimeSpan.FromSeconds(10));
        }
        finally
        {
            Logger.NewEntry -= handler;
        }

        return errors.ToList();
    }
}
