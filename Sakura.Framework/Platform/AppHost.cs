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
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Sakura.Framework.Audio;
using Sakura.Framework.Configurations;
using Sakura.Framework.Development;
using Sakura.Framework.Extensions.ExceptionExtensions;
using Sakura.Framework.Extensions.IEnumerableExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Input;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform.Dialogs;
using Sakura.Framework.Reactive;
using Sakura.Framework.Statistic;
using Sakura.Framework.Threading;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Platform;

public abstract class AppHost : IDisposable
{
    private static readonly int frame_sync_value_count = Enum.GetValues<FrameSync>().Length;

    private static readonly double ms_per_tick = 1000.0 / Stopwatch.Frequency;

    private static readonly GlobalStatistic<double> stat_uptime = GlobalStatistics.Get<double>("Host", "Uptime", StatisticKind.Gauge, StatisticUnit.Milliseconds);

    /// <summary>
    /// How often the GC/texture statistics are refreshed on the main loop.
    /// </summary>
    private const double gc_statistics_interval_ms = 250;

    private long lastGCStatisticsTicks;
    private static readonly GlobalStatistic<double> stat_target_update_hz = GlobalStatistics.Get<double>("Host", "Target Update Rate", StatisticKind.Gauge, StatisticUnit.Hertz);
    private static readonly GlobalStatistic<int> stat_drawn_last_frame = GlobalStatistics.Get<int>("Drawables", "Drawn Last Frame", StatisticKind.PerFrame);

    public IWindow Window { get; private set; }
    public IRenderer Renderer { get; private set; }

    [CanBeNull]
    public IAudioManager AudioManager
    {
        get => audioManager;
        set
        {
            audioManager = value;

            // A manager made during a call starts out knowing it.
            if (value != null)
                value.OutputInterrupted.Value = audioInterrupted;
        }
    }

    [CanBeNull]
    private IAudioManager audioManager;

    // What the platform last said, on the update thread: whether something else has the audio output for now.
    private bool audioInterrupted;

    private App app;

    public Reactive<FrameSync> FrameLimiter { get; set; }
    public Reactive<ExecutionMode> ExecutionMode { get; set; }

    /// <summary>
    /// Whether the display stays on while the app runs, rather than dimming and sleeping on the system's timer. On
    /// for a game, which is often watched without being touched, and off for an application. Either can change it:
    /// an application while it plays a video, say, or a game only during play.
    /// </summary>
    public Reactive<bool> KeepScreenOn { get; }
    protected internal Clock UpdateClock { get; private set; }
    protected internal Clock DrawClock { get; private set; }
    protected internal Clock AudioClock { get; private set; }
    public Clock InputClock { get; private set; }

    /// <summary>
    /// Per-frame timings for the main loop (performance overlay labels "Input")
    /// </summary>
    public ThreadFrameStatistics InputFrameStatistics { get; } = new ThreadFrameStatistics();

    /// <summary>
    /// Per-frame timings for the update thread, or null before the host has started
    /// </summary>
    public ThreadFrameStatistics UpdateFrameStatistics => updateThread?.FrameStatistics;

    /// <summary>
    /// Per-frame timings for the draw thread, or null before the host has started
    /// </summary>
    public ThreadFrameStatistics DrawFrameStatistics => drawThread?.FrameStatistics;

    /// <summary>
    /// Per-frame timings for the audio thread, or null before the host has started
    /// </summary>
    public ThreadFrameStatistics AudioFrameStatistics => audioThread?.FrameStatistics;

    private readonly Stopwatch appLoopStopwatch = new Stopwatch();
    private readonly ConcurrentQueue<PendingInput> inputQueue = new ConcurrentQueue<PendingInput>();
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    /// <summary>
    /// Runs actions on the update thread. Thread-safe to add to from any thread; pumped once per
    /// update frame. Used to marshal asynchronous results (e.g. file dialog callbacks) back onto
    /// the update thread where drawables are safe to touch.
    /// </summary>
    private readonly Scheduler updateScheduler = new Scheduler();

    private enum PendingInputType : byte
    {
        KeyDown,
        KeyUp,
        MouseDown,
        MouseUp,
        MouseMove,
        TouchDown,
        TouchMove,
        TouchUp,
        Scroll,
        DragDropFile,
        DragDropText,
        TextInput,
        TextEditing,
        Resize,
        GamepadButtonDown,
        GamepadButtonUp,
        GamepadAxisMotion,
        GamepadConnected,
        GamepadDisconnected,
        ExitRequested,
        ReleaseHeldInput,
    }

    /// <summary>
    /// A queued window event as a plain struct. Events can arrive at up to 1000 Hz;
    /// queuing them as structs (instead of closure + delegate pairs) keeps the input
    /// path allocation-free.
    /// </summary>
    private struct PendingInput
    {
        public PendingInputType Type;
        public KeyEvent Key;
        public MouseButtonEvent MouseButton;
        public MouseEvent Mouse;
        public TouchEvent Touch;
        public ScrollEvent Scroll;
        public TextInputEvent TextInput;
        public TextEditingEvent TextEditing;
        public DragDropFileEvent DragDropFile;
        public DragDropTextEvent DragDropText;
        public int ResizePhysicalWidth;
        public int ResizePhysicalHeight;
        public int ResizeLogicalWidth;
        public int ResizeLogicalHeight;
        public GamepadButtonEvent GamepadButton;
        public GamepadAxisEvent GamepadAxis;
        public GamepadConnectedEvent GamepadConnected;
        public GamepadDisconnectedEvent GamepadDisconnected;
    }

    private void dispatchInput(in PendingInput input)
    {
        switch (input.Type)
        {
            case PendingInputType.KeyDown:
                OnKeyDown(input.Key);
                break;

            case PendingInputType.KeyUp:
                OnKeyUp(input.Key);
                break;

            case PendingInputType.MouseDown:
                // Dropped while a native file dialog is open: see isFileDialogOpen.
                if (!isFileDialogOpen)
                    OnMouseDown(input.MouseButton);
                break;

            case PendingInputType.MouseUp:
                // ignore when a native file dialog is open since the state got stuck
                if (!isFileDialogOpen || app?.InputManager.CurrentState.IsPressed(input.MouseButton.Button) == true)
                    OnMouseUp(input.MouseButton);
                break;

            case PendingInputType.MouseMove:
                if (!isFileDialogOpen)
                    OnMouseMove(input.Mouse);
                break;

            case PendingInputType.Scroll:
                if (!isFileDialogOpen)
                    OnScroll(input.Scroll);
                break;

            case PendingInputType.TouchDown:
                if (!isFileDialogOpen)
                    app?.OnTouchDown(input.Touch);
                break;

            case PendingInputType.TouchMove:
                if (!isFileDialogOpen)
                    app?.OnTouchMove(input.Touch);
                break;

            // Delivered even with a dialog open, so a finger that went down before it can't stay down for good.
            case PendingInputType.TouchUp:
                app?.OnTouchUp(input.Touch);
                break;

            case PendingInputType.DragDropFile:
                onDragDropFile(input.DragDropFile);
                break;

            case PendingInputType.DragDropText:
                onDragDropText(input.DragDropText);
                break;

            case PendingInputType.TextInput:
                OnTextInput(input.TextInput);
                break;

            case PendingInputType.TextEditing:
                OnTextEditing(input.TextEditing);
                break;

            case PendingInputType.Resize:
                onResize(input.ResizePhysicalWidth, input.ResizePhysicalHeight, input.ResizeLogicalWidth, input.ResizeLogicalHeight);
                break;

            case PendingInputType.GamepadButtonDown:
                OnGamepadButtonDown(input.GamepadButton);
                break;

            case PendingInputType.GamepadButtonUp:
                OnGamepadButtonUp(input.GamepadButton);
                break;

            case PendingInputType.GamepadAxisMotion:
                OnGamepadAxisMotion(input.GamepadAxis);
                break;

            case PendingInputType.GamepadConnected:
                OnGamepadConnected(input.GamepadConnected);
                break;

            case PendingInputType.GamepadDisconnected:
                OnGamepadDisconnected(input.GamepadDisconnected);
                break;

            case PendingInputType.ExitRequested:
                handleExitRequested();
                break;

            case PendingInputType.ReleaseHeldInput:
                app?.ReleaseHeldInput();
                break;
        }
    }

    private readonly FrameBufferManager frameBufferManager = new FrameBufferManager();
    private readonly DrawNode[] rootDrawNodes = new DrawNode[3];

    // When the update each of rootDrawNodes came from began, for the renderer to measure how long its frames take to
    // reach the screen from (IRenderer.SetFrameContentTime).
    private readonly long[] rootDrawNodeTimes = new long[3];
    private DrawNode currentFrameDrawNode;

    private ThreadRunner threadRunner;
    private AppThread updateThread;
    private AppThread drawThread;
    private AppThread audioThread;

    // TODO: This "should" not be accessible from outside the framework.
    public Storage FrameworkStorage { get => field ??= new EmbeddedResourceStorage(typeof(AppHost).Assembly, "Sakura.Framework.Resources"); private set; }

    public FrameworkConfigManager FrameworkConfigManager { get; private set; }

    /// <summary>
    /// Determines whether the host should run without a window or renderer.
    /// Override this in a headless host implementation.
    /// </summary>
    public virtual bool IsHeadless => false;

    /// <summary>
    /// The audio output's native format, where the platform knows it before a device is opened, or null to ask
    /// SDL. On Android SDL only reports a placeholder until a stream is open, so the audio device would open at a
    /// rate the output then has to resample from.
    /// </summary>
    public virtual NativeAudioOutput? NativeAudioOutput => null;

    /// <summary>
    /// The route the platform's own audio takes the sound along after the app's device buffer, where the platform
    /// says: the SDL audio backend adds its delay to the sound's and names the output by it. Null elsewhere.
    /// </summary>
    public virtual ISystemAudioRoute? SystemAudioRoute => null;

    /// <summary>
    /// The platform's performance services: the frame threads report their work through it, and an app can read how
    /// hot the device is running.
    /// </summary>
    public virtual IPlatformPerformance Performance { get; } = new NoPlatformPerformance();

    /// <summary>
    /// How many more bytes the app may use before the system ends it, or null where the platform doesn't say. A budget
    /// for how much to load ahead or cache, read when deciding rather than kept, since it moves with what else runs.
    /// </summary>
    public virtual long? AvailableMemory => null;

    [NotNull]
    public HostOptions Options { get; private set; }

    public string Name { get; }

    /// <summary>
    /// The assembly containing the application's embedded resources.
    /// Defaults to the entry assembly (<see cref="RuntimeInfo.EntryAssembly"/>).
    /// </summary>
    public virtual Assembly ResourceAssembly => RuntimeInfo.EntryAssembly;

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
        KeepScreenOn = new Reactive<bool>(Options.AppType == AppType.Game);
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

    /// <summary>
    /// Called when the renderer <see cref="CreateRenderer"/> returned failed to start, in creating the
    /// window for it or in setting it up. Returns whether to go on: the renderer is disposed, the window
    /// destroyed, and <see cref="CreateRenderer"/> called again, which must then return a different
    /// renderer or throw. Otherwise <paramref name="exception"/> ends startup, as the default does.
    /// </summary>
    protected virtual bool OnRendererFailed(Exception exception) => false;

    private static readonly SemaphoreSlim host_running_mutex = new SemaphoreSlim(1);

    private readonly object lifecycleLock = new object();
    private bool inBackground;

    /// <summary>
    /// Stops every per-frame thread and lets go of the window's surface, for an app leaving the screen. Runs on
    /// whichever thread the system reports it on, and is done by the time it returns, as the system needs.
    /// </summary>
    private void enterBackground()
    {
        lock (lifecycleLock)
        {
            if (inBackground || threadRunner == null)
                return;

            inBackground = true;

            threadRunner.Pause();

            (AudioManager as IStopsInBackground)?.EnterBackground();

            // Time spent in the background isn't the app's: the first frame back carries none of it.
            UpdateClock.Stop();
            DrawClock.Stop();
            AudioClock.Stop();

            Renderer?.ReleaseSurface();

            // From here the system may end the process without another word: every setting is saved, the app's too.
            ConfigManagers.FlushAll();

            Logger.Verbose("Entered the background: frames stopped and the window surface released.");

            // Long enough for what's queued, which is a few lines; the system allows a background move about five seconds.
            Logger.Flush(TimeSpan.FromMilliseconds(500));
        }
    }

    /// <summary>
    /// Raised on the update thread once the system has said it's short of memory: drop whatever can be made again, such
    /// as loaded-ahead content and caches of things not on screen. Android says so as an app goes to the background
    /// too, and the update thread is stopped there, so this comes once the app is back. By then the framework has
    /// compacted the managed heap and handed what was free back to the system. iOS says so while the app runs, and
    /// collects what the handlers let go of once they've run.
    /// </summary>
    public event Action LowMemory;

    /// <summary>
    /// Compacts the managed heap where the system said it's short of memory: on Android its UI thread, usually with
    /// the app in the background, which is when a big process is the first to be ended. A full collection stops every
    /// managed thread, but in the background they're stopped anyway.
    /// </summary>
    private void onLowMemory()
    {
        long before = GC.GetTotalMemory(false);

        // Before the collection, so it frees them.
        ImageRawData.TrimPool();

        if (OperatingSystem.IsIOS())
        {
            // iOS warns in the foreground, every thread running and maybe mid-game, where stopping them all to compact
            // the heap is a hitch the player sees. So the app drops what it can first, and a background collection
            // then frees it without stopping anything for long.
            updateScheduler.Add(() =>
            {
                LowMemory?.Invoke();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);

                Logger.Verbose($"Short of memory: the managed heap held {before / 1048576.0:0.#} MB; the app has dropped what it could, and a "
                               + "background collection is freeing it.");
            });

            return;
        }

        // Aggressive gives back the memory the collection frees rather than keeping it for the next allocations.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        Logger.Verbose($"Short of memory: the managed heap went from {before / 1048576.0:0.#} MB to {GC.GetTotalMemory(false) / 1048576.0:0.#} MB "
                       + $"({GC.GetGCMemoryInfo().TotalCommittedBytes / 1048576.0:0.#} MB still held from the system).");

        updateScheduler.Add(() => LowMemory?.Invoke());
    }

    /// <summary>
    /// Says the system has taken the audio output for something else for now, such as a call, or given it back. It
    /// reaches the app as <see cref="IAudioManager.OutputInterrupted"/>, on the update thread. Safe to call from any
    /// thread.
    /// </summary>
    protected void SetAudioInterrupted(bool interrupted) => updateScheduler.Add(() =>
    {
        audioInterrupted = interrupted;

        if (AudioManager != null)
            AudioManager.OutputInterrupted.Value = interrupted;
    });

    private void enterForeground()
    {
        lock (lifecycleLock)
        {
            if (!inBackground)
                return;

            inBackground = false;

            UpdateClock.Start();
            DrawClock.Start();
            AudioClock.Start();

            (AudioManager as IStopsInBackground)?.EnterForeground();

            threadRunner.Resume();

            Logger.Verbose("Back in the foreground: frames resumed.");
        }
    }

    /// <summary>
    /// The <see cref="Console.CancelKeyPress"/> handler, kept so it can be detached on disposal.
    /// </summary>
    private ConsoleCancelEventHandler? cancelKeyPressHandler;

    /// <summary>
    /// Where the logs are written. The app's storage, unless the platform keeps them apart from the data it backs up.
    /// </summary>
    protected virtual Storage GetLogStorage() => Storage.GetStorageForDirectory("logs");

    /// <summary>
    /// Where what can be made again is kept, such as compiled shaders. The app's storage, unless the platform has a
    /// place it clears when it's short of space and leaves out of backups.
    /// </summary>
    protected virtual Storage GetCacheStorage() => Storage.GetStorageForDirectory("cache");

    protected virtual void SetupForRun()
    {
        Logger.Storage = GetLogStorage();

        FrameworkConfigManager = new FrameworkConfigManager(Storage, Options.AppType);
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

        // Avoiding blocking collections lets the heap grow, which only a game's frame times are worth.
        if (RuntimeInfo.IsDesktop && Options.AppType == AppType.Game)
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }

        // A process started from Java (Android) has no managed entry point, so the app's assembly stands in
        // for it. Everything below reads it, the logger first.
        RuntimeInfo.EntryAssembly ??= app.GetType().Assembly;

        bool ownsHostRunning = false;

        try
        {
            Logger.AppIdentifier = Name;
            Logger.VersionIdentifier = RuntimeInfo.EntryAssembly.GetName().Version?.ToString() ?? Logger.VersionIdentifier;

            Logger.Initialize(DebugUtils.IsDebugBuild ? LogLevel.Debug : LogLevel.Verbose);

            if (!host_running_mutex.Wait(10000))
            {
                Logger.Error("Another instance of the application is already running.");
                return;
            }

            ownsHostRunning = true;

            AppDomain.CurrentDomain.UnhandledException += unhandledExceptionHandler;
            TaskScheduler.UnobservedTaskException += unobservedTaskExceptionHandler;

            Storage = app.CreateStorage(this, GetDefaultAppStorage());
            app.SetHost(this);

            SetupForRun();

            // Startup is a game's first loading screen: the system can raise the CPU for it.
            if (Options.AppType == AppType.Game)
                Performance.SetGameState(GameState.None, loading: true);

            // Mobile platforms have no console to press Ctrl+C in, and don't support subscribing to it.
            if (RuntimeInfo.IsDesktop)
            {
                cancelKeyPressHandler = (_, _) =>
                {
                    // Try to gracefully stop the application without missing any events before exiting.
                    Window.Close();
                    Dispose(true);
                    exitEvent.Set();
                };

                Console.CancelKeyPress += cancelKeyPressHandler;
            }

            FrameLimiter = FrameworkConfigManager.Get<FrameSync>(FrameworkSetting.FrameLimiter);
            updateTargetUpdateHz();
            FrameLimiter.ValueChanged += e =>
            {
                updateTargetUpdateHz();
                Logger.Verbose($"Frame limiter changed from {e.OldValue} to {e.NewValue}");
            };

            // From here on there is an update loop to drain it, so removal-triggered disposal can be
            // deferred and budgeted rather than walking a whole subtree inline
            DrawableDisposalQueue.Enabled = true;

            executionState = ExecutionState.Running;

            FrameLimiter.ValueChanged += onFrameLimiterChanged;

            Window = CreateWindow();
            Window.Title = Options.FriendlyAppName;
            Window.ApplicationName = Name;
            Window.KeepScreenOn = KeepScreenOn.Value;
            KeepScreenOn.ValueChanged += e => mainThreadActions.Enqueue(() => Window.KeepScreenOn = e.NewValue);

            Window.OnKeyDown += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.KeyDown, Key = e });
            Window.OnKeyUp += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.KeyUp, Key = e });
            Window.OnTouchDown += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.TouchDown, Touch = e });
            Window.OnTouchMove += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.TouchMove, Touch = e });
            Window.OnTouchUp += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.TouchUp, Touch = e });
            Window.OnMouseDown += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.MouseDown, MouseButton = e });
            Window.OnMouseUp += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.MouseUp, MouseButton = e });
            Window.OnMouseMove += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.MouseMove, Mouse = e });
            Window.OnScroll += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.Scroll, Scroll = e });
            Window.OnDragDropFile += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.DragDropFile, DragDropFile = e });
            Window.OnDragDropText += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.DragDropText, DragDropText = e });
            Window.OnTextInput += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.TextInput, TextInput = e });
            Window.OnTextEditing += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.TextEditing, TextEditing = e });
            Window.OnGamepadButtonDown += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.GamepadButtonDown, GamepadButton = e });
            Window.OnGamepadButtonUp += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.GamepadButtonUp, GamepadButton = e });
            Window.OnGamepadAxisMotion += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.GamepadAxisMotion, GamepadAxis = e });
            Window.OnGamepadConnected += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.GamepadConnected, GamepadConnected = e });
            Window.OnGamepadDisconnected += e => inputQueue.Enqueue(new PendingInput { Type = PendingInputType.GamepadDisconnected, GamepadDisconnected = e });
            Window.Resized += (w, h) =>
            {
                Window.GetPhysicalSize(out int pw, out int ph);
                inputQueue.Enqueue(new PendingInput
                {
                    Type = PendingInputType.Resize,
                    ResizePhysicalWidth = pw,
                    ResizePhysicalHeight = ph,
                    ResizeLogicalWidth = w,
                    ResizeLogicalHeight = h
                });
            };
            // Route the close request through the input queue so it is handled on the update thread,
            // where the scene graph lives (a confirmation dialog etc. can be shown safely).
            Window.ExitRequested += () => inputQueue.Enqueue(new PendingInput
            {
                Type = PendingInputType.ExitRequested
            });
            Window.FocusLost += updateTargetUpdateHz;

            // Whatever is let go of in another window never reaches this one, so release it here.
            Window.FocusLost += () => inputQueue.Enqueue(new PendingInput
            {
                Type = PendingInputType.ReleaseHeldInput
            });
            Window.FocusGained += updateTargetUpdateHz;
            Window.Minimized += updateTargetUpdateHz;
            Window.Restored += updateTargetUpdateHz;
            Window.DisplayChanged += _ => updateTargetUpdateHz();
            Window.EnteringBackground += enterBackground;
            Window.EnteredForeground += enterForeground;
            Window.LowMemory += onLowMemory;

            Window.RenderRequested += () =>
            {
                // make renderer still resize itself in single thread mode. The window can ask before the
                // threads exist: a window made during startup (a renderer falling back makes a second one)
                // reports being exposed while the renderer is still being set up.
                if (executionState == ExecutionState.Running && threadRunner != null && ExecutionMode?.Value == Threading.ExecutionMode.SingleThread)
                {
                    threadRunner.RunSingleThreadedFrame(targetUpdateHz > 0 ? 1000.0 / targetUpdateHz : 0, GetPresentationDeadlineMs());
                }
            };

            Window.Initialize();

            createRendererAndWindow();

            Window.GetPhysicalSize(out int initialPhysicalWidth, out int initialPhysicalHeight);
            onResize(initialPhysicalWidth, initialPhysicalHeight, Window.Width, Window.Height);

            // Precision busy-spinning is only worthwhile for a game while the window is focused (and may
            // be disabled entirely via HostOptions for battery-constrained targets).
            Func<bool> usePreciseTiming = () => allowSpinning && Window?.IsActive != false;
            bool useMultimediaClassScheduler = Options.AppType == AppType.Game && Options.UseMultimediaClassScheduler;

            // The two threads a frame waits on. An application only has to run smoothly, so the system may favour the
            // battery for it.
            bool preferPowerEfficiency = Options.AppType == AppType.Application;
            Func<string, double, IFrameWorkHint> createWorkHint = (name, target) => Performance.CreateFrameWorkHint(name, target, preferPowerEfficiency);

            updateThread = new AppThread("UpdateThread", PerformUpdate, getUpdateTargetHz)
            {
                GetDeadlineMilliseconds = GetPresentationDeadlineMs,
                Priority = ThreadPriority.AboveNormal,
                UsePreciseTiming = usePreciseTiming,
                UseMultimediaClassScheduler = useMultimediaClassScheduler,
                CreateWorkHint = createWorkHint
            };
            drawThread = new AppThread("DrawThread", PerformDraw, getDrawTargetHz)
            {
                Priority = ThreadPriority.Normal,
                UsePreciseTiming = usePreciseTiming,
                UseMultimediaClassScheduler = useMultimediaClassScheduler,
                GetBlockedMilliseconds = () => lastPresentMilliseconds,
                GetDeadlineMilliseconds = GetPresentationDeadlineMs,
                CreateWorkHint = createWorkHint
            };
            audioThread = new AppThread("AudioThread", PerformSoundUpdate, getAudioTargetHz)
            {
                GetDeadlineMilliseconds = GetPresentationDeadlineMs,
                Priority = ThreadPriority.Highest,

                // Never spins. The sound itself is mixed on the audio device's own thread, so this one only
                // advances AudioManager state, which a tick landing a fraction of a millisecond late does not
                // disturb. Spinning bought nothing but a core's worth of CPU.
                UsePreciseTiming = static () => false
            };

            drawThread.OnInitialize = () => Window.MakeCurrent();

            UpdateClock = updateThread.Clock;
            updateScheduler.SetClock(UpdateClock);
            DrawClock = drawThread.Clock;
            AudioClock = audioThread.Clock;
            InputClock = new Clock(true);

            threadRunner = new ThreadRunner(updateThread, drawThread, audioThread);

            if (FrameworkEnvironment.StartupExecutionMode is ExecutionMode forced)
            {
                ExecutionMode = new Reactive<Threading.ExecutionMode>(forced);
                Logger.Verbose($"Execution mode forced to {forced} by SAKURA_EXECUTION_MODE.");
            }
            else
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
            this.app.CompleteLoad();

            UpdateClock.Reset();
            DrawClock.Reset();
            AudioClock.Reset();
            InputClock.Reset();

            this.app.ResetClock();
            this.app.InvokeReady();

            renderPrimingFrame();

            Window.Show();

            if (Options.AppType == AppType.Game)
                Performance.SetGameState(GameState.None, loading: false);

            if (ExecutionMode.Value == Threading.ExecutionMode.MultiThread)
            {
                Window.ClearCurrent();
                threadRunner.SetExecutionMode(Threading.ExecutionMode.MultiThread);
            }

            FrameworkConfigManager.Get<WindowMode>(FrameworkSetting.WindowMode).ValueChanged += e =>
            {
                mainThreadActions.Enqueue(() =>
                {
                    Window.WindowMode = e.NewValue;
                });
            };

            appLoopStopwatch.Start();
            long nextMainFrameTime = Stopwatch.GetTimestamp();

            var mainWaiter = new FrameWaiter("MainThread");

            try
            {
                while (executionState == ExecutionState.Running)
                {
                    // In single-threaded mode the main loop drives everything at the update rate.
                    // In multi-threaded mode it only drives input, so it follows the input target Hz
                    // (which throttles to 60 when the window is inactive, like the other threads).
                    //
                    // Read before the body rather than after it because the single-threaded frame needs
                    // the budget to record against. A focus change arriving in this iteration's
                    // PollEvents is therefore acted on one frame later, which at these rates is noise.
                    double currentHz = ExecutionMode.Value == Threading.ExecutionMode.SingleThread
                        ? targetUpdateHz
                        : GetInputTargetHz();

                    double mainBudgetMs = currentHz > 0 ? 1000.0 / currentHz : 0;

                    double deadlineMs = GetPresentationDeadlineMs();

                    long housekeepingTicks = Stopwatch.GetTimestamp();

                    if ((housekeepingTicks - lastGCStatisticsTicks) * ms_per_tick >= gc_statistics_interval_ms)
                    {
                        lastGCStatisticsTicks = housekeepingTicks;
                        GCStatistics.Update();
                        Graphics.Textures.TextureRegistry.Prune();
                    }

                    long nowTicks = Stopwatch.GetTimestamp();
                    double pauseBefore = GC.GetTotalPauseDuration().TotalMilliseconds;

                    while (mainThreadActions.TryDequeue(out var action))
                    {
                        action.Invoke();
                    }

                    Window?.PollEvents();

                    if (Window?.IsExiting == true)
                        Exit();

                    PerformInput();

                    // Measured here, before any single-threaded frame, so the update/draw/audio work
                    // that runs below is not also booked against the input row.
                    double inputGcMs = GC.GetTotalPauseDuration().TotalMilliseconds - pauseBefore;
                    double inputFrameMs = (Stopwatch.GetTimestamp() - nowTicks) * ms_per_tick;

                    InputFrameStatistics.Record(new ThreadFrameSample
                    {
                        BusyMilliseconds = Math.Max(0, inputFrameMs - inputGcMs),
                        GCMilliseconds = inputGcMs,
                        ElapsedMilliseconds = InputClock.ElapsedFrameTime,
                        BudgetMilliseconds = mainBudgetMs,
                        DeadlineMilliseconds = deadlineMs
                    });

                    if (ExecutionMode.Value == Threading.ExecutionMode.SingleThread)
                    {
                        threadRunner.RunSingleThreadedFrame(mainBudgetMs, deadlineMs);
                    }

                    if (currentHz > 0 && Window != null && ExecutionMode.Value == Threading.ExecutionMode.MultiThread && pacing.InputWaitsForEvents)
                    {
                        // Woken by the next event, or after a frame to act on what other threads asked of this
                        // one (text input, cursor changes, scheduled actions).
                        Window.WaitForEvents(mainBudgetMs);
                        nextMainFrameTime = Stopwatch.GetTimestamp();
                    }
                    else if (currentHz > 0)
                    {
                        long targetMainTicks = (long)(mainBudgetMs / ms_per_tick);

                        nextMainFrameTime += targetMainTicks;

                        long now = Stopwatch.GetTimestamp();

                        if (now > nextMainFrameTime)
                            nextMainFrameTime = now;

                        // Multi-threaded, this loop only polls input, and the events carry their own timestamps,
                        // so it sleeps rather than spins. Single-threaded it drives every frame, so it follows
                        // the app threads' policy: spin unless disabled or the window is inactive.
                        bool mainPreciseTiming = ExecutionMode.Value == Threading.ExecutionMode.SingleThread
                                                 && allowSpinning && Window?.IsActive != false;
                        mainWaiter.WaitUntil(nextMainFrameTime, mainPreciseTiming);
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
                mainWaiter.Dispose();
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

            // Releasing a wait that timed out would let two hosts run at once from then on.
            if (ownsHostRunning)
                host_running_mutex.Release();
        }
    }

    /// <summary>
    /// Invoked when the user requests to close the window (e.g. the title-bar close button), on the
    /// update thread. Return <c>true</c> from a handler to block the automatic exit — typically to
    /// show an in-app confirmation, after which the app calls <see cref="Exit"/> itself once confirmed.
    /// If no handler blocks, the host exits. A second request within <see cref="force_exit_threshold_ms"/>
    /// (e.g. double-clicking the close button) forces an immediate exit, bypassing any block.
    /// </summary>
    public event Func<bool> ExitRequested;

    /// <summary>
    /// Window (in ms, on the update clock) within which a second close request is treated as a
    /// "force close" that bypasses any <see cref="ExitRequested"/>/<see cref="App.OnExitRequested"/> veto.
    /// </summary>
    private const double force_exit_threshold_ms = 1000;

    private double lastExitRequestMs = double.NegativeInfinity;

    /// <summary>
    /// Whether the app may block a close request (<see cref="App.OnExitRequested"/>, <see cref="ExitRequested"/>).
    /// A host whose system closes the app without waiting for it (an Android activity being destroyed) says no.
    /// </summary>
    protected virtual bool CanBlockExit => true;

    /// <summary>
    /// Handles a close request on the update thread: honors a veto on the first request, but forces an
    /// immediate exit if a second request arrives quickly (double-clicking the close button).
    /// </summary>
    private void handleExitRequested()
    {
        double now = UpdateClock.CurrentTime;
        bool isSecondRequest = now - lastExitRequestMs <= force_exit_threshold_ms;
        lastExitRequestMs = now;

        if (!CanBlockExit || isSecondRequest || !shouldBlockExit())
        {
            // Close the window; the main loop's IsExiting check then drives Exit().
            Window?.Close();
        }
        // Otherwise a handler took responsibility (e.g. showed a dialog) and will call Exit() later.
    }

    /// <summary>
    /// Asks the app and any <see cref="ExitRequested"/> subscribers whether the close should be blocked.
    /// All handlers run (no short-circuit), so each gets a chance to react.
    /// </summary>
    private bool shouldBlockExit()
    {
        bool blocked = app?.OnExitRequested() ?? false;

        var handlers = ExitRequested;
        if (handlers != null)
        {
            foreach (var @delegate in handlers.GetInvocationList())
            {
                var handler = (Func<bool>)@delegate;
                blocked |= handler();
            }
        }

        return blocked;
    }

    /// <summary>
    /// Called when nothing in the app handled <see cref="Key.SystemBack"/>. A host for a system with a back
    /// action does what that system does by default; an Android app leaves the screen.
    /// </summary>
    protected virtual void OnUnhandledSystemBack()
    {
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
        bool handled = app?.OnKeyDown(e) ?? false;

        if (!handled && !e.IsRepeat && e.Key == Key.SystemBack)
            OnUnhandledSystemBack();

        // Global hotkey
        // TODO: Global hotkey should be in list (don't have to be able to change since it's framework level)
        if (!e.IsRepeat && e.Key == Key.F10 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            int currentValue = (int)FrameLimiter.Value;
            int nextValue = (currentValue + 1) % frame_sync_value_count;
            FrameLimiter.Value = (FrameSync)nextValue;
        }

        if (!e.IsRepeat && e.Key == Key.F11 && (e.Modifiers & KeyModifiers.Control) == 0)
        {
            ScheduleToMainThread(() =>
            {
                // Cycles through the modes the platform has: windowed, borderless, then fullscreen on desktop.
                var modes = Window.SupportedWindowModes;
                int current = 0;

                for (int i = 0; i < modes.Count; i++)
                {
                    if (modes[i] == Window.WindowMode)
                        current = i;
                }

                Window.WindowMode = modes[(current + 1) % modes.Count];
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

        if (app == null)
            return;

        app.Size = new Vector2(logicalWidth, logicalHeight);
        app.UpdateDpiScale(logicalWidth > 0 ? (float)physicalWidth / logicalWidth : 1.0f);
    }

    private void OnMouseDown(MouseButtonEvent e) => app?.OnMouseDown(e);
    private void OnMouseUp(MouseButtonEvent e) => app?.OnMouseUp(e);
    private void OnMouseMove(MouseEvent e) => app?.OnMouseMove(e);
    private void OnScroll(ScrollEvent e) => app?.OnScroll(e);
    private void onDragDropFile(DragDropFileEvent e) => app?.OnDragDropFile(e);
    private void onDragDropText(DragDropTextEvent e) => app?.OnDragDropText(e);
    protected void OnTextInput(TextInputEvent e) => app?.OnTextInput(e);
    protected void OnTextEditing(TextEditingEvent e) => app?.OnTextEditing(e);
    protected virtual void OnGamepadButtonDown(GamepadButtonEvent e) => app?.OnGamepadButtonDown(e);
    protected virtual void OnGamepadButtonUp(GamepadButtonEvent e) => app?.OnGamepadButtonUp(e);
    protected virtual void OnGamepadAxisMotion(GamepadAxisEvent e) => app?.OnGamepadAxisMotion(e);
    protected virtual void OnGamepadConnected(GamepadConnectedEvent e) => app?.OnGamepadConnected(e);
    protected virtual void OnGamepadDisconnected(GamepadDisconnectedEvent e) => app?.OnGamepadDisconnected(e);

    /// <summary>
    /// Chooses the renderer, creates the window for it and sets it up. A renderer that fails to start is
    /// disposed, the window is destroyed, and the host's next choice gets a window made for it, until one
    /// starts or the host has none left (<see cref="OnRendererFailed"/>).
    /// </summary>
    private void createRendererAndWindow()
    {
        while (true)
        {
            Renderer = CreateRenderer();
            PrepareWindowForRenderer(Window);

            try
            {
                Window.Create();
                onFrameLimiterChanged(new ValueChangedEvent<FrameSync>(default, FrameLimiter.Value));
                SetupRenderer();
                return;
            }
            catch (Exception e) when (OnRendererFailed(e))
            {
                // Disposed before the window goes: a renderer's surface or context belongs to it.
                (Renderer as IDisposable)?.Dispose();
                Renderer = null;
                Window.Destroy();
            }
        }
    }

    protected virtual void SetupRenderer()
    {
        // just safety here if a subclass/test path didn't pre-create it.
        Renderer ??= CreateRenderer();
        Renderer.ShaderStorage = FrameworkStorage.GetStorageForDirectory("Shaders");

        if (Storage != null)
            Renderer.ShaderCache = new DiskCache(GetCacheStorage().GetStorageForDirectory("shader"));

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
        bool vsync = e.NewValue == FrameSync.VSync;

        // VSync need to be set on window, other frame limiters are handled in the clock.
        // OpenGL syncs at the window layer (SDL swap interval), Direct3D11 has no window-layer VSync,
        // so the renderer maps this to its swapchain present interval. Both are notified, each backend
        // acts on the one that applies to it.
        Window?.SetVSync(vsync);
        Renderer?.SetVSync(vsync);
    }

    /// <summary>
    /// This method is called at a fixed 1000Hz for precise input handling.
    /// </summary>
    protected virtual void PerformInput()
    {
        InputClock.ProcessFrame();
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
    /// Shows a native "open file" dialog and invokes <paramref name="callback"/> with the result.
    /// </summary>
    /// <remarks>
    /// Safe to call from any thread. The dialog is shown on the OS main thread and the
    /// <paramref name="callback"/> is marshalled back onto the update thread, so it is safe to
    /// touch drawables and application state directly from it.
    /// </remarks>
    /// <param name="options">The dialog configuration (title, starting location, filters, multi-select).</param>
    /// <param name="callback">Invoked with the selection, an empty result on cancel, or an error.</param>
    public void ShowOpenFileDialog(FileDialogOptions options, Action<FileDialogResult> callback)
        => showFileDialog((window, cb) => window.ShowOpenFileDialog(options, cb), callback);

    /// <summary>
    /// Shows a native "save file" dialog and invokes <paramref name="callback"/> with the result.
    /// </summary>
    /// <remarks>
    /// Safe to call from any thread. The dialog is shown on the OS main thread and the
    /// <paramref name="callback"/> is marshalled back onto the update thread, so it is safe to
    /// touch drawables and application state directly from it.
    /// </remarks>
    /// <param name="options">The dialog configuration. <see cref="FileDialogOptions.AllowMultiple"/> is ignored.</param>
    /// <param name="callback">Invoked with the chosen path, an empty result on cancel, or an error.</param>
    public void ShowSaveFileDialog(FileDialogOptions options, Action<FileDialogResult> callback)
        => showFileDialog((window, cb) => window.ShowSaveFileDialog(options, cb), callback);

    /// <summary>
    /// Shows a native "open folder" dialog and invokes <paramref name="callback"/> with the result.
    /// </summary>
    /// <remarks>
    /// Safe to call from any thread. The dialog is shown on the OS main thread and the
    /// <paramref name="callback"/> is marshalled back onto the update thread, so it is safe to
    /// touch drawables and application state directly from it.
    /// </remarks>
    /// <param name="options">The dialog configuration. <see cref="FileDialogOptions.Filters"/> is ignored.</param>
    /// <param name="callback">Invoked with the selection, an empty result on cancel, or an error.</param>
    public void ShowOpenFolderDialog(FileDialogOptions options, Action<FileDialogResult> callback)
        => showFileDialog((window, cb) => window.ShowOpenFolderDialog(options, cb), callback);

    /// <summary>
    /// Whether a native file dialog is currently open. While true, positional pointer input
    /// (mouse move/down/up, scroll) queued from the window is dropped on the update thread.
    /// </summary>
    /// <remarks>
    /// Some platforms (observed on macOS, and reportedly on Linux/X11 as well) still deliver a
    /// click to the underlying window while a native modal file dialog has focus. However, without a
    /// matching mouse-move to the click position first, the app would otherwise "see" a click at
    /// a stale cursor position that the user never intended to click on.
    /// </remarks>
    private volatile bool isFileDialogOpen;

    private void showFileDialog(Action<IWindow, Action<FileDialogResult>> show, Action<FileDialogResult> callback)
    {
        var window = Window;

        if (window == null)
        {
            // No window to parent the dialog to, report cancellation on the update thread so
            // callers see a consistent asynchronous contract.
            updateScheduler.Add(() => callback?.Invoke(FileDialogResult.Cancelled));
            return;
        }

        isFileDialogOpen = true;

        ScheduleToMainThread(() => show(window, result => updateScheduler.Add(() =>
        {
            isFileDialogOpen = false;
            app?.ReleaseHeldInput();
            callback?.Invoke(result);
        })));
    }

    /// <summary>
    /// This method is called at a fixed 1000Hz for precise audio processing.
    /// </summary>
    protected virtual void PerformSoundUpdate()
    {
        AudioManager?.Update(AudioClock.ElapsedFrameTime);
    }

    /// <summary>
    /// Runs a single synchronous update + draw + present pass on the calling (main) thread.
    /// Used once at startup to put a real frame on screen before the window is shown, so the
    /// user never sees a blank window. Must be called while the graphics context is current on
    /// the calling thread before switching to multi-threaded execution.
    /// </summary>
    private void renderPrimingFrame()
    {
        if (IsHeadless || Renderer == null)
            return;

        UpdateClock.ProcessFrame();
        PerformUpdate();

        DrawClock.ProcessFrame();
        PerformDraw();
    }

    private GlobalStatistic<double> statThermalHeadroom;
    private GlobalStatistic<string> statBatterySaver;
    private GlobalStatistic<string> statGameMode;

    // Each listed only once the platform reports it, so they aren't lines of nothing everywhere else.
    private void publishPlatformState()
    {
        float headroom = Performance.ThermalHeadroom;

        if (!float.IsNaN(headroom))
        {
            statThermalHeadroom ??= GlobalStatistics.Get<double>("Host", "Thermal Headroom");
            statThermalHeadroom.Value = headroom;
        }

        if (Performance.PowerSaving is bool saving)
        {
            statBatterySaver ??= GlobalStatistics.Get<string>("Host", "Battery Saver");
            statBatterySaver.Value = saving ? "On" : "Off";
        }

        if (Performance.GameMode is GameMode mode)
        {
            statGameMode ??= GlobalStatistics.Get<string>("Host", "Game Mode");
            statGameMode.Value = mode.ToString();
        }
    }

    /// <summary>
    /// This method runs the main game logic updates. It uses a fixed-step approach
    /// to ensure updates are deterministic and independent of the draw rate.
    /// </summary>
    protected virtual void PerformUpdate()
    {
        // Before anything reads a clock: this is the moment the frame's drawn as of.
        long updateTime = Stopwatch.GetTimestamp();

        while (inputQueue.TryDequeue(out var input))
        {
            dispatchInput(in input);
        }

        stat_uptime.Value = UpdateClock.CurrentTime;
        stat_target_update_hz.Value = targetUpdateHz;
        publishPlatformState();

        // Run any actions marshalled onto the update thread (e.g. file dialog result callbacks).
        updateScheduler.Update();

        // One update per update-thread frame. Drawables advance by their clock's elapsed
        // time, so a fixed-timestep catch-up loop only multiplied full-tree traversals
        // (and risked a death spiral whenever one update exceeded its own timestep).
        // The thread's pacing (AppThread) already drives this method at the target rate.
        app?.UpdateSubTree();

        // Drain removed drawables within this frame's budget. After UpdateSubTree so anything removed
        // during this frame's updates (or by input dispatched above) is picked up in the same frame
        // rather than lingering until the next one.
        DrawableDisposalQueue.Process();

        int updateIndex = frameBufferManager.GetUpdateIndex();
        rootDrawNodes[updateIndex] = app?.GenerateDrawNodeSubtree(updateIndex);
        rootDrawNodeTimes[updateIndex] = updateTime;
        frameBufferManager.FinishUpdate();
    }

    /// <summary>
    /// This method handles all rendering. It's typically called once per loop iteration,
    /// and its frequency is limited by VSync or by sleeping in unlimited mode.
    /// </summary>
    protected virtual void PerformDraw()
    {
        stat_drawn_last_frame.Value = 0;
        Renderer?.Clear();
        Renderer?.StartFrame();

        int drawIndex = frameBufferManager.GetDrawIndex();
        var currentFrameNode = rootDrawNodes[drawIndex];

        if (currentFrameNode != null)
        {
            Renderer?.SetRoot(currentFrameNode);
            Renderer?.SetFrameContentTime(rootDrawNodeTimes[drawIndex]);
        }

        Renderer?.Draw(DrawClock);

        long swapStart = Stopwatch.GetTimestamp();
        Window?.SwapBuffers();

        // Where the display pushes back depends on the backend, so both places are counted: OpenGL
        // blocks in the window-layer swap above, while Metal and Direct3D11 present from inside the
        // renderer and report their own wait. Only one of the two is ever non-zero.
        lastPresentMilliseconds = (Stopwatch.GetTimestamp() - swapStart) * ms_per_tick
                                  + (Renderer?.LastBlockedMilliseconds ?? 0);
    }

    /// <summary>
    /// How long the last <see cref="PerformDraw"/> spent waiting on the display rather than drawing.
    /// </summary>
    private double lastPresentMilliseconds;

    private bool isMultiThread => ExecutionMode?.Value == Threading.ExecutionMode.MultiThread;
    internal double GetInputTargetHz() => isMultiThread ? getInputTargetHz() : targetUpdateHz;
    internal double GetAudioTargetHz() => isMultiThread ? getAudioTargetHz() : targetUpdateHz;
    internal double GetUpdateTargetHz() => isMultiThread ? getUpdateTargetHz() : targetUpdateHz;
    internal double GetDrawTargetHz() => isMultiThread ? getDrawTargetHz() : targetUpdateHz;

    /// <summary>
    /// How long a frame's work may take before a presented frame is lost, or 0 if that is not knowable.
    /// Recorded on every thread's frames as <see cref="ThreadFrameSample.DeadlineMilliseconds"/>.
    /// </summary>
    internal double GetPresentationDeadlineMs()
    {
        double displayHz = getDisplayHz();
        double drawHz = getDrawTargetHz();

        // Unlimited (0) has no ceiling of its own, so the panel is the only thing left pacing us.
        double presentationHz = drawHz > 0 ? Math.Min(drawHz, displayHz) : displayHz;

        return presentationHz > 0 ? 1000.0 / presentationHz : 0;
    }

    private bool allowSpinning => Options.AppType == AppType.Game && Options.AllowThreadSpinning;

    private FramePacing pacing => FramePacing.Current(Options.AppType);

    private double getDisplayHz() => Window?.DisplayHz > 0 ? Window.DisplayHz : 60;

    private bool windowInactive => Window != null && !Window.IsActive;

    private double getInputTargetHz() => windowInactive ? 60 : pacing.InputHz(getDisplayHz());
    private double getAudioTargetHz() => windowInactive ? 60 : pacing.AudioHz(getDisplayHz(), AudioManager?.PlaybackWaitsForUpdate ?? true);

    private double getDrawTargetHz()
    {
        // In headless mode, default to a sensible refresh rate for update calculations.
        double refreshRate = getDisplayHz();

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

    private double getUpdateTargetHz() => windowInactive ? 60 : pacing.UpdateHz(getDrawTargetHz(), Options.LimitUnlimitedUpdateRate);

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

        // No more frames will be pumped, so anything still queued has to be released now — and from here
        // on removal disposes inline, since deferring it would mean never.
        DrawableDisposalQueue.Enabled = false;
        DrawableDisposalQueue.Flush();

        if (cancelKeyPressHandler != null)
        {
            Console.CancelKeyPress -= cancelKeyPressHandler;
            cancelKeyPressHandler = null;
        }

        FrameworkConfigManager?.Flush();

        Logger.Shutdown();

        executionState = ExecutionState.Stopped;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
