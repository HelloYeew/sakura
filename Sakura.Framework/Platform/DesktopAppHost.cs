// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Diagnostics;
using System.IO;
using Sakura.Framework.Extensions;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Platform;

public class DesktopAppHost : AppHost
{
    public bool IsPortableInstallation { get; }

    public DesktopAppHost(string appName, HostOptions options = null) : base(appName, options)
    {
        IsPortableInstallation = Options.PortableInstallation;
    }

    protected sealed override Storage GetDefaultAppStorage()
    {
        // TODO: Replace the framework config file name with a ConfigManager for framework
        if (IsPortableInstallation || File.Exists(Path.Combine(RuntimeInfo.StartupDirectory, "framework.ini")))
            return GetStorage(RuntimeInfo.StartupDirectory);

        return base.GetDefaultAppStorage();
    }

    public override Storage GetStorage(string path) => new DesktopStorage(path, this);

    protected override IWindow CreateWindow() => new DesktopWindow();

    protected override IRenderer CreateRenderer() => new GLRenderer();

    public override bool OpenFileExternally(string filename)
    {
        openUsingShellExecute(filename);
        return true;
    }

    public override bool PresentFileExternally(string filename)
    {
        OpenFileExternally(Path.GetDirectoryName(filename.TrimDirectorySeparator()));
        return true;
    }

    public override void OpenUrlExternally(string url)
    {
        if (!url.CheckIsValidUrl())
            throw new ArgumentException("The provided URL is not a valid protocol to open externally, it must start with http://, https:// or mailto:.", nameof(url));

        try
        {
            openUsingShellExecute(url);
        }
        catch (Exception ex)
        {
            Logger.Error("Unable to open external link.", ex);
        }
    }

    private static void openUsingShellExecute(string path) => Process.Start(new ProcessStartInfo
    {
        // https://github.com/dotnet/runtime/issues/17938
        FileName = path,
        UseShellExecute = true
    });

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
    }
}
