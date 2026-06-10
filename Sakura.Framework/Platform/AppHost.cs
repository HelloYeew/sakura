// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Sakura.Framework.Configurations;
using Sakura.Framework.Development;
using Sakura.Framework.Extensions.ExceptionExtensions;
using Sakura.Framework.Extensions.IEnumerableExtensions;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Sakura.Framework.Statistic;
using Sakura.Framework.Threading;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Platform;

public abstract class AppHost : IDisposable
{
    public IWindow Window { get; private set; }
    public IRenderer Renderer { get; private set; }

    private App app;

    public Reactive<FrameSync> FrameLimiter { get; set; }
    public Reactive<ExecutionMode> ExecutionMode { get; set; }
    protected internal IClock UpdateClock { get; private set; }
    protected internal IClock DrawClock { get; private set; }
    protected internal IClock AudioClock { get; private set; }
    public IClock InputClock { get; private set; }
    private readonly ThrottledFrameClock soundClock = new ThrottledFrameClock(1000);
    private double lastUpdateTime;
    private readonly Stopwatch appLoopStopwatch = new Stopwatch();
    private readonly ConcurrentQueue<Action> inputQueue = new ConcurrentQueue<Action>();
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    private readonly FrameBufferManager frameBufferManager = new FrameBufferManager();
    private readonly DrawNode[] rootDrawNodes = new DrawNode[3];
    private DrawNode currentFrameDrawNode;

    private ThreadRunner threadRunner;
    private AppThread updateThread;
    private AppThread drawThread;
    private AppThread audioThread;

    // TODO: This "should" not be accessible from outside the framework.
    public Storage FrameworkStorage { get; private set; } = new EmbeddedResourceStorage(typeof(AppHost).Assembly, "Sakura.Framework.Resources");

    public FrameworkConfigManager FrameworkConfigManager { get; private set; }

    /// <summary>
    /// Determines whether the host should run without a window or renderer.
    /// Override this in a headless host implementation.
    /// </summary>
    public virtual bool IsHeadless => false;

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

    /// <summary>
    /// Called between <see cref="IWindow.Initialize"/> and <see cref="IWindow.Create"/> to
    /// configure the window for the chosen graphics API (e.g. call <see cref="SDLWindow.SetGraphicsApi"/>).
    /// Override in platform-specific hosts. Default implementation is nothing to do.
    /// </summary>
    protected virtual void PrepareWindowForRenderer(IWindow window) { }

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
        // if the ExecutionMode hasn't been initialized yet, default to single-thread safety
        if (ExecutionMode != null && ExecutionMode.Value == Threading.ExecutionMode.MultiThread)
        {
            // multi-thread: update runs faster for better input/physics precision
            targetUpdateHz = getUpdateTargetHz();
        }
        else
        {
            // single-thread: lock Update strictly to 1:1 with draw to prevent the death spiral
            targetUpdateHz = getDrawTargetHz();
        }

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

            FrameLimiter.ValueChanged += onFrameLimiterChanged;

            Window = CreateWindow();
            Window.Title = Options.FriendlyAppName;
            Window.ApplicationName = Name;

            Window.OnKeyDown += e => inputQueue.Enqueue(() => OnKeyDown(e));
            Window.OnKeyUp += e => inputQueue.Enqueue(() => OnKeyUp(e));
            Window.OnMouseDown += e => inputQueue.Enqueue(() => OnMouseDown(e));
            Window.OnMouseUp += e => inputQueue.Enqueue(() => OnMouseUp(e));
            Window.OnMouseMove += e => inputQueue.Enqueue(() => OnMouseMove(e));
            Window.OnScroll += e => inputQueue.Enqueue(() => OnScroll(e));
            Window.OnDragDropFile += e => inputQueue.Enqueue(() => onDragDropFile(e));
            Window.OnDragDropText += e => inputQueue.Enqueue(() => onDragDropText(e));
            Window.OnTextInput += e => inputQueue.Enqueue(() => OnTextInput(e));
            Window.OnTextEditing += e => inputQueue.Enqueue(() => OnTextEditing(e));
            Window.Resized += (w, h) =>
            {
                Window.GetPhysicalSize(out int pw, out int ph);
                inputQueue.Enqueue(() => onResize(pw, ph, w, h));
            };
            Window.FocusLost += updateTargetUpdateHz;
            Window.FocusGained += updateTargetUpdateHz;
            Window.Minimized += updateTargetUpdateHz;
            Window.Restored += updateTargetUpdateHz;
            Window.DisplayChanged += _ => updateTargetUpdateHz();

            Window.RenderRequested += () =>
            {
                // make renderer still resize itself in single thread mode
                if (executionState == ExecutionState.Running && ExecutionMode.Value == Threading.ExecutionMode.SingleThread)
                {
                    threadRunner.RunSingleThreadedFrame();
                }
            };

            Window.Initialize();
            PrepareWindowForRenderer(Window);
            Window.Create();

            onFrameLimiterChanged(new ValueChangedEvent<FrameSync>(default, FrameLimiter.Value));

            SetupRenderer();

            Window.GetPhysicalSize(out int initialPhysicalWidth, out int initialPhysicalHeight);
            onResize(initialPhysicalWidth, initialPhysicalHeight, Window.Width, Window.Height);

            updateThread = new AppThread("UpdateThread", PerformUpdate, getUpdateTargetHz)
            {
                Priority = ThreadPriority.AboveNormal
            };
            drawThread = new AppThread("DrawThread", PerformDraw, getDrawTargetHz)
            {
                Priority = ThreadPriority.Normal
            };
            audioThread = new AppThread("AudioThread", PerformSoundUpdate, getAudioTargetHz)
            {
                Priority = ThreadPriority.Highest
            };

            drawThread.OnInitialize = () => Window.MakeCurrent();

            UpdateClock = updateThread.Clock;
            DrawClock = drawThread.Clock;
            AudioClock = audioThread.Clock;
            InputClock = new Clock(true);

            threadRunner = new ThreadRunner(updateThread, drawThread, audioThread);

            ExecutionMode = FrameworkConfigManager.Get<ExecutionMode>(FrameworkSetting.ExecutionMode);

            ExecutionMode.ValueChanged += e =>
            {
                mainThreadActions.Enqueue(() =>
                {
                    if (e.NewValue == Threading.ExecutionMode.MultiThread)
                    {
                        Window.ClearCurrent();
                        threadRunner.SetExecutionMode(Threading.ExecutionMode.MultiThread);
                    }
                    else
                    {
                        threadRunner.SetExecutionMode(Threading.ExecutionMode.SingleThread);
                        Window.MakeCurrent();
                    }

                    updateTargetUpdateHz();

                    Logger.Verbose($"Execution mode changed from {e.OldValue} to {e.NewValue}");
                });
            };

            this.app.Clock = UpdateClock;

            this.app.Load();
            this.app.LoadComplete();

            if (ExecutionMode.Value == Threading.ExecutionMode.MultiThread)
            {
                Window.ClearCurrent();
                threadRunner.SetExecutionMode(Threading.ExecutionMode.MultiThread);
            }

            lastUpdateTime = UpdateClock.CurrentTime;
            appLoopStopwatch.Start();

            long timestampFrequency = Stopwatch.Frequency;
            double msPerTick = 1000.0 / timestampFrequency;
            long nextMainFrameTime = Stopwatch.GetTimestamp();
            const double main_spin_guard_ms = 0.5;

            INativeSleep mainNativeSleep = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WindowsNativeSleep()
                : UnixNativeSleep.IsAvailable ? new UnixNativeSleep() : null;

            try
            {
                while (executionState == ExecutionState.Running)
                {
                    GlobalStatistics.Get<int>("GC", "Gen 0 Collections").Value = GC.CollectionCount(0);
                    GlobalStatistics.Get<int>("GC", "Gen 1 Collections").Value = GC.CollectionCount(1);
                    GlobalStatistics.Get<int>("GC", "Gen 2 Collections").Value = GC.CollectionCount(2);

                    while (mainThreadActions.TryDequeue(out var action))
                    {
                        action.Invoke();
                    }

                    Window?.PollEvents();

                    if (Window?.IsExiting == true)
                        Exit();

                    PerformInput();

                    if (ExecutionMode.Value == Threading.ExecutionMode.SingleThread)
                    {
                        threadRunner.RunSingleThreadedFrame();
                    }

                    // In single-threaded mode the main loop drives everything at the update rate.
                    // In multi-threaded mode it only drives input, so it follows the input target Hz
                    // (which throttles to 60 when the window is inactive, like the other threads).
                    double currentHz = ExecutionMode.Value == Threading.ExecutionMode.SingleThread
                        ? targetUpdateHz
                        : GetInputTargetHz();

                    if (currentHz > 0)
                    {
                        double targetMainFrameTimeMs = 1000.0 / currentHz;
                        long targetMainTicks = (long)(targetMainFrameTimeMs / msPerTick);
                        
                        nextMainFrameTime += targetMainTicks;

                        long now = Stopwatch.GetTimestamp();

                        if (now > nextMainFrameTime)
                            nextMainFrameTime = now;

                        double remainingMs = (nextMainFrameTime - now) * msPerTick;

                        // Coarse phase: give the CPU back to the OS for the bulk of the wait.
                        double sleepMs = remainingMs - main_spin_guard_ms;
                        if (sleepMs > 0)
                        {
                            var sleepSpan = TimeSpan.FromMilliseconds(sleepMs);
                            if (mainNativeSleep?.Sleep(sleepSpan) != true)
                                Thread.Sleep(sleepSpan);
                        }

                        // Fine phase: tight bounded spin (~main_spin_guard_ms) to land exactly on the
                        // deadline. Thread.SpinWait issues a CPU pause hint without yielding to sleep,
                        // which would overshoot a sub-millisecond deadline.
                        while (Stopwatch.GetTimestamp() < nextMainFrameTime)
                            Thread.SpinWait(1);
                    }
                    else
                    {
                        nextMainFrameTime = Stopwatch.GetTimestamp();
                        Thread.Sleep(0);
                    }
                }
            }
            catch (OutOfMemoryException ex)
            {
                Logger.Error("The application has run out of memory and needs to close.", ex);
            }
            finally
            {
                mainNativeSleep?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("An unhandled exception occurred during runtime.", ex);
            // Rethrow so process will exit with error code
            throw;
        }
        finally
        {
            threadRunner?.Dispose();
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

        if (!e.IsRepeat && e.Key == Key.F11 && (e.Modifiers & KeyModifiers.Control) == 0)
        {
            ScheduleToMainThread(() =>
            {
                switch (Window.WindowMode)
                {
                    case WindowMode.Windowed:
                        Window.WindowMode = WindowMode.Borderless;
                        break;
                    case WindowMode.Borderless:
                        if (RuntimeInfo.IsMacOS)
                            Window.WindowMode = WindowMode.Windowed;
                        else
                            Window.WindowMode = WindowMode.Fullscreen;
                        break;
                    case WindowMode.Fullscreen:
                        Window.WindowMode = WindowMode.Windowed;
                        break;
                }
            });
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

    private void onResize(int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight)
    {
        Renderer?.ScheduleToDrawThread(() => Renderer.Resize(physicalWidth, physicalHeight, logicalWidth, logicalHeight));
        if (app != null) app.Size = new Vector2(logicalWidth, logicalHeight);
    }

    private void OnMouseDown(MouseButtonEvent e) => app?.OnMouseDown(e);
    private void OnMouseUp(MouseButtonEvent e) => app?.OnMouseUp(e);
    private void OnMouseMove(MouseEvent e) => app?.OnMouseMove(e);
    private void OnScroll(ScrollEvent e) => app?.OnScroll(e);
    private void onDragDropFile(DragDropFileEvent e) => app?.OnDragDropFile(e);
    private void onDragDropText(DragDropTextEvent e) => app?.OnDragDropText(e);
    protected void OnTextInput(TextInputEvent e) => app?.OnTextInput(e);
    protected void OnTextEditing(TextEditingEvent e) => app?.OnTextEditing(e);

    protected virtual void SetupRenderer()
    {
        Renderer = CreateRenderer();
        Renderer.ShaderStorage = FrameworkStorage.GetStorageForDirectory("Shaders");
        InitializeGraphicsContext(Window, Renderer);
        Renderer.Initialize(Window.GraphicsSurface);
    }

    /// <summary>
    /// Initialises the backend graphics context on the window after the renderer is chosen.
    /// Called inside <see cref="SetupRenderer"/>. Override to perform backend-specific setup
    /// (e.g. creating the GL context or Metal surface). Default implementation is a no-op.
    /// </summary>
    protected virtual void InitializeGraphicsContext(IWindow window, IRenderer renderer) { }

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
        InputClock.Update();
    }

    /// <summary>
    /// Schedules an action to be safely executed on the OS Main User Interface Thread.
    /// </summary>
    public void ScheduleToMainThread(Action action)
    {
        if (action != null)
        {
            mainThreadActions.Enqueue(action);
        }
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
        while (inputQueue.TryDequeue(out var inputAction))
        {
            inputAction.Invoke();
        }

        GlobalStatistics.Get<double>("Host", "Uptime (ms)").Value = UpdateClock.CurrentTime;
        GlobalStatistics.Get<double>("Host", "Target Update Hz").Value = targetUpdateHz;

        if (targetUpdateHz == 0) // Unlimited mode
        {
            // In unlimited mode, we just update once per loop iteration.
            app?.UpdateSubTree();
            lastUpdateTime = UpdateClock.CurrentTime;
            int unlimitedUpdateIndex = frameBufferManager.GetUpdateIndex();
            rootDrawNodes[unlimitedUpdateIndex] = app?.GenerateDrawNodeSubtree(unlimitedUpdateIndex);
            frameBufferManager.FinishUpdate();
            return;
        }

        double timeStep = 1000.0 / targetUpdateHz;

        // Run as many updates as needed to catch up with the current time.
        while (UpdateClock.CurrentTime - lastUpdateTime >= timeStep)
        {
            // The update that happened in the drawable need to be aware of the timeStep.
            app?.UpdateSubTree();
            lastUpdateTime += timeStep;
        }

        int updateIndex = frameBufferManager.GetUpdateIndex();
        rootDrawNodes[updateIndex] = app?.GenerateDrawNodeSubtree(updateIndex);
        frameBufferManager.FinishUpdate();
    }

    /// <summary>
    /// This method handles all rendering. It's typically called once per loop iteration,
    /// and its frequency is limited by VSync or by sleeping in unlimited mode.
    /// </summary>
    protected virtual void PerformDraw()
    {
        GlobalStatistics.Get<int>("Drawables", "Drawn Last Frame").Value = 0;
        Renderer?.Clear();
        Renderer?.StartFrame();

        int drawIndex = frameBufferManager.GetDrawIndex();
        var currentFrameNode = rootDrawNodes[drawIndex];

        if (currentFrameNode != null)
        {
            Renderer?.SetRoot(currentFrameNode);
        }

        Renderer?.Draw(DrawClock);
        Window?.SwapBuffers();
    }

    private bool isMultiThread => ExecutionMode?.Value == Threading.ExecutionMode.MultiThread;
    internal double GetInputTargetHz() => isMultiThread ? getInputTargetHz() : targetUpdateHz;
    internal double GetAudioTargetHz() => isMultiThread ? getAudioTargetHz() : targetUpdateHz;
    internal double GetUpdateTargetHz() => isMultiThread ? getUpdateTargetHz() : targetUpdateHz;
    internal double GetDrawTargetHz() => isMultiThread ? getDrawTargetHz() : targetUpdateHz;

    // Input and audio run at 1000 Hz while the window is focused, and throttle to 60 Hz when it
    // loses focus — matching the update/draw threads (and osu!framework's inactive behaviour).
    private double getInputTargetHz() => Window != null && !Window.IsActive ? 60.0 : 1000.0;
    private double getAudioTargetHz() => Window != null && !Window.IsActive ? 60.0 : 1000.0;

    private double getDrawTargetHz()
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

    private double getUpdateTargetHz()
    {
        double drawHz = getDrawTargetHz();

        if (drawHz == 0)
            return Options.LimitUnlimitedUpdateRate ? 1000 : 0;

        if (Window != null && !Window.IsActive)
            return 60;

        double updateHz = drawHz * 2;

        return Options.LimitUnlimitedUpdateRate ? Math.Min(updateHz, 1000) : updateHz;
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

        AppDomain.CurrentDomain.UnhandledException -= unhandledExceptionHandler;
        TaskScheduler.UnobservedTaskException -= unobservedTaskExceptionHandler;

        Logger.Shutdown();

        if (isTerminating)
        {
            return;
        }

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
