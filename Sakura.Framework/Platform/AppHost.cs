// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Sakura.Framework.Configurations;
using Sakura.Framework.Extensions.ExceptionExtensions;
using Sakura.Framework.Extensions.IEnumerableExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Platform;

public abstract class AppHost : IDisposable
{
    public IWindow Window { get; private set; }
    public IRenderer Renderer { get; private set; }

    private App app;

    public Reactive<FrameSync> FrameLimiter { get; set; }
    protected internal IClock AppClock { get; private set; }
    private readonly ThrottledFrameClock inputClock = new ThrottledFrameClock(1000);
    private readonly ThrottledFrameClock soundClock = new ThrottledFrameClock(1000);
    private double lastUpdateTime;
    private readonly Stopwatch gameLoopStopwatch = new Stopwatch();

    public FrameworkConfigManager FrameworkConfigManager { get; private set; }

    private FpsGraph FpsGraph;

    private Reactive<bool> showFpsGraph = new ReactiveBool();

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

        FrameworkConfigManager = new FrameworkConfigManager(Storage);
        FrameworkConfigManager.Load();
    }

    protected virtual void LoadFrameworkDrawable()
    {
        showFpsGraph = FrameworkConfigManager.Get(FrameworkSetting.ShowFpsGraph, false);
        app.Add(FpsGraph = new FpsGraph(AppClock)
        {
            Depth = float.MaxValue
        });
        if (!showFpsGraph)
            FpsGraph.Hide();
        showFpsGraph.ValueChanged += value =>
        {
            Logger.LogPrint($"ShowFpsGraph changed to {value.NewValue}");
            if (value.NewValue)
                FpsGraph.Show();
            else
                FpsGraph.Hide();
        };
    }

    public void Run(App app)
    {
        this.app = app;

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
            app.SetHost(this);

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

            FrameLimiter = FrameworkConfigManager.Get<FrameSync>(FrameworkSetting.FrameLimiter);
            FrameLimiter.ValueChanged += e => Logger.Verbose($"Frame limiter changed from {e.OldValue} to {e.NewValue}");

            executionState = ExecutionState.Running;

            if (!IsHeadless)
            {
                FrameLimiter.ValueChanged += onFrameLimiterChanged;

                Window = CreateWindow();
                Window.Title = Options.FriendlyAppName;

                Window.OnKeyDown += OnKeyDown;
                Window.OnKeyUp += OnKeyUp;
                Window.OnMouseDown += OnMouseDown;
                Window.OnMouseUp += OnMouseUp;
                Window.OnMouseMove += OnMouseMove;
                Window.OnScroll += OnScroll;
                Window.Resized += onResize;

                Window.Initialize();
                Window.Create();

                onFrameLimiterChanged(new ValueChangedEvent<FrameSync>(default, FrameLimiter.Value));

                SetupRenderer();
            }

            Renderer?.SetRoot(this.app);

            // Set initial size to current window size.
            Window.GetDrawableSize(out int initialWidth, out int initialHeight);
            onResize(initialWidth, initialHeight);

            AppClock = new Clock(true);

            this.app.Load();
            LoadFrameworkDrawable();
            this.app.LoadComplete();

            lastUpdateTime = AppClock.CurrentTime;
            gameLoopStopwatch.Start();

            try
            {
                while (executionState == ExecutionState.Running)
                {
                    double frameStartTime = gameLoopStopwatch.Elapsed.TotalMilliseconds;
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

                    if (FrameLimiter.Value != FrameSync.VSync && FrameLimiter.Value != FrameSync.Unlimited)
                    {
                        // In non-VSync or unlimited modes, we need to limit the loop ourselves to the target frame rate.
                        double targetHz = getTargetUpdateHz();
                        if (targetHz > 0)
                        {
                            double targetFrameTime = 1000.0 / targetHz;
                            // Logger.LogPrint("targetFrameTime: " + targetFrameTime);

                            // A simple, busy-wait loop for precision.
                            // For better CPU usage, a hybrid Thread.Sleep/SpinWait would be better,
                            // but this is the most straightforward fix to ensure accuracy.
                            var spin = new SpinWait();
                            // Logger.LogPrint($"Spinning... (elapsed: {gameLoopStopwatch.Elapsed.TotalMilliseconds - frameStartTime:F2}ms, target: {targetFrameTime:F2}ms)");
                            while (gameLoopStopwatch.Elapsed.TotalMilliseconds - frameStartTime < targetFrameTime)
                            {
                                spin.SpinOnce();
                            }
                        }
                    }
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

    protected virtual void OnKeyDown(KeyEvent e)
    {
        app?.OnKeyDown(e);

        // Global hotkey
        // TODO: Global hotkey should be in list (don't have to be able to change since it's framework level)
        if (!e.IsRepeat && e.Key == Key.F10 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            int currentValue = (int)FrameLimiter.Value;
            int nextValue = (currentValue + 1) % Enum.GetValues(typeof(FrameSync)).Length;
            FrameLimiter.Value = (FrameSync)nextValue;
        }
        if (!e.IsRepeat && e.Key == Key.F1)
        {
            var builder = new StringBuilder();
            builder.AppendLine("--- HIERARCHY DUMP ---");
            PrintHierarchy(app, builder);
            Logger.Log(builder.ToString());
            Logger.Log("Hierarchy dumped to console. Press F1 to dump again.");
        }
        if (!e.IsRepeat && e.Key == Key.F11 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            showFpsGraph.Value = !showFpsGraph.Value;
        }
        if (!e.IsRepeat && e.Key == Key.F12 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            Storage.OpenFileExternally("");
        }
    }

    protected virtual void OnKeyUp(KeyEvent e)
    {
        app?.OnKeyUp(e);
    }

    private void onResize(int width, int height)
    {
        Renderer?.Resize(width, height);
        if (app != null) app.Size = new Vector2(width, height);
    }

    private void OnMouseDown(MouseButtonEvent e) => app?.OnMouseDown(e);
    private void OnMouseUp(MouseButtonEvent e) => app?.OnMouseUp(e);
    private void OnMouseMove(MouseEvent e) => app?.OnMouseMove(e);
    private void OnScroll(ScrollEvent e) => app?.OnScroll(e);

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

    public void PrintHierarchy(Drawable drawable, StringBuilder builder, string indent = "")
    {
        builder.AppendLine($"{indent}- {drawable.GetType().Name}");
        builder.AppendLine($"{indent}  Size: {drawable.Size}");
        builder.AppendLine($"{indent}  Position (Relative): {drawable.Position}");
        builder.AppendLine($"{indent}  DrawRectangle (Absolute): {drawable.DrawRectangle}");
        builder.AppendLine($"{indent}  ModelMatrix: {drawable.ModelMatrix}");

        if (drawable is Container container)
        {
            builder.AppendLine($"{indent}  Children ({container.Children.Count}):");
            foreach (var child in container.Children)
            {
                PrintHierarchy(child, builder, indent + "  ");
            }
        }
    }

    /// <summary>
    /// This method is called at a fixed 1000Hz for precise input handling.
    /// </summary>
    protected virtual void PerformInput()
    {
        // Logger.LogPrint($"Input tick. Time: {AppClock.CurrentTime:F2}ms (delta: {AppClock.ElapsedFrameTime:F2}ms, frame: {AppClock.FramesPerSecond})");
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
            app?.Update();
            lastUpdateTime = AppClock.CurrentTime;
            return;
        }

        double timeStep = 1000.0 / targetHz;

        // Run as many updates as needed to catch up with the current time.
        while (AppClock.CurrentTime - lastUpdateTime >= timeStep)
        {
            // TODO: This is main loop here, might want to pass timeStep to
            // The update that happened in the drawable need to be aware of the timeStep.
            app?.Update();
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
