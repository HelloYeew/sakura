// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Sakura.Framework.Extensions.ExceptionExtensions;
using Sakura.Framework.Extensions.IEnumerableExtensions;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Platform;

public abstract class AppHost : IDisposable
{
    public IWindow Window { get; private set; }
    public IRenderer Renderer { get; private set; }

    [NotNull]
    public HostOptions Options { get; private set; }

    public string Name { get; }

    private ExecutionState executionState = ExecutionState.Idle;

    protected AppHost([NotNull] string appName, [CanBeNull] HostOptions options = null)
    {
        Options = options ?? new HostOptions();

        if (string.IsNullOrEmpty(Options.FriendlyAppName))
        {
            Options.FriendlyAppName = $@"Sakura framework (running {appName})";
        }

        Name = appName;
    }

    /// <summary>
    /// Return a <see cref="Storage"/> for the specified path.
    /// </summary>
    /// <param name="path">The absolute path to the storage.</param>
    /// <returns>The absolute path to use as root to create a <see cref="Storage"/>.</returns>
    public abstract Storage GetStorage(string path);

    /// <summary>
    /// The main storage for the application.
    /// </summary>
    public Storage Storage { get; protected set; }

    /// <summary>
    /// Find the default <see cref="Storage"/> for the application to be used.
    /// </summary>
    /// <returns></returns>
    protected virtual Storage GetDefaultAppStorage()
    {
        foreach (string path in UserStoragePaths)
        {
            var storage = GetStorage(path);

            // If an existing data directory exists, use that immediately.
            if (storage.ExistsDirectory(Name))
                return storage.GetStorageForDirectory(Name);
        }

        // Create a new directory for the application if it does not exist.
        foreach (string path in UserStoragePaths)
        {
            try
            {
                return GetStorage(path).GetStorageForDirectory(Name);
            }
            catch
            {
                // Failed on creation
            }
        }

        throw new InvalidOperationException("No valid user storage path could be resolved for the application.");
    }

    /// <summary>
    /// All valid user storage paths in order of usage priority.
    /// </summary>
    public virtual IEnumerable<string> UserStoragePaths
        // This is common to _most_ operating systems, with some specific ones overriding this value where a better option exists.
        => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create).Yield();

    /// <summary>
    /// Request to open a file externally using the system's default application for that file type if available.
    /// </summary>
    /// <param name="filename">The full path to the file to open.</param>
    /// <returns>Whether the file was successfully opened.</returns>
    public abstract bool OpenFileExternally(string filename);

    /// <summary>
    /// Present a file externally on the platform's native file browser and will be highlight the file if possible.
    /// </summary>
    /// <param name="filename">The full path to the file to present.</param>
    /// <returns>Whether the file was successfully presented.</returns>
    public abstract bool PresentFileExternally(string filename);

    /// <summary>
    /// Open a URL externally using the system's default browser or application for that URL type.
    /// </summary>
    /// <param name="url"> The URL to open.</param>
    public abstract void OpenUrlExternally(string url);

    /// <summary>
    /// Create the game window for the host.
    /// </summary>
    /// <returns>An instance of <see cref="IWindow"/> that represents the game window.</returns>
    protected abstract IWindow CreateWindow();

    /// <summary>
    /// Create the renderer for the host.
    /// </summary>
    /// <returns>An instance of <see cref="IRenderer"/> that represents the renderer.</returns>
    protected abstract IRenderer CreateRenderer();

    private static readonly SemaphoreSlim host_running_mutex = new SemaphoreSlim(1);

    protected virtual void SetupForRun()
    {
        Logger.Storage = Storage.GetStorageForDirectory("logs");
    }

    public void Run(App app)
    {
        if (RuntimeInfo.IsDesktop)
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }

        try
        {
            if (!host_running_mutex.Wait(10000))
            {
                Logger.Error("Another instance of the application is already running.");
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += unhandledExceptionHandler;
            TaskScheduler.UnobservedTaskException += unobservedTaskExceptionHandler;

            Storage = app.CreateStorage(this, GetDefaultAppStorage());

            Logger.AppIdentifier = Name;
            Logger.VersionIdentifier = RuntimeInfo.EntryAssembly.GetName().Version?.ToString() ?? Logger.VersionIdentifier;

            SetupForRun();

            executionState = ExecutionState.Running;

            Logger.Initialize();

            // TODO: Testing purpose only, will remove later.
            Logger.Verbose($"Starting {Options.FriendlyAppName}...");

            Window = CreateWindow();
            Window.Title = Options.FriendlyAppName;
            Window.Initialize();
            Window.Create();

            SetupRenderer();

            Window.Run();

            try
            {
                if (Window != null)
                {
                    // Update window event
                }
                else
                {
                    while (executionState != ExecutionState.Stopping)
                    {
                        // Update window event
                    }
                }
            }
            catch (OutOfMemoryException)
            {
            }
        }
        finally
        {
            host_running_mutex.Release();
        }
    }

    protected virtual void SetupRenderer()
    {
        Renderer = CreateRenderer();
        Renderer.Initialize(Window.GraphicsSurface);

        Window.Update += () =>
        {
            Renderer.Clear();
            Renderer.StartFrame();
            Renderer.Draw();
        };
    }

    private void unhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args)
    {
        var exception = (Exception)args.ExceptionObject;
        Logger.Error("An unhandled exception occurred in the application.", exception);
        // TODO: abort execution from exception
    }

    private void unobservedTaskExceptionHandler(object sender, UnobservedTaskExceptionEventArgs args)
    {
        var exception = args.Exception.AsSingular();
        Logger.Error("An unobserved task exception occurred in the application.", exception);
        // TODO: abort execution from exception
    }

    private bool isDisposed;

    protected virtual void Dispose(bool disposing)
    {
        if (isDisposed)
            return;

        isDisposed = true;

        AppDomain.CurrentDomain.UnhandledException -= unhandledExceptionHandler;
        TaskScheduler.UnobservedTaskException -= unobservedTaskExceptionHandler;

        Logger.Shutdown();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
