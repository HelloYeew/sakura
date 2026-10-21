// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Android.Content;
using Android.Views;
using Org.Libsdl.App;
using Sakura.Framework.Graphics.Primitives;

namespace Sakura.Framework.Android;

/// <summary>
/// SDL's surface, telling the activity which insets cover the app's content.
/// </summary>
internal class SakuraSurface : SDLSurface
{
    private readonly SakuraActivity activity;

    public SakuraSurface(SakuraActivity activity, Context context)
        : base(context)
    {
        this.activity = activity;
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
