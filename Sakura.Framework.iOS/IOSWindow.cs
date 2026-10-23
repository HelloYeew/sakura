// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using CoreAnimation;
using Foundation;
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
    }

    protected override void ConfigureHints()
    {
        // A swipe in from an edge goes home, or opens Control Center or the notifications. A game asks for two of them,
        // the first only showing where it would go, so sliding off the bottom of a lane doesn't leave the game; the
        // home indicator stays, dimmed. An application leaves the edges to the system, as any other app does. Unset,
        // SDL would defer every edge for any borderless window, which on iOS is every window.
        SDL_SetHint(SDL_HINT_IOS_HIDE_HOME_INDICATOR, appType == AppType.Game ? "2" : "0");

        // The audio session's category, which SDL sets as it opens the device. Playback has the output to itself: other
        // apps' music stops, and the silent switch doesn't mute it, which is how a game's sound is heard. Ambient plays
        // along with whatever else is playing and goes quiet with the switch. Unset, SDL would play over other apps'
        // music and ignore the switch.
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

    // SDL has no file dialog on iOS. The system's document picker hands over copies in the app's own storage, which can
    // be read by path like any other file, with nothing to ask permission for.
    public override void ShowOpenFileDialog(FileDialogOptions options, Action<FileDialogResult> callback)
    {
        var presenter = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
                                     .SelectMany(scene => scene.Windows)
                                     .FirstOrDefault(window => window.IsKeyWindow)?.RootViewController;

        if (presenter == null)
        {
            callback(FileDialogResult.Failed("There's no window to show the document picker over."));
            return;
        }

        var picker = new UIDocumentPickerViewController(contentTypesFor(options.Filters), true)
        {
            AllowsMultipleSelection = options.AllowMultiple
        };

        picker.DidPickDocumentAtUrls += (_, e) => callback(FileDialogResult.FromPaths(e.Urls.Select(url => url.Path!).ToArray()));
        picker.WasCancelled += (_, _) => callback(FileDialogResult.Cancelled);

        presenter.PresentViewController(picker, true, null);
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
