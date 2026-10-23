// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Foundation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using static SDL.SDL3;

namespace Sakura.Framework.iOS;

/// <summary>
/// Runs an <see cref="App"/> on iOS, started by <see cref="SakuraApplication.Run"/>.
/// </summary>
public class IOSAppHost : SDLAppHost
{
    public IOSAppHost(string appName, HostOptions? options = null)
        : base(appName, options)
    {
        Logger.ConsoleOutput = IOSLog.For(appName);
    }

    /// <summary>
    /// <c>Library/Application Support</c>, where the app keeps its data. Each app has a container of its own, so nothing
    /// needs a folder of its own in it. It's backed up, and kept out of the Files app, unlike <c>Documents</c>, which is
    /// where .NET puts <see cref="Environment.SpecialFolder.LocalApplicationData"/> on iOS.
    /// </summary>
    public string DataDirectory { get; } = userDirectory(NSSearchPathDirectory.ApplicationSupportDirectory);

    /// <summary>
    /// <c>Library/Caches</c>: left out of backups, and cleared by the system while the app isn't running if the device is
    /// short of space.
    /// </summary>
    private readonly string cachesDirectory = userDirectory(NSSearchPathDirectory.CachesDirectory);

    private static string userDirectory(NSSearchPathDirectory directory)
    {
        string path = NSFileManager.DefaultManager.GetUrls(directory, NSSearchPathDomain.User)[0].Path!;

        // Application Support doesn't exist until an app makes it.
        Directory.CreateDirectory(path);
        return path;
    }

    public override IEnumerable<string> UserStoragePaths => [DataDirectory];

    protected override Storage GetDefaultAppStorage() => GetStorage(DataDirectory);

    // Logs are this device's, and a cache is made again, so neither goes into the backup.
    protected override Storage GetLogStorage() => GetStorage(Path.Combine(cachesDirectory, "logs"));

    protected override Storage GetCacheStorage() => GetStorage(cachesDirectory);

    public override Storage GetStorage(string path) => new NativeStorage(path, this);

    protected override IWindow CreateWindow() => new IOSWindow(Options.AppType, Options.TakesAudioFocus);

    public override ISystemAudioRoute SystemAudioRoute { get; } = new IOSAudioRoute();

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern nuint os_proc_available_memory();

    // 0 where the system keeps no limit for the process, as on the simulator.
    public override long? AvailableMemory => os_proc_available_memory() is var available and > 0 ? (long)available : null;

    // Another app opens one of this app's files through a share sheet, which isn't set up yet.
    public override bool OpenFileExternally(string filename) => false;

    public override bool PresentFileExternally(string filename) => false;

    // iOS ends an app without asking: from the app switcher, or when it wants the memory back. The app has already been
    // told it was going to the background, which is its chance to save.
    protected override bool CanBlockExit => false;

    // Nor does an app end itself, and SDL wouldn't let it go either: once the app was done, its last frame would stay on
    // the screen with nothing behind it. An app has no Quit on a phone, so a call here is a mistake to see in the log.
    public override void Exit() =>
        Logger.Error("Exit() does nothing on iOS: only the user ends an app there. Leave quitting out of the app on mobile.");

    public override void OpenUrlExternally(string url)
    {
        if (!url.CheckIsValidUrl())
            throw new ArgumentException("The provided URL is not a valid protocol to open externally, it must start with http://, https:// or mailto:.", nameof(url));

        if (!SDL_OpenURL(url))
            Logger.Error($"Unable to open external link: {SDL_GetError()}");
    }
}
