// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using AVFoundation;
using Foundation;
using Sakura.Framework.Audio;
using Sakura.Framework.Configurations;
using Sakura.Framework.Extensions;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using UIKit;
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

        // A game's sound stops other apps' music and plays with the silent switch on (Playback); anything else mixes in
        // with what's playing and keeps to the switch (Ambient). Set here, as the native engine's RemoteIO output starts
        // without SDL, which would set it as its own device opened. SDL's hint (IOSWindow) asks for the same, options
        // included, so SDL leaves the session be where it plays instead: it starts the session over on any difference.
        var error = Options.TakesAudioFocus
            ? AVAudioSession.SharedInstance().SetCategory(AVAudioSessionCategory.Playback, 0)
            : AVAudioSession.SharedInstance().SetCategory(AVAudioSessionCategory.Ambient, AVAudioSessionCategoryOptions.MixWithOthers);

        if (error != null)
            Logger.Error($"Could not set the audio session's category: {error.LocalizedDescription}");

        // A call, an alarm or Siri takes the audio output for a while, stopping it meanwhile; the app hears of it as
        // OutputInterrupted, as it does from Android's audio focus, to pause what's playing. The end isn't always sent,
        // so coming back to the front ends it too, as SDL does.
        interruptionObserver = AVAudioSession.Notifications.ObserveInterruption((_, e) => setInterrupted(e.InterruptionType == AVAudioSessionInterruptionType.Began));
        activeObserver = UIApplication.Notifications.ObserveDidBecomeActive((_, _) => setInterrupted(false));
    }

    // Kept for as long as the host, which is the app's life.
    private readonly NSObject interruptionObserver;
    private readonly NSObject activeObserver;

    // Only touched on the main thread, where the notifications come.
    private bool audioInterrupted;

    private void setInterrupted(bool interrupted)
    {
        if (interrupted == audioInterrupted)
            return;

        audioInterrupted = interrupted;

        // The interruption made the session inactive, and the output can't start again until it's active.
        if (!interrupted && !AVAudioSession.SharedInstance().SetActive(true, out NSError? error))
            Logger.Error($"Could not make the audio session active again: {error?.LocalizedDescription}");

        SetAudioInterrupted(interrupted);
    }

    /// <summary>
    /// The audio session's rate and I/O buffer, read with the session made active, as only then are they the
    /// hardware's. The device buffer setting is the I/O buffer asked for here: the native engine fills the system's own
    /// buffer, and only a preference, as the hardware takes the nearest size it can do.
    /// </summary>
    public override NativeAudioOutput? NativeAudioOutput
    {
        get
        {
            var session = AVAudioSession.SharedInstance();

            if (!session.SetActive(true, out NSError? error))
            {
                Logger.Error($"Could not make the audio session active: {error?.LocalizedDescription}");
                return null;
            }

            int frames = FrameworkConfigManager.Get<int>(FrameworkSetting.AudioDeviceBufferFrames).Value;

            if (frames > 0 && session.SampleRate > 0 && !session.SetPreferredIOBufferDuration(frames / session.SampleRate, out error))
                Logger.Error($"Could not ask for a {frames}-frame audio buffer: {error?.LocalizedDescription}");

            return new NativeAudioOutput((int)session.SampleRate, (int)Math.Round(session.IOBufferDuration * session.SampleRate));
        }
    }

    public override IPlatformPerformance Performance { get; } = new IOSPerformance();

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

    // iOS has no default app to open a file with, nor a file browser to show it in: the app's data isn't in Files. The
    // share sheet is what there is, where another app opens the file or Files keeps a copy of it. A folder can't go
    // through it, so asking for one does nothing.
    public override bool OpenFileExternally(string filename) => share(filename);

    public override bool PresentFileExternally(string filename) => share(filename);

    private static bool share(string filename)
    {
        if (!File.Exists(filename))
            return false;

        NSRunLoop.Main.BeginInvokeOnMainThread(() =>
        {
            if (IOSWindow.KeyWindowController() is not UIViewController presenter || presenter.View is not UIView view)
            {
                Logger.Error($"There's no window to show the share sheet for {filename} over.");
                return;
            }

            var sheet = new UIActivityViewController([NSUrl.FromFilename(filename)], null);

            // A popover on an iPad, which needs somewhere to come from: the middle of the window, with no arrow.
            if (sheet.PopoverPresentationController is UIPopoverPresentationController popover)
            {
                popover.SourceView = view;
                popover.SourceRect = new CoreGraphics.CGRect(view.Bounds.Width / 2, view.Bounds.Height / 2, 0, 0);
                popover.PermittedArrowDirections = 0;
            }

            presenter.PresentViewController(sheet, true, null);
        });

        return true;
    }

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
