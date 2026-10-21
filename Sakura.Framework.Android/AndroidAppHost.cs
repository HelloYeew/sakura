// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Android.Content;
using Sakura.Framework.Extensions;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Uri = Android.Net.Uri;

namespace Sakura.Framework.Android;

/// <summary>
/// Runs an <see cref="App"/> inside a <see cref="SakuraActivity"/>.
/// </summary>
public class AndroidAppHost : SDLAppHost
{
    private readonly SakuraActivity activity;

    public AndroidAppHost(SakuraActivity activity, string appName, HostOptions? options = null)
        : base(appName, options)
    {
        this.activity = activity;

        Logger.ConsoleOutput = AndroidLog.For(appName);
    }

    /// <summary>
    /// The app's private files directory, which belongs to this app alone, so nothing needs a folder of its own in it.
    /// </summary>
    private string filesDirectory => activity.FilesDir!.AbsolutePath;

    public override IEnumerable<string> UserStoragePaths => [filesDirectory];

    protected override Storage GetDefaultAppStorage() => GetStorage(filesDirectory);

    public override Storage GetStorage(string path) => new NativeStorage(path, this);

    protected override IWindow CreateWindow() => new AndroidWindow(activity);

    // Another app can only open one of ours through a content URI handed out by a FileProvider, which isn't set up yet.
    public override bool OpenFileExternally(string filename) => false;

    public override bool PresentFileExternally(string filename) => false;

    public override void OpenUrlExternally(string url)
    {
        if (!url.CheckIsValidUrl())
            throw new ArgumentException("The provided URL is not a valid protocol to open externally, it must start with http://, https:// or mailto:.", nameof(url));

        try
        {
            var intent = new Intent(Intent.ActionView, Uri.Parse(url));
            intent.AddFlags(ActivityFlags.NewTask);
            activity.StartActivity(intent);
        }
        catch (ActivityNotFoundException ex)
        {
            Logger.Error("No app on this device can open the link.", ex);
        }
    }
}
