// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Android.Content;
using Android.Media;
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
    private AudioFocusHolder? audioFocus;

    public AndroidAppHost(SakuraActivity activity, string appName, HostOptions? options = null)
        : base(appName, options)
    {
        this.activity = activity;
        Performance = new AndroidPerformance(activity);
        Haptics = new AndroidHaptics(activity);

        Logger.ConsoleOutput = AndroidLog.For(appName);

        if (Options.TakesAudioFocus)
        {
            audioFocus = new AudioFocusHolder(activity, Options.AppType, SetAudioInterrupted);

            // The app is at the front already: SDL starts this thread once it is.
            audioFocus.Take();
            activity.CameToFront += audioFocus.Take;
            activity.LeftScreen += audioFocus.Release;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (audioFocus != null)
        {
            activity.CameToFront -= audioFocus.Take;
            activity.LeftScreen -= audioFocus.Release;
            audioFocus.Release();
            audioFocus.Dispose();
            audioFocus = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Where the app keeps its data: its private files, which belong to this app alone, so nothing needs a folder of its
    /// own in it. An app that shares its files (<see cref="SakuraActivity.SharesFiles"/>) keeps them in its folder on
    /// shared storage instead, where a computer reaches them over USB.
    /// </summary>
    /// <remarks>
    /// Found when first asked for, which is once the logger runs, so what moving the files over has to say is heard.
    /// </remarks>
    public string DataDirectory => dataDirectory ??= findDataDirectory();

    private string? dataDirectory;
    private bool sharingFiles;

    private string findDataDirectory()
    {
        string privateFiles = activity.FilesDir!.AbsolutePath;

        if (!activity.SharesFiles)
            return privateFiles;

        // Shared storage is built into any phone of recent years, but can be missing (unmounted).
        var shared = activity.GetExternalFilesDir(null);

        if (shared == null || global::Android.OS.Environment.GetExternalStorageState(shared) != global::Android.OS.Environment.MediaMounted)
        {
            Logger.Error("Shared storage isn't available, so the app's files are kept where only it can reach them.");
            return privateFiles;
        }

        sharingFiles = true;
        moveInto(privateFiles, shared.AbsolutePath);
        return shared.AbsolutePath;
    }

    // What the app kept before it shared its files moves along, rather than staying where the app no longer looks. The
    // two are on different file systems, so a folder is copied under a hidden name, renamed once it's whole and only then
    // removed: one cut off halfway is copied again next launch. Anything of the same name already there wins. Names
    // starting with a dot are the runtime's own (fast deployment keeps its assemblies in files/.__override__) and stay.
    private static void moveInto(string from, string to)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(from))
        {
            string name = Path.GetFileName(entry);
            string target = Path.Combine(to, name);

            if (name.StartsWith('.') || File.Exists(target) || Directory.Exists(target))
                continue;

            try
            {
                if (Directory.Exists(entry))
                {
                    string partial = Path.Combine(to, $".{name}.moving");

                    if (Directory.Exists(partial))
                        Directory.Delete(partial, true);

                    copyDirectory(entry, partial);
                    Directory.Move(partial, target);
                    Directory.Delete(entry, true);
                }
                else
                    File.Move(entry, target);

                Logger.Verbose($"Moved {name} to the app's folder on shared storage.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Logger.Error($"Could not move {name} to the app's folder on shared storage; it's tried again next launch.", e);
            }
        }
    }

    private static void copyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (string file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));

        foreach (string directory in Directory.EnumerateDirectories(from))
            copyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
    }

    public override IEnumerable<string> UserStoragePaths => [DataDirectory];

    protected override Storage GetDefaultAppStorage() => GetStorage(DataDirectory);

    // Out of the app data Android backs up, which has a 25 MB quota and is restored onto a new phone: logs are this
    // phone's, and a cache is made again. The cache is Android's too, cleared when the phone runs short of space. An app
    // that shares its files keeps its logs with them, for the user to reach as on a desktop, and in the backup with them:
    // shared storage has no folder the backup leaves out.
    protected override Storage GetLogStorage()
    {
        string data = DataDirectory;
        return GetStorage(sharingFiles ? Path.Combine(data, "logs") : Path.Combine(activity.NoBackupFilesDir!.AbsolutePath, "logs"));
    }

    protected override Storage GetCacheStorage() => GetStorage(activity.CacheDir!.AbsolutePath);

    public override Storage GetStorage(string path) => new NativeStorage(path, this);

    protected override IWindow CreateWindow() => new AndroidWindow(activity, Options.AppType);

    public override IPlatformPerformance Performance { get; }

    public override IPlatformHaptics Haptics { get; }

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

            return new NativeAudioOutput(sampleRate, Math.Max(framesPerBurst, 0), describeAudioDevice);
        }
    }

    // An output as the player knows it: what's built in or in the headphone socket by its kind, and what's connected
    // to the phone by its own name, with how it's connected.
    private string describeAudioDevice(int id)
    {
        var audio = activity.GetSystemService(Context.AudioService) as AndroidAudioManager;
        var device = audio?.GetDevices(GetDevicesTargets.Outputs)?.FirstOrDefault(d => d.Id == id);

        if (device == null)
            return string.Empty;

        string product = device.ProductName ?? string.Empty;

        return device.Type switch
        {
            AudioDeviceType.BuiltinSpeaker or AudioDeviceType.BuiltinSpeakerSafe => "Speaker",
            AudioDeviceType.BuiltinEarpiece => "Earpiece",
            AudioDeviceType.WiredHeadphones or AudioDeviceType.WiredHeadset => "Wired headphones",
            AudioDeviceType.BluetoothA2dp or AudioDeviceType.BluetoothSco or AudioDeviceType.BleHeadset or AudioDeviceType.BleSpeaker
                or AudioDeviceType.HearingAid => $"Bluetooth: {product}",
            AudioDeviceType.UsbHeadset or AudioDeviceType.UsbDevice or AudioDeviceType.UsbAccessory => $"USB: {product}",
            AudioDeviceType.Hdmi or AudioDeviceType.HdmiArc or AudioDeviceType.HdmiEarc => $"HDMI: {product}",
            _ => product
        };
    }

    // Another app can only open one of ours through a content URI handed out by a FileProvider, which isn't set up yet.
    public override bool OpenFileExternally(string filename) => false;

    public override bool PresentFileExternally(string filename) => false;

    // SDL only asks to quit when the activity is being destroyed, and nothing can stop that or wait on a dialog.
    // The app has already been told it was going to the background, which is its chance to save.
    protected override bool CanBlockExit => false;

    // Another app's music took the output for good, and the player's press is them back for this one's.
    protected override void OnPressWhileAudioInterrupted() => audioFocus?.TakeBack();

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
