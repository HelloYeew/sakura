// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.BassEngine;
using Sakura.Framework.Audio.Headless;
using Sakura.Framework.Audio.SdlEngine;
using Sakura.Framework.Configurations;
using Sakura.Framework.Development;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Direct3D11;
using Sakura.Framework.Graphics.Rendering.Metal;
using Sakura.Framework.Graphics.Rendering.Vulkan;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Platform.Dialogs;
using Sakura.Framework.Reactive;
using Sakura.Framework.Statistic;
using Sakura.Framework.Threading;
using Sakura.Framework.Timing;

namespace Sakura.Framework;

public partial class App : Container, IFocusManager, IInputManagerProvider
{
    public IWindow Window => Host?.Window;

    protected AppHost Host { get; private set; }

    protected IAudioManager AudioManager { get; private set; }
    protected TrackStore TrackStore { get; private set; }
    protected SampleStore SampleStore { get; private set; }

    protected ITextureManager TextureManager { get; private set; }
    protected IFontStore FontStore { get; private set; }

    /// <summary>
    /// Tells the font store what pixel density the window is now on, so text rasterized for the old
    /// display can be re-measured.
    /// </summary>
    internal void UpdateDpiScale(float dpiScale) => FontStore?.SetDpiScale(dpiScale);
    protected VideoStore VideoStore { get; private set; }

    private Reactive<PerformanceOverlayState> fpsGraphState;

    /// <summary>
    /// Holds everything added to the app, laid out in the window's safe area so nothing lands under a notch,
    /// a cutout or the system bars. A background reaches the edge of the screen with <see cref="Drawable.Bleed"/>.
    /// </summary>
    private readonly SafeAreaDefiningContainer safeArea = new SafeAreaDefiningContainer();

    private Reactive<SafeAreaPreview> safeAreaPreview;
    private Reactive<bool> logOverlayShown;

    protected override Container Content => safeArea;

    /// <summary>
    /// Stands a gesture's first finger in for the mouse when nothing takes it.
    /// </summary>
    private readonly TouchMouseEmulation touchEmulation;
    private readonly MultiFingerTap multiFingerTap = new MultiFingerTap();

    /// <summary>
    /// How many fingers tap together to open the debug tools on a touchscreen, in a debug build.
    /// </summary>
    private const int debug_tools_fingers = 4;

    public App()
    {
        AddInternal(safeArea);
        touchEmulation = new TouchMouseEmulation(this, InputManager, OnMouseMove, OnMouseDown, OnMouseUp);
    }

    /// <summary>
    /// The Assembly where embedded resources are stored.
    /// This defaults to the Assembly containing your App class (e.g., SampleApp.dll).
    /// </summary>
    protected virtual Assembly ResourceAssembly => GetType().Assembly;

    /// <summary>
    /// The root namespace for your embedded resources.
    /// This defaults to "[YourAppNamespace].Resources" (e.g., "SampleApp.Resources").
    /// You can override this in your App class to a dedicated namespace like "SampleApp.Resource".
    /// </summary>
    protected virtual string ResourceRootNamespace => $"{GetType().Namespace}.Resources";

    /// <summary>
    /// Additional resource assemblies to merge with the primary <see cref="ResourceAssembly"/>.
    /// Each entry is an (Assembly, rootNamespace) pair. Resources from all assemblies are
    /// searched in order. Mainly for override your test app to include both game resources
    /// and test-specific resources.
    /// </summary>
    protected virtual IEnumerable<(Assembly Assembly, string RootNamespace)> AdditionalResourceAssemblies
        => Array.Empty<(Assembly, string)>();

    public void SetHost(AppHost host) => Host = host;

    /// <summary>
    /// Shows a native "open file" dialog. The <paramref name="callback"/> runs on the update thread,
    /// so it is safe to touch drawables directly. See <see cref="AppHost.ShowOpenFileDialog"/>.
    /// </summary>
    protected void ShowOpenFileDialog(FileDialogOptions options, Action<FileDialogResult> callback)
        => Host?.ShowOpenFileDialog(options, callback);

    /// <summary>
    /// Shows a native "save file" dialog. The <paramref name="callback"/> runs on the update thread,
    /// so it is safe to touch drawables directly. See <see cref="AppHost.ShowSaveFileDialog"/>.
    /// </summary>
    protected void ShowSaveFileDialog(FileDialogOptions options, Action<FileDialogResult> callback)
        => Host?.ShowSaveFileDialog(options, callback);

    /// <summary>
    /// Shows a native "open folder" dialog. The <paramref name="callback"/> runs on the update thread,
    /// so it is safe to touch drawables directly. See <see cref="AppHost.ShowOpenFolderDialog"/>.
    /// </summary>
    protected void ShowOpenFolderDialog(FileDialogOptions options, Action<FileDialogResult> callback)
        => Host?.ShowOpenFolderDialog(options, callback);

    /// <summary>
    /// Invoked after the application is fully initialized (loaded, renderer ready)
    /// </summary>
    public virtual void OnReady()
    {
    }

    internal void InvokeReady()
    {
        OnReady();
        Ready?.Invoke();
    }

    /// <summary>
    /// Re-baselines the app's clock to zero to make every clock start from zero.
    /// </summary>
    internal void ResetClock()
    {
        if (Clock is FramedClock framed)
            framed.Reset();
    }

    /// <summary>
    /// Raised after <see cref="OnReady"/>, at the moment the application is fully initialized
    /// and about to become visible. See <see cref="OnReady"/> for timing guarantees.
    /// </summary>
    public event Action Ready;

    /// <summary>
    /// Hosts the four debug tools, each of which is a floating window built on the first press of its
    /// key and detached from the tree when it is closed.
    /// </summary>
    private DebugWindowLayer debugWindows;

    public override void Load()
    {
        if (!HasCustomClock && Clock is not FramedClock)
            Clock = new FramedClock(Host.UpdateClock);

        base.Load();

        Cache(Host.Window);

        AudioManager = CreateAudioManager();

        // The audio manager updates on the audio thread, so its user-facing events need a way back to
        // the update thread. This scheduler is drained in Update() below, which is that thread.
        AudioManager.EventScheduler = Scheduler;

        Host.AudioManager = AudioManager;
        var masterVolume = Host.FrameworkConfigManager.Get<double>(FrameworkSetting.MasterVolume);
        var trackVolume = Host.FrameworkConfigManager.Get<double>(FrameworkSetting.TrackVolume);
        var sampleVolume = Host.FrameworkConfigManager.Get<double>(FrameworkSetting.SampleVolume);
        AudioManager.MasterVolume.BindTo(masterVolume);
        AudioManager.TrackVolume.BindTo(trackVolume);
        AudioManager.SampleVolume.BindTo(sampleVolume);
        AudioManager.OutputDevice.BindTo(Host.FrameworkConfigManager.Get<string>(FrameworkSetting.AudioDevice));

        Cache(AudioManager);

        var primaryStorage = new EmbeddedResourceStorage(ResourceAssembly, ResourceRootNamespace);
        var additionalStorages = AdditionalResourceAssemblies
            .Select(e => (Storage)new EmbeddedResourceStorage(e.Assembly, e.RootNamespace))
            .ToList();
        Storage embeddedResourceStorage = additionalStorages.Count == 0
            ? primaryStorage
            : new CompositeStorage([primaryStorage, ..additionalStorages]);

        TrackStore = new TrackStore(embeddedResourceStorage.GetStorageForDirectory("Tracks"), AudioManager);
        SampleStore = new SampleStore(embeddedResourceStorage.GetStorageForDirectory("Samples"), AudioManager);

        IImageLoader imageLoader = CreateImageLoader();
        imageLoader.LogInfo();

        switch (Host.Renderer)
        {
            case GLRenderer:
                TextureManager = new GLTextureManager(Host.Renderer, GLRenderer.GL, embeddedResourceStorage.GetStorageForDirectory("Textures"), imageLoader);
                VideoStore = new VideoStore(embeddedResourceStorage.GetStorageForDirectory("Videos"), Host.Renderer, TextureManager);
                FontStore = new RendererFontStore(Host.Renderer);
                // TODO: This will exposed all framework file resource out, maybe find better way?
                var frameworkFontStorage = Host.FrameworkStorage.GetStorageForDirectory("Fonts");
                var fontStorage = embeddedResourceStorage.GetStorageForDirectory("Fonts");
                fontStorage = new CompositeStorage(frameworkFontStorage, fontStorage);
                FontStore.LoadDefaultFont(fontStorage);
                break;

            case HeadlessRenderer:
                TextureManager = new HeadlessTextureManager();
                FontStore = new HeadlessFontStore((HeadlessTextureManager)TextureManager);
                VideoStore = new VideoStore(embeddedResourceStorage.GetStorageForDirectory("Videos"), Host.Renderer, TextureManager);
                break;

            case MetalRenderer or D3D11Renderer or VulkanRenderer:
                TextureManager = new NativeTextureManager(Host.Renderer, embeddedResourceStorage.GetStorageForDirectory("Textures"), imageLoader);
                FontStore = new RendererFontStore(Host.Renderer);
                var nativeFrameworkFontStorage = Host.FrameworkStorage.GetStorageForDirectory("Fonts");
                var nativeFontStorage = new CompositeStorage(nativeFrameworkFontStorage, embeddedResourceStorage.GetStorageForDirectory("Fonts"));
                FontStore.LoadDefaultFont(nativeFontStorage);
                VideoStore = new VideoStore(embeddedResourceStorage.GetStorageForDirectory("Videos"), Host.Renderer, TextureManager);
                break;

            default:
                throw new NotSupportedException($"Renderer type {Host.Renderer.GetType().FullName} is not supported.");
        }

        Host.Window.GetPhysicalSize(out int physicalWidth, out _);
        FontStore.SetDpiScale(Host.Window.Width > 0 ? (float)physicalWidth / Host.Window.Width : 1.0f);

        Cache(TextureManager);
        Cache(imageLoader);
        Cache(FontStore);

        Cache<IAudioStore<ITrack>>(TrackStore);
        Cache<IAudioStore<ISample>>(SampleStore);
        if (VideoStore != null) Cache(VideoStore);

        Cache(Host);
        Cache(this);
        if (Host.Window != null) Cache(Host.Window);
        if (Host.Storage != null) Cache(Host.Storage);
        Cache(Host.FrameworkConfigManager);

        fpsGraphState = Host.FrameworkConfigManager.Get(FrameworkSetting.ShowFpsGraph, PerformanceOverlayState.Hidden);
        safeAreaPreview = Host.FrameworkConfigManager.Get(FrameworkSetting.SafeAreaPreview, SafeAreaPreview.Off);
        logOverlayShown = Host.FrameworkConfigManager.Get(FrameworkSetting.ShowLogOverlay, false);

        AddInternal(new SafeAreaPreviewOverlay(safeArea, safeAreaPreview)
        {
            Depth = float.MaxValue
        });

        Add(debugWindows = new DebugWindowLayer
        {
            Depth = float.MaxValue - 5
        });
        Add(new FpsGraph()
        {
            Depth = float.MaxValue
        });
        Add(new LogOverlay
        {
            Depth = float.MaxValue
        });

        registerHostStatistics();
    }

    private static void registerHostStatistics()
    {
        GlobalStatistics.Get<string>("Host", "Framework Version").Value = DebugUtils.GetFrameworkVersion();
        GlobalStatistics.Get<string>("Host", "App Version").Value = $"{Logger.AppIdentifier} {Logger.VersionIdentifier}";
        GlobalStatistics.Get<string>("Host", "Build").Value = DebugUtils.IsDebugBuild ? "Debug" : "Release";
    }

    /// <summary>
    /// Create a default <see cref="Storage"/>
    /// </summary>
    protected internal virtual Storage CreateStorage(AppHost host, Storage defaultStorage) => defaultStorage;

    /// <summary>
    /// Create the image loader used for loading textures, defaults to <see cref="RoutingImageLoader"/>
    /// </summary>
    protected virtual IImageLoader CreateImageLoader() => new RoutingImageLoader();

    /// <summary>
    /// Create the audio manager used for this app, selected by
    /// <see cref="FrameworkSetting.AudioBackend"/> and default to <see cref="SDLAudioManager"/>
    /// </summary>
    protected virtual IAudioManager CreateAudioManager()
    {
        if (Host.IsHeadless)
            return new HeadlessAudioManager();

        var backend = Host.FrameworkConfigManager.Get<AudioBackend>(FrameworkSetting.AudioBackend).Value;

        // Opened on the device picked from the start, rather than on the default and then moved.
        string outputDevice = Host.FrameworkConfigManager.Get<string>(FrameworkSetting.AudioDevice).Value;
        var deviceBufferFrames = Host.FrameworkConfigManager.Get<int>(FrameworkSetting.AudioDeviceBufferFrames);

        return AudioBackends.Create(backend,
            () => new SDLAudioManager(backend != AudioBackend.SDLManaged, deviceBufferFrames.Value,
                next => deviceBufferFrames.Value = next, Host.NativeAudioOutput, outputDevice),
            () => new BassAudioManager(outputDevice: outputDevice),
            AudioBackends.BassAvailable);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (IsDisposed)
            return;

        (AudioManager as IDisposable)?.Dispose();

        base.Dispose(isDisposing);
    }

    public override void Update()
    {
        base.Update();
        Scheduler?.Update();
        touchEmulation.Update();
    }

    /// <summary>
    /// Called on the update thread when the user requests to close the window (e.g. the title-bar
    /// close button). Override to intercept the close — for example, show a confirmation dialog and
    /// return <c>true</c> to keep the app open, then call <see cref="AppHost.Exit"/> via
    /// <see cref="Host"/> once the user confirms. Return <c>false</c> (the default) to allow the close.
    /// A rapid second request (double-clicking the close button) forces the close regardless.
    /// </summary>
    protected internal virtual bool OnExitRequested() => false;

    public InputManager InputManager { get; } = new InputManager();

    private void rebuildInputQueues() => InputManager.BuildQueues(this);

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!e.IsRepeat)
            InputManager.HandleKeyDown(e.Key);
        rebuildInputQueues();

        if (!e.IsRepeat && e.Key == Key.F1 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            // Shift opens the window if it is closed and goes straight to picking, rather than
            // toggling the window and the mode in one keystroke as this used to.
            if ((e.Modifiers & KeyModifiers.Shift) > 0)
                debugWindows.Open(() => new DrawVisualiser(this)).ToggleInspectMode();
            else
                toggleDrawVisualiser();

            return true;
        }
        else if (!e.IsRepeat && e.Key == Key.F2 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            toggleStatistics();
            return true;
        }
        else if (!e.IsRepeat && e.Key == Key.F3 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            toggleTextureViewer();
            return true;
        }
        else if (!e.IsRepeat && e.Key == Key.F6 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            cycleSafeAreaPreview();
            return true;
        }
        else if (!e.IsRepeat && e.Key == Key.F7 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            toggleExecutionMode();
        }
        // Not on a function key like the rest: macOS keeps Ctrl+F3 to Ctrl+F8 for its own keyboard navigation by
        // default, and those left are taken.
        else if (!e.IsRepeat && e.Key == Key.L && (e.Modifiers & KeyModifiers.Control) > 0 && (e.Modifiers & KeyModifiers.Shift) > 0)
        {
            toggleLogOverlay();
            return true;
        }
        else if (!e.IsRepeat && e.Key == Key.F9 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            toggleAudioMixer();
            return true;
        }
        if (!e.IsRepeat && e.Key == Key.F11 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            cycleFpsGraph();
            return true;
        }

        return InputManager.DispatchKeyDownWithFocus(e);
    }

    private void toggleDrawVisualiser() => debugWindows.Toggle(() => new DrawVisualiser(this));

    private void toggleStatistics() => debugWindows.Toggle(() => new GlobalStatisticsDisplay());

    private void toggleTextureViewer() => debugWindows.Toggle(() => new TextureViewerDisplay());

    private void toggleAudioMixer() => debugWindows.Toggle(() => new AudioMixerVisualiser(AudioManager));

    private void toggleLogOverlay() => logOverlayShown.Value = !logOverlayShown.Value;

    private void cycleSafeAreaPreview() =>
        safeAreaPreview.Value = (SafeAreaPreview)(((int)safeAreaPreview.Value + 1) % Enum.GetValues<SafeAreaPreview>().Length);

    private void toggleExecutionMode() =>
        Host.ExecutionMode.Value = Host.ExecutionMode.Value == ExecutionMode.MultiThread ? ExecutionMode.SingleThread : ExecutionMode.MultiThread;

    private void cycleFpsGraph() =>
        fpsGraphState.Value = (PerformanceOverlayState)(((int)fpsGraphState.Value + 1) % Enum.GetValues<PerformanceOverlayState>().Length);

    private void cycleFrameLimiter() =>
        Host.FrameLimiter.Value = (FrameSync)(((int)Host.FrameLimiter.Value + 1) % Enum.GetValues<FrameSync>().Length);

    private void toggleDebugTools() => debugWindows.Toggle(() => new DebugToolsWindow(new (string, Action)[]
    {
        ("Frame graph (Ctrl+F11)", cycleFpsGraph),
        ("Draw visualiser (Ctrl+F1)", toggleDrawVisualiser),
        ("Statistics (Ctrl+F2)", toggleStatistics),
        ("Textures (Ctrl+F3)", toggleTextureViewer),
        ("Audio mixer (Ctrl+F9)", toggleAudioMixer),
        ("Log (Ctrl+Shift+L)", toggleLogOverlay),
        ("Safe area preview (Ctrl+F6)", cycleSafeAreaPreview),
        ("Frame limiter (Ctrl+F10)", cycleFrameLimiter),
        ("Execution mode (Ctrl+F7)", toggleExecutionMode),
    }));

    public override bool OnKeyUp(KeyEvent e)
    {
        InputManager.HandleKeyUp(e.Key);
        rebuildInputQueues();
        return InputManager.DispatchKeyUp(e);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        InputManager.HandleMouseDown(e.Button, e.ScreenSpaceMousePosition, e.MouseState.FromTouch);
        rebuildInputQueues();

        debugWindows?.NotifyMouseDown(e.ScreenSpaceMousePosition);

        BeginMouseDownFocusTracking();

        bool handled = InputManager.DispatchMouseDown(e);

        // If nothing claimed focus during this click, release whatever was focused.
        // This correctly handles clicking non-focusable drawables that still consume
        // the event (labels, backgrounds, decorative containers, etc.).
        if (!WasFocusClaimedByLastClick)
            ChangeFocus(null);

        return handled;
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        InputManager.HandleMouseUp(e.Button, e.ScreenSpaceMousePosition, e.MouseState.FromTouch);
        rebuildInputQueues();
        return InputManager.DispatchMouseUp(e);
    }

    public void ReleaseHeldInput()
    {
        var state = InputManager.CurrentState;

        foreach (var button in state.PressedMouseButtons.ToArray())
            OnMouseUp(new MouseButtonEvent(new MouseState { Position = state.MousePosition }, button, 1));

        foreach (var key in state.PressedKeys.ToArray())
            OnKeyUp(new KeyEvent(key, state.Modifiers, false));
    }

    public override bool OnTouchDown(TouchEvent e)
    {
        InputManager.HandleTouch(e.Source, e.ScreenSpacePosition, true);
        InputManager.BuildQueues(this, e.ScreenSpacePosition);

        bool taken = InputManager.DispatchTouchDown(e);
        touchEmulation.TouchDown(e, taken);
        multiFingerTap.TouchDown(e, taken, Clock.CurrentTime);
        return taken;
    }

    public override bool OnTouchMove(TouchEvent e)
    {
        InputManager.HandleTouch(e.Source, e.ScreenSpacePosition, true);
        multiFingerTap.TouchMove(e);

        if (InputManager.DispatchTouchMove(e))
            return true;

        touchEmulation.TouchMove(e);
        return false;
    }

    public override bool OnTouchUp(TouchEvent e)
    {
        InputManager.HandleTouch(e.Source, e.ScreenSpacePosition, false);

        bool handled = InputManager.DispatchTouchUp(e);

        if (!handled)
            touchEmulation.TouchUp(e);

        // There are no Ctrl+F keys on a phone. Only in a debug build, since a player's four fingers landing together
        // is ordinary play in a game with lanes.
        if (multiFingerTap.TouchUp(e, Clock.CurrentTime, out _) == debug_tools_fingers && DebugUtils.IsDebugBuild)
            toggleDebugTools();

        return handled;
    }

    public override bool OnMouseMove(MouseEvent e)
    {
        InputManager.HandleMouseMove(e.MouseState.Position, e.MouseState.FromTouch);
        rebuildInputQueues();
        return InputManager.DispatchMouseMove(e);
    }

    public override bool OnScroll(ScrollEvent e)
    {
        InputManager.HandleScroll(e.ScreenSpaceMousePosition);
        rebuildInputQueues();
        return InputManager.DispatchScroll(e);
    }

    public override bool OnTextInput(TextInputEvent e)
    {
        rebuildInputQueues();
        return InputManager.DispatchTextInput(e);
    }

    public override bool OnTextEditing(TextEditingEvent e)
    {
        rebuildInputQueues();
        return InputManager.DispatchTextEditing(e);
    }

    public override bool OnGamepadButtonDown(GamepadButtonEvent e)
    {
        InputManager.HandleGamepadButtonDown(e.GamepadState.DeviceId, e.Button);
        rebuildInputQueues();
        return InputManager.DispatchGamepadButtonDown(e);
    }

    public override bool OnGamepadButtonUp(GamepadButtonEvent e)
    {
        InputManager.HandleGamepadButtonUp(e.GamepadState.DeviceId, e.Button);
        rebuildInputQueues();
        return InputManager.DispatchGamepadButtonUp(e);
    }

    public override bool OnGamepadAxisMotion(GamepadAxisEvent e)
    {
        InputManager.HandleGamepadAxis(e.GamepadState.DeviceId, e.Axis, e.Value);
        rebuildInputQueues();
        return InputManager.DispatchGamepadAxisMotion(e);
    }

    public override void OnGamepadConnected(GamepadConnectedEvent e)
    {
        InputManager.HandleGamepadConnected(e.DeviceId);
        rebuildInputQueues();
        InputManager.DispatchGamepadConnected(e);
    }

    public override void OnGamepadDisconnected(GamepadDisconnectedEvent e)
    {
        InputManager.HandleGamepadDisconnected(e.DeviceId);
        rebuildInputQueues();
        InputManager.DispatchGamepadDisconnected(e);
    }

    #region Focus Management (delegated to InputManager)

    public Drawable? FocusedDrawable => InputManager.FocusedDrawable;

    public IReadOnlyList<Drawable> FocusStack => InputManager.FocusStack;

    public bool WasFocusClaimedByLastClick => InputManager.WasFocusClaimedByLastClick;

    public void BeginMouseDownFocusTracking() => InputManager.BeginMouseDownFocusTracking();

    public virtual bool ChangeFocus(Drawable? potentialFocusTarget) => InputManager.ChangeFocus(potentialFocusTarget);

    public virtual void TriggerFocusContention(Drawable? triggerSource) => InputManager.TriggerFocusContention(triggerSource);

    public virtual bool MoveFocusToNextTabStop(bool reverse = false) => InputManager.MoveFocusToNextTabStop(reverse);

    #endregion
}
