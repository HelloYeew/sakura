// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Sakura.Framework.Configurations;
using Sakura.Framework.Development;
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

    // TODO: This "should" not be accessible from outside the framework.
    public Storage FrameworkStorage { get; private set; } = new EmbeddedResourceStorage(typeof(AppHost).Assembly, "Sakura.Framework.Resources");

    public FrameworkConfigManager FrameworkConfigManager { get; private set; }

    /// <summary>
    /// Determines whether the host should run without a window or renderer.
    /// Override this in a headless host implementation.
    /// </summary>
    protected virtual bool IsHeadless => false;

    [NotNull]
    public HostOptions Options { get; private set; }

    public string Name { get; }

    /// <summary>
    /// The assembly containing the application's embedded resources.
    /// Defaults to the entry assembly.
    /// </summary>
    public virtual Assembly ResourceAssembly => Assembly.GetEntryAssembly();

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
    /// An unhandled exception has occurred. Return true to ignore and continue running.
    /// </summary>
    public event Func<Exception, bool> ExceptionThrown;

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
    /// Create the application window for the host.
    /// </summary>
    /// <returns>An instance of <see cref="IWindow"/> that represents the application window.</returns>
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

    /// <summary>
    /// The target update rate in Hz, based on the current frame limiter setting.
    /// </summary>
    private double targetUpdateHz = 60;

    private void updateTargetUpdateHz()
    {
        targetUpdateHz = getTargetUpdateHz();
        Logger.Debug($"Target update rate is now {targetUpdateHz} Hz");
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
            Logger.AppIdentifier = Name;
            Logger.VersionIdentifier = RuntimeInfo.EntryAssembly.GetName().Version?.ToString() ?? Logger.VersionIdentifier;

            Logger.Initialize();

            if (!host_running_mutex.Wait(10000))
            {
                Logger.Error("Another instance of the application is already running.");
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += unhandledExceptionHandler;
            TaskScheduler.UnobservedTaskException += unobservedTaskExceptionHandler;

            Storage = app.CreateStorage(this, GetDefaultAppStorage());
            app.SetHost(this);

            SetupForRun();

            Console.CancelKeyPress += (_, _) =>
            {
                // Try to gracefully stop the application without missing any events before exiting.
                Window.Close();
                Dispose(true);
                exitEvent.Set();
            };

            FrameLimiter = FrameworkConfigManager.Get<FrameSync>(FrameworkSetting.FrameLimiter);
            updateTargetUpdateHz();
            FrameLimiter.ValueChanged += e =>
            {
                updateTargetUpdateHz();
                Logger.Verbose($"Frame limiter changed from {e.OldValue} to {e.NewValue}");
            };

            executionState = ExecutionState.Running;

            if (!IsHeadless)
            {
                FrameLimiter.ValueChanged += onFrameLimiterChanged;

                Window = CreateWindow();
                Window.Title = Options.FriendlyAppName;
                Window.ApplicationName = Name;

                Window.OnKeyDown += OnKeyDown;
                Window.OnKeyUp += OnKeyUp;
                Window.OnMouseDown += OnMouseDown;
                Window.OnMouseUp += OnMouseUp;
                Window.OnMouseMove += OnMouseMove;
                Window.OnScroll += OnScroll;
                Window.OnDragDropFile += onDragDropFile;
                Window.OnDragDropText += onDragDropText;
                Window.Resized += onResize;
                Window.FocusLost += updateTargetUpdateHz;
                Window.FocusGained += updateTargetUpdateHz;
                Window.Minimized += updateTargetUpdateHz;
                Window.Restored += updateTargetUpdateHz;
                Window.DisplayChanged += _ => updateTargetUpdateHz();
                Window.RenderRequested += () =>
                {
                    if (executionState == ExecutionState.Running)
                    {
                        // Force an update and draw when requested
                        // during the resize operation since the loop is blocked.
                        app?.Update();
                        if (!IsHeadless)
                            PerformDraw();
                    }
                };

                Window.Initialize();
                Window.Create();

                onFrameLimiterChanged(new ValueChangedEvent<FrameSync>(default, FrameLimiter.Value));

                SetupRenderer();
            }

            Renderer?.SetRoot(this.app);

            onResize(Window.Width, Window.Height);

            AppClock = new Clock(true);

            this.app.Clock = AppClock;

            this.app.Load();
            this.app.LoadComplete();

            lastUpdateTime = AppClock.CurrentTime;
            gameLoopStopwatch.Start();

            var spinWait = new SpinWait();

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

                    // 20251029: PerformSoundUpdate is removed since the AudioManager is updated in App.Update().
                    // Will introduce it again when want multi-threaded audio update.

                    PerformUpdate();

                    if (!IsHeadless)
                        PerformDraw();

                    if (FrameLimiter.Value != FrameSync.VSync && FrameLimiter.Value != FrameSync.Unlimited)
                    {
                        // In non-VSync or unlimited modes, we need to limit the loop ourselves to the target frame rate.
                        if (targetUpdateHz > 0)
                        {
                            double targetFrameTime = 1000.0 / targetUpdateHz;

                            // A simple, busy-wait loop for precision.
                            // For better CPU usage, a hybrid Thread.Sleep/SpinWait would be better,
                            // but this is the most straightforward fix to ensure accuracy.
                            // Logger.LogPrint($"Spinning... (elapsed: {gameLoopStopwatch.Elapsed.TotalMilliseconds - frameStartTime:F2}ms, target: {targetFrameTime:F2}ms)");
                            while (gameLoopStopwatch.Elapsed.TotalMilliseconds - frameStartTime < targetFrameTime)
                            {
                                spinWait.SpinOnce();
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
        if (!e.IsRepeat && e.Key == Key.F12 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            Storage.OpenFileExternally("");
        }
    }

    protected virtual void OnKeyUp(KeyEvent e)
    {
        app?.OnKeyUp(e);
    }

    private void onResize(int logicalWidth, int logicalHeight)
    {
        int physicalWidth = logicalWidth;
        int physicalHeight = logicalHeight;
        if (Window is SDLWindow sdlWindow)
            sdlWindow.GetPhysicalSize(out physicalWidth, out physicalHeight);
        Renderer?.Resize(physicalWidth, physicalHeight, logicalWidth, logicalHeight);
        if (app != null) app.Size = new Vector2(logicalWidth, logicalHeight);
    }

    private void OnMouseDown(MouseButtonEvent e) => app?.OnMouseDown(e);
    private void OnMouseUp(MouseButtonEvent e) => app?.OnMouseUp(e);
    private void OnMouseMove(MouseEvent e) => app?.OnMouseMove(e);
    private void OnScroll(ScrollEvent e) => app?.OnScroll(e);
    private void onDragDropFile(DragDropFileEvent e) => app?.OnDragDropFile(e);
    private void onDragDropText(DragDropTextEvent e) => app?.OnDragDropText(e);

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
        if (drawable is FpsGraph)
            return;

        builder.AppendLine($"{indent}- {drawable.GetDisplayName()}");
        if (drawable is Container)
            builder.AppendLine($"{indent}  AutoSizeAxes: {((Container)drawable).AutoSizeAxes}");
        builder.AppendLine($"{indent}  Size: {drawable.Size} (DrawSize: {drawable.DrawSize}, RelativeSize: {drawable.RelativeSizeAxes})");
        builder.AppendLine($"{indent}  Position (Relative): {drawable.Position} (Anchor: {drawable.Anchor}, Origin: {drawable.Origin}, RelativePosition: {drawable.RelativePositionAxes})");
        builder.AppendLine($"{indent}  DrawRectangle (Absolute): {drawable.DrawRectangle}");
        builder.AppendLine($"{indent}  Alpha: {drawable.Alpha} (DrawAlpha: {drawable.DrawAlpha})");
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
        if (targetUpdateHz == 0) // Unlimited mode
        {
            // In unlimited mode, we just update once per loop iteration.
            app?.Update();
            lastUpdateTime = AppClock.CurrentTime;
            return;
        }

        double timeStep = 1000.0 / targetUpdateHz;

        // Run as many updates as needed to catch up with the current time.
        while (AppClock.CurrentTime - lastUpdateTime >= timeStep)
        {
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

        if (Window != null && !Window.IsActive)
            return 60;

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
        abortExecutionFromException(exception, args.IsTerminating);
    }

    private void unobservedTaskExceptionHandler(object sender, UnobservedTaskExceptionEventArgs args)
    {
        var exception = args.Exception.AsSingular();
        Logger.Error("An unobserved task exception occurred in the application.", exception);
        if (DebugUtils.IsNUnitRunning)
            abortExecutionFromException(exception, false);
    }

    private void abortExecutionFromException(Exception exception, bool isTerminating)
    {
        // ignore if consumer wishes to handle the exception themselves
        if (ExceptionThrown?.Invoke(exception) == true) return;

        if (isTerminating)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException -= unhandledExceptionHandler;
        TaskScheduler.UnobservedTaskException -= unobservedTaskExceptionHandler;

        Logger.Shutdown();

        var captured = ExceptionDispatchInfo.Capture(exception);

        captured.Throw();
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
