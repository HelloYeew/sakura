// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sakura.Framework.Platform;
using SDL;
using static SDL.SDL3;

namespace Sakura.Framework.iOS;

/// <summary>
/// Starts an app on iOS. SDL owns the UIKit application and its scene, so the app's <c>Main</c> hands the process over
/// to it through <see cref="Run"/>, and the host runs the <see cref="App"/> on the main thread once UIKit has launched.
/// </summary>
public static unsafe class SakuraApplication
{
    private static string appName = string.Empty;
    private static Func<App>? createApp;
    private static Func<HostOptions>? createHostOptions;

    /// <summary>
    /// Runs the app. Call from the app's <c>Main</c>: it doesn't return while the app runs, and once the app has exited
    /// it doesn't return at all, since an iOS app can't end itself.
    /// </summary>
    /// <param name="appName">The name the host logs under and keeps its settings for.</param>
    /// <param name="createApp">Creates the app to run, on the main thread, once UIKit has launched.</param>
    /// <param name="createHostOptions">The options the host runs with, or null for the defaults.</param>
    public static void Run(string appName, Func<App> createApp, Func<HostOptions>? createHostOptions = null)
    {
        SakuraApplication.appName = appName;
        SakuraApplication.createApp = createApp;
        SakuraApplication.createHostOptions = createHostOptions;

        // Before the first call into SDL, which is the next line.
        NativeLibrary.SetDllImportResolver(typeof(SDL3).Assembly, resolveSdl);

        SDL_RunApp(0, null, &main, IntPtr.Zero);
    }

    /// <summary>
    /// SDL is embedded as a framework under <c>@rpath</c>, where nothing looks for it by name.
    /// </summary>
    private static IntPtr resolveSdl(string name, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath) =>
        name == "SDL3" && NativeLibrary.TryLoad("@rpath/SDL3.framework/SDL3", assembly, searchPath, out IntPtr handle) ? handle : IntPtr.Zero;

    /// <summary>
    /// SDL's main function: called on the main thread once UIKit has launched, while the launch screen still shows.
    /// The host's loop pumps UIKit's run loop from here on.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int main(int argc, byte** argv)
    {
        using (var host = new IOSAppHost(appName, createHostOptions?.Invoke()))
        {
            // The process starts in '/', which the app can't write to, so relative paths go to its own data instead.
            Environment.CurrentDirectory = host.DataDirectory;

            using (var app = createApp!())
                host.Run(app);
        }

        return 0;
    }
}
