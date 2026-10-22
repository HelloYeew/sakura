// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Org.Libsdl.App;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Platform;
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

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // The process starts in '/', which the app can't write to, so relative paths go to its own files instead.
        System.Environment.CurrentDirectory = FilesDir!.AbsolutePath;

        base.OnCreate(savedInstanceState);

        showSurfaceUnderSystemBars();
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
}
