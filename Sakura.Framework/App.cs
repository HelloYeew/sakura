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
using Sakura.Framework.Configurations;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Metal;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.Input;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Sakura.Framework.Threading;
using Sakura.Framework.Timing;

namespace Sakura.Framework;

public partial class App : Container, IFocusManager, IInputManagerProvider, IDisposable
{
    public IWindow Window => Host?.Window;

    protected AppHost Host { get; private set; }

    protected IAudioManager AudioManager { get; private set; }
    protected TrackStore TrackStore { get; private set; }
    protected SampleStore SampleStore { get; private set; }

    protected ITextureManager TextureManager { get; private set; }
    protected IFontStore FontStore { get; private set; }
    protected VideoStore VideoStore { get; private set; }

    private Reactive<PerformanceOverlayState> fpsGraphState;

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

    private DrawVisualiser drawVisualiser;
    private GlobalStatisticsDisplay globalStatisticsDisplay;
    private TextureViewerDisplay textureViewerDisplay;
    private AudioMixerVisualiser audioMixerVisualiser;

    public override void Load()
    {
        if (!HasCustomClock && Clock is not FramedClock)
            Clock = new FramedClock(Host.UpdateClock);

        base.Load();

        Cache(Host.Window);

        AudioManager = CreateAudioManager();
        var masterVolume = Host.FrameworkConfigManager.Get<double>(FrameworkSetting.MasterVolume);
        var trackVolume = Host.FrameworkConfigManager.Get<double>(FrameworkSetting.TrackVolume);
        var sampleVolume = Host.FrameworkConfigManager.Get<double>(FrameworkSetting.SampleVolume);
        AudioManager.MasterVolume.BindTo(masterVolume);
        AudioManager.TrackVolume.BindTo(trackVolume);
        AudioManager.SampleVolume.BindTo(sampleVolume);

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

        switch (Host.Renderer)
        {
            case GLRenderer:
                TextureManager = new GLTextureManager(Host.Renderer, GLRenderer.GL, embeddedResourceStorage.GetStorageForDirectory("Textures"), CreateImageLoader());
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

            case MetalRenderer:
                TextureManager = new MetalTextureManager(Host.Renderer, embeddedResourceStorage.GetStorageForDirectory("Textures"), CreateImageLoader());
                FontStore = new RendererFontStore(Host.Renderer);
                var metalFrameworkFontStorage = Host.FrameworkStorage.GetStorageForDirectory("Fonts");
                var metalFontStorage = new CompositeStorage(metalFrameworkFontStorage, embeddedResourceStorage.GetStorageForDirectory("Fonts"));
                FontStore.LoadDefaultFont(metalFontStorage);
                VideoStore = new VideoStore(embeddedResourceStorage.GetStorageForDirectory("Videos"), Host.Renderer, TextureManager);
                break;

            default:
                throw new NotSupportedException($"Renderer type {Host.Renderer.GetType().FullName} is not supported.");
        }
        Cache(TextureManager);
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

        Add(drawVisualiser = new DrawVisualiser(this)
        {
            Depth = float.MaxValue - 10,
            Alpha = 0
        });
        Add(textureViewerDisplay = new TextureViewerDisplay()
        {
            Depth = float.MaxValue - 10,
            Alpha = 0
        });
        Add(globalStatisticsDisplay = new GlobalStatisticsDisplay()
        {
            Depth = float.MaxValue - 10,
            Alpha = 0
        });
        Add(audioMixerVisualiser = new AudioMixerVisualiser(AudioManager)
        {
            Depth = float.MaxValue - 10,
            Alpha = 0
        });
        Add(new FpsGraph()
        {
            Depth = float.MaxValue
        });
    }

    private void toggleVisualiser()
    {
        if (textureViewerDisplay.State == Visibility.Visible) textureViewerDisplay.Hide();
        drawVisualiser.ToggleVisibility();
    }

    private void toggleStatisticsDisplay()
    {
        globalStatisticsDisplay.ToggleVisibility();
    }

    private void toggleTextureViewerDisplay()
    {
        if (globalStatisticsDisplay.State == Visibility.Visible) globalStatisticsDisplay.Hide();
        textureViewerDisplay.ToggleVisibility();
    }

    private void toggleAudioMixerVisualiserDisplay()
    {
        audioMixerVisualiser.ToggleVisibility();
    }

    /// <summary>
    /// Create a default <see cref="Storage"/>
    /// </summary>
    protected internal virtual Storage CreateStorage(AppHost host, Storage defaultStorage) => defaultStorage;

    /// <summary>
    /// Create the image loader used for loading textures, defaults to <see cref="ImageSharpImageLoader"/>.
    /// </summary>
    protected virtual IImageLoader CreateImageLoader() => new ImageSharpImageLoader();

    /// <summary>
    /// Create the audio manager used for this app, defaults to <see cref="BassAudioManager"/>.
    /// </summary>
    protected virtual IAudioManager CreateAudioManager()
    {
        if (Host.IsHeadless)
            return new HeadlessAudioManager();
        return new BassAudioManager();
    }

    public void Dispose()
    {
        (AudioManager as IDisposable)?.Dispose();
    }

    public override void Update()
    {
        base.Update();
        AudioManager?.Update(Clock.ElapsedFrameTime);
        Scheduler?.Update();
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
            toggleVisualiser();
            if ((e.Modifiers & KeyModifiers.Shift) > 0)
                drawVisualiser.ToggleInspectMode();
            return true;
        }
        else if (!e.IsRepeat && e.Key == Key.F2 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            toggleStatisticsDisplay();
            return true;
        }
        else if (!e.IsRepeat && e.Key == Key.F3 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            toggleTextureViewerDisplay();
            return true;
        }
        else if (!e.IsRepeat && e.Key == Key.F7 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            Host.ExecutionMode.Value = Host.ExecutionMode.Value == ExecutionMode.MultiThread ? ExecutionMode.SingleThread : ExecutionMode.MultiThread;
        }
        else if (!e.IsRepeat && e.Key == Key.F9 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            toggleAudioMixerVisualiserDisplay();
            return true;
        }
        if (!e.IsRepeat && e.Key == Key.F11 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            int nextState = ((int)fpsGraphState.Value + 1) % 3;
            fpsGraphState.Value = (PerformanceOverlayState)nextState;
            return true;
        }

        var focused = InputManager.FocusedDrawable;
        if (focused != null && focused.IsLoaded && focused.IsAlive)
        {
            if (focused.OnKeyDown(e))
                return true;
        }

        return InputManager.DispatchKeyDown(e);
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        InputManager.HandleKeyUp(e.Key);
        rebuildInputQueues();
        return InputManager.DispatchKeyUp(e);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        InputManager.HandleMouseDown(e.Button, e.ScreenSpaceMousePosition);
        rebuildInputQueues();

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
        InputManager.HandleMouseUp(e.Button, e.ScreenSpaceMousePosition);
        rebuildInputQueues();
        return InputManager.DispatchMouseUp(e);
    }

    public override bool OnMouseMove(MouseEvent e)
    {
        InputManager.HandleMouseMove(e.MouseState.Position);
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

    #endregion
}
