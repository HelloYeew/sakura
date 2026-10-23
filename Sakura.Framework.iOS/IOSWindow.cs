// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using CoreAnimation;
using Foundation;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Platform.Dialogs;
using SDL;
using UIKit;
using UniformTypeIdentifiers;
using static SDL.SDL3;

namespace Sakura.Framework.iOS;

/// <summary>
/// The window an app draws to on iOS: SDL's, with the system's edge swipes and the app's audio session set for the kind
/// of app, its refresh rate measured from the display as the system runs it, and the system's document picker for
/// opening files.
/// </summary>
internal class IOSWindow : SDLWindow
{
    private readonly AppType appType;
    private readonly bool takesAudioFocus;

    // How many frames in a row a new rate has to hold before the window takes it, so a single odd frame doesn't move it.
    private const int frames_to_settle = 8;

    private CADisplayLink? displayLink;
    private float refreshRate;
    private float newRate;
    private int newRateFrames;

    public IOSWindow(AppType appType, bool takesAudioFocus)
    {
        this.appType = appType;
        this.takesAudioFocus = takesAudioFocus;

        copyOpenedFilesIn();
    }

    // SDL's own handleURL: on its scene and app delegates, which a file opened in the app goes through.
    private static IntPtr sdlSceneHandleUrl;
    private static IntPtr sdlAppHandleUrl;

    /// <summary>
    /// A file another app opens in this one arrives as a URL, which SDL hands on as a drop of its path alone. That's
    /// enough for a copy the system made into the app's own storage, but an app whose files show in the Files app
    /// (<c>LSSupportsOpeningDocumentsInPlace</c>) gets the original where it is, readable only through the access that
    /// comes with the URL. So SDL's handleURL: is replaced with one that copies such a file in first, as the document
    /// picker does, and passes SDL the copy. Once for the process.
    /// </summary>
    private static unsafe void copyOpenedFilesIn()
    {
        if (sdlSceneHandleUrl != IntPtr.Zero || sdlAppHandleUrl != IntPtr.Zero)
            return;

        IntPtr selector = ObjCRuntime.Selector.GetHandle("handleURL:");
        sdlSceneHandleUrl = replaceMethod("SDLUIKitSceneDelegate", selector, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&sceneHandleUrl);
        sdlAppHandleUrl = replaceMethod("SDLUIKitDelegate", selector, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&appHandleUrl);

        if (sdlSceneHandleUrl == IntPtr.Zero && sdlAppHandleUrl == IntPtr.Zero)
            Logger.Warning("SDL's delegates have no handleURL: to take over, so a file opened in place from another app may not be readable.");
    }

    // The method's old implementation, or zero where the class or the method isn't there.
    private static IntPtr replaceMethod(string className, IntPtr selector, IntPtr implementation)
    {
        IntPtr cls = ObjCRuntime.Class.GetHandle(className);

        if (cls == IntPtr.Zero)
            return IntPtr.Zero;

        IntPtr method = class_getInstanceMethod(cls, selector);
        return method == IntPtr.Zero ? IntPtr.Zero : method_setImplementation(method, implementation);
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void sceneHandleUrl(IntPtr self, IntPtr selector, IntPtr url) => handleUrl(sdlSceneHandleUrl, self, selector, url);

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void appHandleUrl(IntPtr self, IntPtr selector, IntPtr url) => handleUrl(sdlAppHandleUrl, self, selector, url);

    private static unsafe void handleUrl(IntPtr sdl, IntPtr self, IntPtr selector, IntPtr url)
    {
        NSUrl? copy = null;

        try
        {
            if (ObjCRuntime.Runtime.GetNSObject<NSUrl>(url) is NSUrl { IsFileUrl: true, Path: string path } opened && !insideContainer(path)
                && copyIn(opened, "Opened", "the app that opened it") is string copied)
            {
                copy = NSUrl.FromFilename(copied);
            }
        }
        catch (Exception e)
        {
            // Never into SDL's caller, which is UIKit's: SDL gets the original, as it would have.
            Logger.Error("Couldn't copy in a file opened in the app.", e);
        }

        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)sdl)(self, selector, copy?.Handle ?? url);
        GC.KeepAlive(copy);
    }

    // Whether a path is in the app's own container, which it can read without being given access. Paths there come with
    // or without the /private prefix /var has.
    private static bool insideContainer(string path)
    {
        // Not known, so not copied: SDL gets the file as it would have.
        if (NSFileManager.HomeDirectory is not string home)
            return true;

        static string trim(string p) => p.StartsWith("/private/", StringComparison.Ordinal) ? p["/private".Length..] : p;
        return trim(path).StartsWith(trim(home).TrimEnd('/') + "/", StringComparison.Ordinal);
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr class_getInstanceMethod(IntPtr cls, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr method_setImplementation(IntPtr method, IntPtr implementation);

    protected override void ConfigureHints()
    {
        // A swipe in from an edge goes home, or opens Control Center or the notifications. A game asks for two of them,
        // the first only showing where it would go, so sliding off the bottom of a lane doesn't leave the game; the
        // home indicator stays, dimmed. An application leaves the edges to the system, as any other app does. Unset,
        // SDL would defer every edge for any borderless window, which on iOS is every window.
        SDL_SetHint(SDL_HINT_IOS_HIDE_HOME_INDICATOR, appType == AppType.Game ? "2" : "0");

        // The audio session's category, which SDL sets as it opens a device. Playback has the output to itself: other
        // apps' music stops, and the silent switch doesn't mute it, which is how a game's sound is heard. Ambient plays
        // along with whatever else is playing and goes quiet with the switch. Unset, SDL would play over other apps'
        // music and ignore the switch. The host sets the same itself, for the RemoteIO output, which starts without SDL.
        SDL_SetHint(SDL_HINT_AUDIO_CATEGORY, takesAudioFocus ? "playback" : "ambient");
    }

    protected override unsafe void OnCreated(SDL_Window* sdlWindow)
    {
        // The screen reports the highest rate it can run at, 120 on ProMotion, even while the system holds it lower: in
        // Low Power Mode, when the phone is hot, or when nothing asks for more. A display link is told the rate of each
        // frame as the system runs it, so the window's rate comes from one instead.
        //
        // SDL keeps a display link of its own on a screen faster than 60, asking it for the highest rate whatever the
        // app. It goes, and this one asks for what the kind of app needs: a game for every frame the screen can show, an
        // application for what the system thinks right, which drops while nothing moves.
        SDL_SetiOSAnimationCallback(sdlWindow, 1, null, IntPtr.Zero);

        displayLink = CADisplayLink.Create(onFrame);

        if (appType == AppType.Game)
        {
            float highest = UIScreen.MainScreen.MaximumFramesPerSecond;
            displayLink.PreferredFrameRateRange = CAFrameRateRange.Create(highest * 2 / 3, highest, highest);
        }

        displayLink.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Default);
    }

    // On the main thread, which is the event thread here, once a frame.
    private void onFrame()
    {
        double frameSeconds = displayLink!.TargetTimestamp - displayLink.Timestamp;

        if (frameSeconds <= 0)
            return;

        float rate = MathF.Round((float)(1 / frameSeconds));

        if (rate == refreshRate)
        {
            newRateFrames = 0;
            return;
        }

        if (rate != newRate)
        {
            newRate = rate;
            newRateFrames = 0;
        }

        if (++newRateFrames < frames_to_settle)
            return;

        refreshRate = rate;
        newRateFrames = 0;
        Logger.Debug($"The system runs the display at {rate} Hz");
        InvalidateRefreshRate();
    }

    protected override float? GetPlatformRefreshRate() => refreshRate > 0 ? refreshRate : null;

    // A phone's number pads have no Return key (an iPad's do), so a field taking a number couldn't be finished from the
    // keyboard. A bar above the pad gives it one, Done, which reaches SDL as its own Return key would.
    protected override void OnTextInputStarted(TextInputType type)
    {
        if (UIDevice.CurrentDevice.UserInterfaceIdiom != UIUserInterfaceIdiom.Phone || findTextField(KeyWindowController()?.View) is not UITextField field)
            return;

        bool numberPad = type is TextInputType.Number or TextInputType.NumberPassword;

        if (numberPad == (field.InputAccessoryView != null))
            return;

        field.InputAccessoryView = numberPad ? createDoneBar(field) : null;
        field.ReloadInputViews();
    }

    // SDL's own text field, hidden in its view, which takes what's typed and keeps the keyboard up.
    private static UITextField? findTextField(UIView? view)
    {
        if (view == null)
            return null;

        if (view is UITextField field)
            return field;

        foreach (var subview in view.Subviews)
        {
            if (findTextField(subview) is UITextField found)
                return found;
        }

        return null;
    }

    private static UIToolbar createDoneBar(UITextField field)
    {
        var bar = new UIToolbar();

        // SDL's view controller is the field's delegate, and Return comes through it. Sent directly: the method answers a
        // BOOL, which the bindings' PerformSelector would take for an object, and the app would end on it.
        var done = new UIBarButtonItem(UIBarButtonSystemItem.Done, (_, _) =>
        {
            if (field.WeakDelegate is NSObject handler)
                sendReturningBool(handler.Handle, ObjCRuntime.Selector.GetHandle("textFieldShouldReturn:"), field.Handle);
        });

        bar.Items = [new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace), done];
        bar.SizeToFit();
        return bar;
    }

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern byte sendReturningBool(IntPtr receiver, IntPtr selector, IntPtr argument);

    internal static UIViewController? KeyWindowController() =>
        UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
                     .SelectMany(scene => scene.Windows)
                     .FirstOrDefault(window => window.IsKeyWindow)?.RootViewController;

    // SDL has no file dialog on iOS: the system's document picker instead. It hands over the files where they are (in
    // Files, iCloud Drive, another app's folder), readable only while the app holds their security scope, so each is
    // copied into the app's own storage before the picker's callback returns, and the copy is what the app gets: a path
    // like any other, still there whenever the app reads it. The picker's own copies aren't promised to outlast its
    // callback, and an app reads its picks later, off the main thread.
    public override void ShowOpenFileDialog(FileDialogOptions options, Action<FileDialogResult> callback)
    {
        var presenter = KeyWindowController();

        if (presenter == null)
        {
            callback(FileDialogResult.Failed("There's no window to show the document picker over."));
            return;
        }

        var picker = new UIDocumentPickerViewController(contentTypesFor(options.Filters), false)
        {
            AllowsMultipleSelection = options.AllowMultiple
        };

        picker.DidPickDocumentAtUrls += (_, e) =>
        {
            string[] paths = e.Urls.Select(url => copyIn(url, "Picked", "the document picker")).OfType<string>().ToArray();
            Logger.Verbose($"Document picker: {e.Urls.Length} picked, {paths.Length} copied in");

            callback(paths.Length > 0 || e.Urls.Length == 0 ? FileDialogResult.FromPaths(paths) : FileDialogResult.Failed("The picked files couldn't be read."));
        };
        picker.WasCancelled += (_, _) => callback(FileDialogResult.Cancelled);

        presenter.PresentViewController(picker, true, null);
    }

    /// <summary>
    /// Copies a file the app was handed (picked, or opened in place) into a folder of its own in the app's temporary
    /// storage, keeping its name, and returns where to; null if it couldn't be read. The file coordinator downloads a file
    /// that's only in iCloud first.
    /// </summary>
    /// <param name="url">The file, with whatever access to it the system gave the app.</param>
    /// <param name="kind">The folder in temporary storage these go under.</param>
    /// <param name="from">Where the file came from, for the log.</param>
    private static string? copyIn(NSUrl url, string kind, string from)
    {
        bool scoped = url.StartAccessingSecurityScopedResource();

        try
        {
            string folder = Path.Combine(Path.GetTempPath(), kind, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);

            string? copied = null;

            new NSFileCoordinator().CoordinateRead(url, NSFileCoordinatorReadingOptions.WithoutChanges, out NSError? error, readable =>
            {
                string destination = Path.Combine(folder, readable.LastPathComponent ?? "file");
                File.Copy(readable.Path!, destination);
                copied = destination;
            });

            if (error != null)
                Logger.Error($"Couldn't read {url.LastPathComponent} from {from}: {error.LocalizedDescription}");

            return copied;
        }
        catch (Exception ex)
        {
            Logger.Error($"Couldn't copy {url.LastPathComponent} from {from}.", ex);
            return null;
        }
        finally
        {
            if (scoped)
                url.StopAccessingSecurityScopedResource();
        }
    }

    /// <summary>
    /// The types the picker offers for <paramref name="filters"/>' extensions: the type an app declares for one where
    /// there is one (yuuki's .osz), and one made from the extension otherwise. Anything at all when there's no filter or
    /// a filter takes every file.
    /// </summary>
    private static UTType[] contentTypesFor(IReadOnlyCollection<FileDialogFilter> filters)
    {
        var extensions = filters.SelectMany(filter => filter.ToPattern().Split(';', StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();

        if (extensions.Length == 0 || extensions.Contains("*"))
            return [UTTypes.Item];

        return extensions.Select(extension => UTType.CreateFromExtension(extension)).OfType<UTType>().ToArray();
    }
}
