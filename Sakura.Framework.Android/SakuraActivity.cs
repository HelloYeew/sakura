// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Android.OS;
using Org.Libsdl.App;
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

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // The process starts in '/', which the app can't write to, so relative paths go to its own files instead.
        System.Environment.CurrentDirectory = FilesDir!.AbsolutePath;

        base.OnCreate(savedInstanceState);
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
