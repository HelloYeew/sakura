// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Platform;

public class HeadlessAppHost : AppHost
{
    private readonly HeadlessTextureManager textureManager;
    private TemporaryStorage temporaryStorage;

    public HeadlessAppHost(string appName, HostOptions? options = null) : base(appName, options)
    {
        textureManager = new HeadlessTextureManager();
    }

    public override bool IsHeadless => true;

    public override bool OpenFileExternally(string filename) => false;
    public override bool PresentFileExternally(string filename) => false;

    public override void OpenUrlExternally(string url)
    {

    }

    protected override IWindow CreateWindow()
    {
        return new HeadlessWindow();
    }

    protected override IRenderer CreateRenderer()
    {
        return new HeadlessRenderer(textureManager);
    }

    protected override Storage GetDefaultAppStorage()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"sakura-headless-{Guid.NewGuid()}");
        temporaryStorage = new TemporaryStorage(tempPath, this);
        return temporaryStorage;
    }

    public override Storage GetStorage(string path)
    {
        return new DesktopStorage(path, null);
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        if (isDisposing)
        {
            temporaryStorage.Dispose();
            textureManager.Dispose();
        }
    }
}
