// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Org.Libsdl.App;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Android;

/// <summary>
/// SDL's surface, telling the activity which insets cover the app's content, and asking the display for the refresh
/// rate the app wants.
/// </summary>
internal class SakuraSurface : SDLSurface
{
    private readonly SakuraActivity activity;

    public SakuraSurface(SakuraActivity activity, Context context)
        : base(context)
    {
        this.activity = activity;
    }

    private bool prefersHighestRefreshRate;

    /// <summary>
    /// Asks the display for its highest refresh rate at the current resolution, now and for every surface after
    /// this one. Called on the UI thread.
    /// </summary>
    internal void PreferHighestRefreshRate()
    {
        prefersHighestRefreshRate = true;
        askForHighestRefreshRate(Holder?.Surface);
    }

    /// <summary>
    /// Has touches reach the app as they happen. Android otherwise gathers a finger's moves up and hands them over once
    /// a frame, as the last position with the rest as history SDL doesn't read. SDL also stamps a touch with the time
    /// it arrives rather than when it happened, so a gathered tap would be timed up to a frame late, and a fast swipe
    /// would lose its path. Called on the UI thread.
    /// </summary>
    internal void RequestUnbufferedTouch()
    {
        // Android 10 can only ask from inside an event, which comes too late for the first touch.
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
            return;

        RequestUnbufferedDispatch((int)InputSourceType.Touchscreen);
        Logger.Verbose("Touches are dispatched as they happen");
    }

    public override void SurfaceChanged(ISurfaceHolder? holder, Format format, int width, int height)
    {
        base.SurfaceChanged(holder, format, width, height);

        // The request belongs to the surface, and a new one comes back from every trip to the background.
        if (prefersHighestRefreshRate)
            askForHighestRefreshRate(holder?.Surface);
    }

    private void askForHighestRefreshRate(Surface? surface)
    {
        var display = Display;

        if (display?.GetMode() is not Display.Mode current)
            return;

        // Only modes at the current resolution: some panels reach their highest rate only at a lower one, and the
        // app shouldn't change that.
        var highest = current;

        foreach (var mode in display.GetSupportedModes() ?? [])
        {
            if (mode.PhysicalWidth == current.PhysicalWidth && mode.PhysicalHeight == current.PhysicalHeight && mode.RefreshRate > highest.RefreshRate)
                highest = mode;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            // A vote the system weighs with the user's settings and battery saver, rather than a mode it must set.
            if (surface?.IsValid != true)
                return;

            surface.SetFrameRate(highest.RefreshRate, (int)SurfaceFrameRateCompatibility.Default);
        }
        else
        {
            // Android 10 can only be asked for a mode, through the window.
            var window = activity.Window!;
            var attributes = window.Attributes!;

            if (attributes.PreferredDisplayModeId == highest.ModeId)
                return;

            attributes.PreferredDisplayModeId = highest.ModeId;
            window.Attributes = attributes;
        }

        Logger.Verbose($"Asked the display for {highest.RefreshRate:0.##} Hz (now {current.RefreshRate:0.##} Hz)");
    }

    public override WindowInsets? OnApplyWindowInsets(View? view, WindowInsets? insets)
    {
        if (insets != null)
            activity.SetCoveredInsets(coveredBy(insets));

        // SDL's own handling stays: it also tracks the soft keyboard from here.
        return base.OnApplyWindowInsets(view, insets);
    }

    /// <summary>
    /// The insets of what is drawn over the content, in pixels: the status and navigation bars and the cutout.
    /// SDL's safe area adds the system gesture zones, but content there is in plain view; only an edge swipe
    /// goes to the system instead of the app.
    /// </summary>
    private static MarginPadding coveredBy(WindowInsets insets)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var covered = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            return new MarginPadding(covered.Top, covered.Right, covered.Bottom, covered.Left);
        }

        // Android 10 reports the bars as the system window insets, and the cutout on its own.
        var cutout = insets.DisplayCutout;

        return new MarginPadding(
            Math.Max(insets.SystemWindowInsetTop, cutout?.SafeInsetTop ?? 0),
            Math.Max(insets.SystemWindowInsetRight, cutout?.SafeInsetRight ?? 0),
            Math.Max(insets.SystemWindowInsetBottom, cutout?.SafeInsetBottom ?? 0),
            Math.Max(insets.SystemWindowInsetLeft, cutout?.SafeInsetLeft ?? 0));
    }
}
