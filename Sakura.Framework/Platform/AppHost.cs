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
using Sakura.Framework.Reactive;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Platform;

public abstract class AppHost : IDisposable
{
    public IWindow Window { get; private set; }
    public IRenderer Renderer { get; private set; }

    public Reactive<FrameSync> FrameLimiter { get; protected set; }
    protected IClock AppClock { get; private set; }
    private readonly ThrottledFrameClock inputClock = new ThrottledFrameClock(1000);
    private readonly ThrottledFrameClock soundClock = new ThrottledFrameClock(1000);
    private double lastUpdateTime;

    /// <summary>
    /// Determines whether the host should run without a window or renderer.
    /// Override this in a headless host implementation.
    /// </summary>
    protected virtual bool IsHeadless => false;

    [NotNull]
    public HostOptions Options { get; private set; }

    public string Name { get; }

    private ExecutionState executionState = ExecutionState.Idle;

    private readonly ManualResetEventSlim exitEvent = new ManualResetEventSlim(false);

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

            Logger.Initialize();

            SetupForRun();

            Console.CancelKeyPress += (_, _) =>
            {
                // Try to gracefully stop the application without missing any events before exiting.
                Window.Close();
                Dispose(true);
                exitEvent.Set();
            };

            FrameLimiter = new Reactive<FrameSync>(FrameSync.Unlimited);
            FrameLimiter.ValueChanged += onFrameLimiterChanged;

            executionState = ExecutionState.Running;

            if (!IsHeadless)
            {
                FrameLimiter.ValueChanged += onFrameLimiterChanged;

                Window = CreateWindow();
                Window.Title = Options.FriendlyAppName;
                Window.Initialize();
                Window.Create();

                onFrameLimiterChanged(new ValueChangedEvent<FrameSync>(default, FrameLimiter.Value));

                SetupRenderer();
            }

            AppClock = new Clock(true);
            lastUpdateTime = AppClock.CurrentTime;

            try
            {
                while (executionState == ExecutionState.Running)
                {
                    AppClock.Update();
                    Window?.PollEvents();

                    if (Window?.IsExiting == true)
                        Exit();

                    if (inputClock.Process(AppClock.CurrentTime))
                        PerformInput();

                    if (soundClock.Process(AppClock.CurrentTime))
                        PerformSoundUpdate();

                    PerformUpdate();

                    if (!IsHeadless)
                        PerformDraw();
                }
            }
            catch (OutOfMemoryException)
            {
            }
        }
        catch (Exception ex)
        {
            Logger.Error("An unhandled exception occurred during runtime.", ex);
        }
        finally
        {
            Dispose(true);
            host_running_mutex.Release();
        }
    }

    /// <summary>
    /// Requests the host to exit gracefully.
    /// </summary>
    public virtual void Exit()
    {
        executionState = ExecutionState.Stopping;
    }

    protected virtual void SetupRenderer()
    {
        Renderer = CreateRenderer();
        Renderer.Initialize(Window.GraphicsSurface);
    }

    private void onFrameLimiterChanged(ValueChangedEvent<FrameSync> e)
    {
        // VSync need to be set on window, other frame limiters are handled in the clock.
        Window?.SetVSync(e.NewValue == FrameSync.VSync);
    }

    /// <summary>
    /// This method is called at a fixed 1000Hz for precise input handling.
    /// </summary>
    protected virtual void PerformInput()
    {
        Logger.LogPrint($"Input tick. Time: {AppClock.CurrentTime:F2}ms (delta: {AppClock.ElapsedFrameTime:F2}ms, frame: {AppClock.FramesPerSecond})");
    }

    /// <summary>
    /// This method is called at a fixed 1000Hz for precise audio processing.
    /// </summary>
    protected virtual void PerformSoundUpdate() { }

    /// <summary>
    /// This method runs the main game logic updates. It uses a fixed-step approach
    /// to ensure updates are deterministic and independent of the draw rate.
    /// </summary>
    protected virtual void PerformUpdate()
    {
        double targetHz = getTargetUpdateHz();

        if (targetHz == 0) // Unlimited mode
        {
            // In unlimited mode, we just update once per loop iteration.
            // TODO: might want to pass AppClock.ElapsedFrameTime to your update logic here.
            lastUpdateTime = AppClock.CurrentTime;
            return;
        }

        double timeStep = 1000.0 / targetHz;

        // Run as many updates as needed to catch up with the current time.
        while (AppClock.CurrentTime - lastUpdateTime >= timeStep)
        {
            // TODO: This is main loop here, might want to pass timeStep to
            // The update that happened in the drawable need to be aware of the timeStep.

            lastUpdateTime += timeStep;
        }
    }

    /// <summary>
    /// This method handles all rendering. It's typically called once per loop iteration,
    /// and its frequency is limited by VSync or by sleeping in unlimited mode.
    /// </summary>
    protected virtual void PerformDraw()
    {
        Renderer?.Clear();
        Renderer?.StartFrame();
        Renderer?.Draw(AppClock);
        Window?.SwapBuffers();
    }

    /// <summary>
    /// Get the target update rate in Hz based on the current frame limiter setting.
    /// </summary>
    /// <returns>The target update rate in Hz, or 0 for unlimited.</returns>
    private double getTargetUpdateHz()
    {
        // In headless mode, default to a sensible refresh rate for update calculations.
        double refreshRate = Window?.DisplayHz > 0 ? Window.DisplayHz : 60;

        switch (FrameLimiter.Value)
        {
            case FrameSync.VSync:
                return refreshRate;
            case FrameSync.Limit2x:
                return refreshRate * 2;
            case FrameSync.Limit4x:
                return refreshRate * 4;
            case FrameSync.Limit8x:
                return refreshRate * 8;
            case FrameSync.Unlimited:
                return 0; // Special value for unlimited
            default:
                return refreshRate;
        }
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

        executionState = ExecutionState.Stopping;

        isDisposed = true;

        AppDomain.CurrentDomain.UnhandledException -= unhandledExceptionHandler;
        TaskScheduler.UnobservedTaskException -= unobservedTaskExceptionHandler;

        Logger.Shutdown();

        executionState = ExecutionState.Stopped;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
