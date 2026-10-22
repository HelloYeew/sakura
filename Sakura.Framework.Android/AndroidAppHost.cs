// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Android.Content;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using AndroidAudioManager = Android.Media.AudioManager;
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
        Performance = new AndroidPerformance(activity);

        Logger.ConsoleOutput = AndroidLog.For(appName);
    }

    /// <summary>
    /// The app's private files directory, which belongs to this app alone, so nothing needs a folder of its own in it.
    /// </summary>
    private string filesDirectory => activity.FilesDir!.AbsolutePath;

    public override IEnumerable<string> UserStoragePaths => [filesDirectory];

    protected override Storage GetDefaultAppStorage() => GetStorage(filesDirectory);

    public override Storage GetStorage(string path) => new NativeStorage(path, this);

    protected override IWindow CreateWindow() => new AndroidWindow(activity, Options.AppType);

    public override IPlatformPerformance Performance { get; }

    // What the speaker or headphones run at, which a stream needs to match for the low-latency path: usually
    // 48 kHz, in bursts of 96 to 240 frames. SDL only knows a placeholder (44.1 kHz) until a stream is open.
    public override NativeAudioOutput? NativeAudioOutput
    {
        get
        {
            var audio = activity.GetSystemService(Context.AudioService) as AndroidAudioManager;

            if (!int.TryParse(audio?.GetProperty(AndroidAudioManager.PropertyOutputSampleRate), out int sampleRate) || sampleRate <= 0)
                return null;

            if (!int.TryParse(audio.GetProperty(AndroidAudioManager.PropertyOutputFramesPerBuffer), out int framesPerBurst))
                framesPerBurst = 0;

            return new NativeAudioOutput(sampleRate, Math.Max(framesPerBurst, 0));
        }
    }

    // Another app can only open one of ours through a content URI handed out by a FileProvider, which isn't set up yet.
    public override bool OpenFileExternally(string filename) => false;

    public override bool PresentFileExternally(string filename) => false;

    // SDL only asks to quit when the activity is being destroyed, and nothing can stop that or wait on a dialog.
    // The app has already been told it was going to the background, which is its chance to save.
    protected override bool CanBlockExit => false;

    // What the system does with Back on an app's first screen: it goes to the background, still running.
    protected override void OnUnhandledSystemBack() => activity.RunOnUiThread(() => activity.MoveTaskToBack(true));

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
