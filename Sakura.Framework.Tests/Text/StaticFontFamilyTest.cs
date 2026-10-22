// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Text;

/// <summary>
/// A family shipped as one file per weight, with only some of the weights: the test font has Light to Bold, but no Thin,
/// ExtraLight, ExtraBold or Black.
/// </summary>
[TestFixture]
public class StaticFontFamilyTest
{
    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        store = new RendererFontStore(new HeadlessRenderer(textureManager));

        store.AddFontFamily(new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources").GetStorageForDirectory("Fonts"), "Comfortaa");
    }

    [TearDown]
    public void TearDown()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    [Test]
    public void AWeightTheFamilyHasIsItsOwnFile()
    {
        Assert.That(store.Get(FontUsage.Default.With(family: "Comfortaa", weight: FontWeights.Bold)).Name, Is.EqualTo("Comfortaa-Bold"));
    }

    /// <summary>
    /// A weight with no file falls back to the family quietly: it used to be registered all the same, and logged an
    /// error the first time it was asked for.
    /// </summary>
    [Test]
    public void AWeightTheFamilyLacksFallsBackWithoutAnError()
    {
        var errors = new ConcurrentQueue<LogEntry>();
        Action<LogEntry> handler = e =>
        {
            if (e.Level == LogLevel.Error)
                errors.Enqueue(e);
        };

        Logger.NewEntry += handler;

        Font font;

        try
        {
            font = store.Get(FontUsage.Default.With(family: "Comfortaa", weight: FontWeights.Black));
            Logger.Flush(TimeSpan.FromSeconds(10));
        }
        finally
        {
            Logger.NewEntry -= handler;
        }

        Assert.Multiple(() =>
        {
            Assert.That(font.Name, Is.EqualTo("Comfortaa-Regular"), "the family's regular");
            Assert.That(errors.Select(e => e.Message), Is.Empty);
        });
    }
}
