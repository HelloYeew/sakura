// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Display;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Org.Libsdl.App;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Platform;
using SDL;
using static SDL.SDL3;

namespace Sakura.Framework.Android;

/// <summary>
/// The activity an app runs in on Android. SDL's activity owns the surface and the input, and runs
/// <see cref="Main"/> on a thread of its own, where the host runs the <see cref="App"/> from
/// <see cref="CreateApp"/>. An app's activity derives from this and carries the manifest attributes.
/// </summary>
public abstract class SakuraActivity : SDLActivity
{
    /// <summary>
    /// Creates the app to run. Called on SDL's thread, once per launch.
    /// </summary>
    protected abstract App CreateApp();

    /// <summary>
    /// The name the host logs under. Defaults to the package name.
    /// </summary>
    protected virtual string AppName => PackageName ?? GetType().Name;

    /// <summary>
    /// The options the host runs with.
    /// </summary>
    protected virtual HostOptions CreateHostOptions() => new HostOptions();

    protected override string[] GetLibraries() => ["SDL3"];

    protected override SDLSurface CreateSDLSurface(Context? context) => new SakuraSurface(this, context!);

    private readonly object coveredInsetsLock = new object();
    private MarginPadding? coveredInsets;

    /// <summary>
    /// The insets of what covers the content (the system bars and the cutout) in pixels, or null before the
    /// system has reported any.
    /// </summary>
    internal MarginPadding? CoveredInsets
    {
        get
        {
            lock (coveredInsetsLock)
                return coveredInsets;
        }
    }

    /// <summary>
    /// Raised on the UI thread when <see cref="CoveredInsets"/> changes.
    /// </summary>
    internal event Action? CoveredInsetsChanged;

    internal void SetCoveredInsets(MarginPadding insets)
    {
        lock (coveredInsetsLock)
        {
            if (coveredInsets is MarginPadding current && current.Equals(insets))
                return;

            coveredInsets = insets;
        }

        CoveredInsetsChanged?.Invoke();
    }

    private DisplayListener? displayListener;
    private volatile float refreshRate;

    /// <summary>
    /// The refresh rate of the display the app is on, or 0 before it's known. SDL reads it only when the surface
    /// changes, so it misses a panel switching between 60 and 120 Hz. Safe to read from any thread.
    /// </summary>
    internal float RefreshRate => refreshRate;

    /// <summary>
    /// Raised on the UI thread when <see cref="RefreshRate"/> changes.
    /// </summary>
    internal event Action? RefreshRateChanged;

    /// <summary>
    /// Asks the display for its highest refresh rate while the app's surface shows. Safe to call from any thread.
    /// </summary>
    internal void PreferHighestRefreshRate() => RunOnUiThread(() => (MSurface as SakuraSurface)?.PreferHighestRefreshRate());

    private void updateRefreshRate()
    {
        // Display moved from the window manager to the context in API 30.
#pragma warning disable CA1422
        var display = OperatingSystem.IsAndroidVersionAtLeast(30) ? Display : WindowManager?.DefaultDisplay;
#pragma warning restore CA1422
        float rate = display?.RefreshRate ?? 0;

        if (rate == refreshRate)
            return;

        refreshRate = rate;
        RefreshRateChanged?.Invoke();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // The process starts in '/', which the app can't write to, so relative paths go to its own files instead.
        System.Environment.CurrentDirectory = FilesDir!.AbsolutePath;

        base.OnCreate(savedInstanceState);

        showSurfaceUnderSystemBars();
        panForSoftKeyboard();

        updateRefreshRate();
        displayListener = new DisplayListener(updateRefreshRate);
        (GetSystemService(DisplayService) as DisplayManager)?.RegisterDisplayListener(displayListener, null);
    }

    protected override void OnDestroy()
    {
        if (displayListener != null)
            (GetSystemService(DisplayService) as DisplayManager)?.UnregisterDisplayListener(displayListener);

        base.OnDestroy();
    }

    /// <summary>
    /// Has Android pan the window up when the soft keyboard would cover the field being typed in, where SDL keeps a
    /// hidden text view. It picks that by itself when no view scrolls, but this doesn't rest on the guess. An app's
    /// manifest choosing another way is left alone.
    /// </summary>
    private void panForSoftKeyboard()
    {
        var window = Window!;
        var mode = window.Attributes!.SoftInputMode;

        if ((mode & SoftInput.MaskAdjust) == SoftInput.AdjustUnspecified)
            window.SetSoftInputMode((mode & ~SoftInput.MaskAdjust) | SoftInput.AdjustPan);
    }

    /// <summary>
    /// Moves the soft keyboard that's up onto another field. SDL's hidden text view, which Android pans into view and
    /// the keyboard takes its layout from, is placed only as the keyboard opens. Safe to call from any thread.
    /// </summary>
    /// <param name="type">The field's input type.</param>
    /// <param name="caret">Where the field's caret is, in pixels.</param>
    /// <param name="switchLayout">Whether the type differs from the field before.</param>
    internal void RetargetKeyboard(InputTypes type, SDL_Rect caret, bool switchLayout)
    {
        // SDL's own way of placing the view, which gives it the type too.
        ShowTextInput((int)type, caret.x, caret.y, caret.w, caret.h);

        if (!switchLayout)
            return;

        // Queued behind that: the keyboard only reads the view's type again when its input restarts.
        RunOnUiThread(() =>
        {
            var view = MLayout?.FindFocus();

            if (view?.OnCheckIsTextEditor() == true)
                (GetSystemService(InputMethodService) as InputMethodManager)?.RestartInput(view);
        });
    }

    /// <summary>
    /// SDL would pick the orientation from the window as it's made, allowing every one unless the app set
    /// <c>SDL_HINT_ORIENTATIONS</c>, over whatever the activity's manifest entry asks for. The manifest decides
    /// instead: it applies before the activity shows, so an app made for landscape doesn't start upright and turn.
    /// </summary>
    public override void SetOrientationBis(int w, int h, bool resizable, string? hint)
    {
        if (!string.IsNullOrEmpty(hint))
            base.SetOrientationBis(w, h, resizable, hint);
    }

    /// <summary>
    /// The surface sits under the window, which draws backgrounds of its own behind the status and navigation
    /// bars, over whatever the app drew there (a background that bleeds, for one). Those are made see-through,
    /// with no contrast scrim either.
    /// </summary>
    private void showSurfaceUnderSystemBars()
    {
        var window = Window!;

        window.AddFlags(WindowManagerFlags.DrawsSystemBarBackgrounds);

        // Up to Android 14 the cutout is letterboxed in black once the bars are hidden (Borderless), unless the window
        // asks to draw there; the safe area keeps content clear of it. Android 15 does this for every app targeting it.
        var attributes = window.Attributes!;
        attributes.LayoutInDisplayCutoutMode = OperatingSystem.IsAndroidVersionAtLeast(30) ? LayoutInDisplayCutoutMode.Always : LayoutInDisplayCutoutMode.ShortEdges;
        window.Attributes = attributes;

        // Obsolete from API 35, where enforced edge-to-edge is meant to make the bars clear, but this theme's
        // still came out black on an API 35 emulator without them.
#pragma warning disable CA1422
        window.SetStatusBarColor(Color.Transparent);
        window.SetNavigationBarColor(Color.Transparent);

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            window.StatusBarContrastEnforced = false;
            window.NavigationBarContrastEnforced = false;
        }
#pragma warning restore CA1422
    }

    protected override void Main()
    {
        // SDL counts on its own main entry point having marked it ready, and this runs in place of that.
        SDL_SetMainReady();

        using (var host = new AndroidAppHost(this, AppName, CreateHostOptions()))
        using (var app = CreateApp())
            host.Run(app);

        // The app exited by itself, so the activity goes with it rather than staying on a dead surface.
        if (!IsFinishing)
            Finish();
    }

    /// <summary>
    /// Hears of every change to every display, a mode change included, on the UI thread.
    /// </summary>
    private sealed class DisplayListener : Java.Lang.Object, DisplayManager.IDisplayListener
    {
        private readonly Action changed;

        public DisplayListener(Action changed)
        {
            this.changed = changed;
        }

        public void OnDisplayChanged(int displayId) => changed();

        public void OnDisplayAdded(int displayId)
        {
        }

        public void OnDisplayRemoved(int displayId)
        {
        }
    }
}
