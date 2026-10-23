// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Sakura.Framework.Configurations;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform.Dialogs;
using Sakura.Framework.Reactive;
using Sakura.Framework.Statistic;
using Sakura.Framework.Timing;
using SDL;
using static SDL.SDL3;
using SakuraCursorState = Sakura.Framework.Input.CursorState;
using SakuraMouseButtonEvent = Sakura.Framework.Input.MouseButtonEvent;
using TextEditingEvent = Sakura.Framework.Input.TextEditingEvent;
using TextInputEvent = Sakura.Framework.Input.TextInputEvent;

namespace Sakura.Framework.Platform;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SDLWindow : IWindow
{
    private static unsafe SDL_Window* window;
    private static unsafe SDL_GLContextState* glContext;

    // The event watch callback is [UnmanagedCallersOnly] (static), so it reaches the
    // window through this instance reference. The window is effectively a singleton.
    private static SDLWindow instance;

    // Graphics surfaces — only one will be active depending on the selected renderer.
    private readonly SDLGraphicsSurface glSurface = new SDLGraphicsSurface();
    private readonly MetalGraphicsSurface metalSurface = new MetalGraphicsSurface();
    private readonly Win32GraphicsSurface win32Surface = new Win32GraphicsSurface();
    private readonly VulkanGraphicsSurface vulkanSurface;
    private IGraphicsSurface activeSurface;

    /// <summary>
    /// The view <see cref="InitializeMetalSurface"/> made, destroyed with the window.
    /// </summary>
    private IntPtr metalView;

    /// <summary>
    /// The window flags each graphics API adds, cleared before another API's are set.
    /// </summary>
    private const SDL_WindowFlags graphics_api_flags = SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL_WindowFlags.SDL_WINDOW_METAL | SDL_WindowFlags.SDL_WINDOW_VULKAN;

    private RendererType graphicsApi = RendererType.OpenGL;

    private FrameworkConfigManager windowConfig;

    private readonly MouseState mouseState = new MouseState();

    private readonly Dictionary<int, GamepadState> gamepadStates = new Dictionary<int, GamepadState>();
    private readonly Dictionary<int, IntPtr> openGamepads = new Dictionary<int, IntPtr>();

    private bool initialized;

    private string title = "Window";
    private string applicationName = "Sakura Framework App";
    private bool resizable = true;
    private Vector2 minimumSize = new Vector2(800, 600);
    private int currentWidth;
    private int currentHeight;
    private int logicalWidth;
    private int logicalHeight;

    /// <summary>
    /// SDL window coordinates per framework unit. On Android SDL works in physical pixels and reports the
    /// display's density on its own, so every position and size crossing into or out of SDL is scaled by it,
    /// and an app lays out in density-independent units as it does elsewhere. Desktop systems already scale
    /// window coordinates themselves, so there it stays 1.
    /// </summary>
    private float contentScale = 1;

    // Tracks the last size for which Resized was fired (main-thread only).
    // Separate from currentWidth/currentHeight which the watch callback updates
    // mid-drag so the renderer can draw at the correct size before PollEvents runs.
    private int lastNotifiedDrawW;
    private int lastNotifiedDrawH;
    private int lastNotifiedLogW;
    private int lastNotifiedLogH;
    private bool? pendingTextInputState;
    private TextInputType pendingTextInputType;
    private RectangleF? pendingTextInputRect;
    private readonly Lock textInputLock = new Lock();

    // Whether SDL text input is currently active (a text field is focused). Set on the main thread
    // when the pending start/stop is applied. Used to gate the macOS emoji-palette shortcut so it
    // only opens when there's somewhere to insert.
    private bool textInputActive;

    // The type the virtual keyboard was last opened with.
    private TextInputType activeTextInputType;

    // The text input area SDL has, in SDL window coordinates, so an unchanged one isn't sent again.
    private SDL_Rect appliedTextInputArea;
    private bool textInputAreaApplied;

    // The area a virtual keyboard was placed by: SDL places it as it opens, and after that only
    // RetargetScreenKeyboard moves it.
    private SDL_Rect screenKeyboardArea;

    private WindowMode windowMode = WindowMode.Windowed;

    // Base flags — no graphics API flag yet; added by SetGraphicsApi() before Create().
    private SDL_WindowFlags windowFlags = SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY;

    // SDL_WINDOWPOS_CENTERED — kept as a local constant (it's a C macro: SDL_WINDOWPOS_CENTERED_MASK | 0).
    private const int windowpos_centered = 0x2FFF0000;

    // Offset mapping SDL's event timestamp timeline onto the shared TimeSource timeline (ms).
    private double sdlTimestampOffset;

    public SDLWindow()
    {
        instance = this;
        vulkanSurface = new VulkanGraphicsSurface(this);
    }

    public string Title
    {
        get => title;
        set => setTitle(value);
    }

    public string ApplicationName
    {
        get => applicationName;
        set => setApplicationName(value);
    }

    public bool Resizable
    {
        get => resizable;
        set => setResizable(value);
    }

    public WindowMode WindowMode
    {
        get => windowMode;
        set => setWindowMode(value);
    }

    public Reactive<WindowMode> WindowModeReactive { get; } = new Reactive<WindowMode>(WindowMode.Windowed);

    public IReadOnlyList<WindowMode> SupportedWindowModes { get; } = fillsDisplay ? [WindowMode.Windowed, WindowMode.Borderless] : Enum.GetValues<WindowMode>();

    /// <summary>
    /// Whether the window always fills the screen, as on a phone: the system sizes it, so it has no size, position or
    /// minimum of its own to apply or remember, and fullscreen only hides the system bars.
    /// </summary>
    private static bool fillsDisplay => RuntimeInfo.IsMobile;

    // Only a desktop window in windowed mode has a size and position the user chose.
    private bool remembersBounds => windowConfig != null && windowMode == WindowMode.Windowed && !fillsDisplay;

    private static WindowMode supported(WindowMode mode) => fillsDisplay && mode == WindowMode.Fullscreen ? WindowMode.Borderless : mode;

    private bool keepScreenOn = true;

    public bool KeepScreenOn
    {
        get => keepScreenOn;
        set
        {
            if (keepScreenOn == value)
                return;

            keepScreenOn = value;

            if (!initialized)
                return;

            // On a phone this holds the screen on only while the app is in front; the system lets it sleep once it's
            // not.
            if (value)
                SDL_DisableScreenSaver();
            else
                SDL_EnableScreenSaver();

            Logger.Verbose(value ? "Keeping the screen on" : "Letting the screen sleep");
        }
    }

    public int Width => logicalWidth;
    public int Height => logicalHeight;

    /// <summary>
    /// The minimum size of the window, in pixels.
    /// </summary>
    public Vector2 MinimumSize
    {
        get => minimumSize;
        set
        {
            minimumSize = value;
            applyMinimumSize();
        }
    }

    public bool CursorInWindow { get; private set; }
    public IGraphicsSurface GraphicsSurface => activeSurface;

    private bool relativeMouseMode;
    private double cursorSensitivity = 1.0;

    /// <summary>
    /// Whether the mouse is captured in relative (raw input) mode.
    /// SDL reports unaccelerated hardware deltas (Raw Input / evdev underneath) and the window
    /// maintains a virtual cursor from them, scaled by <see cref="CursorSensitivity"/> and
    /// clamped to the window bounds.
    /// </summary>
    // Mode switches can be requested from any thread (e.g. a config change on the update
    // thread), but SDL window calls must run on the main thread — applied in PollEvents.
    private volatile bool pendingRelativeModeChange;

    public bool RelativeMouseMode
    {
        get => relativeMouseMode;
        set
        {
            if (relativeMouseMode == value)
                return;

            relativeMouseMode = value;

            if (initialized)
                pendingRelativeModeChange = true;
        }
    }

    /// <summary>
    /// Scale applied to raw mouse deltas while <see cref="RelativeMouseMode"/> is active.
    /// </summary>
    public double CursorSensitivity
    {
        get => cursorSensitivity;
        set => cursorSensitivity = value;
    }

    /// <summary>
    /// The device safe-area insets in window coordinates, kept up to date from SDL.
    /// </summary>
    public Reactive<MarginPadding> SafeAreaPadding { get; } = new Reactive<MarginPadding>(new MarginPadding());

    private unsafe void updateSafeArea()
    {
        if (window == null)
            return;

        MarginPadding insets;

        if (GetPlatformSafeAreaInsets() is MarginPadding platformInsets)
            insets = platformInsets;
        else
        {
            SDL_Rect safeRect;
            if (!SDL_GetWindowSafeArea(window, &safeRect))
                return;

            int windowW, windowH;
            SDL_GetWindowSize(window, &windowW, &windowH);

            insets = new MarginPadding
            {
                Left = Math.Max(0, safeRect.x),
                Top = Math.Max(0, safeRect.y),
                Right = Math.Max(0, windowW - (safeRect.x + safeRect.w)),
                Bottom = Math.Max(0, windowH - (safeRect.y + safeRect.h)),
            };
        }

        var padding = new MarginPadding
        {
            Left = insets.Left / contentScale,
            Top = insets.Top / contentScale,
            Right = insets.Right / contentScale,
            Bottom = insets.Bottom / contentScale,
        };

        if (padding.Equals(SafeAreaPadding.Value))
            return;

        SafeAreaPadding.Value = padding;
        Logger.Verbose($"Safe area insets: {padding}");
    }

    /// <summary>
    /// Sets the SDL hints this platform needs. Called before SDL is initialised.
    /// </summary>
    protected virtual void ConfigureHints()
    {
    }

    /// <summary>
    /// The safe-area insets in SDL window coordinates when the platform knows them better than SDL does, or
    /// null to use SDL's. Called on the event thread whenever the safe area may have changed.
    /// </summary>
    protected virtual MarginPadding? GetPlatformSafeAreaInsets() => null;

    /// <summary>
    /// Moves a virtual keyboard that's already up onto another text field. SDL places the keyboard and picks its
    /// layout only as it opens, so a field that starts text input while it's up would otherwise get the previous
    /// field's. Called on the event thread.
    /// </summary>
    /// <param name="type">The field's input type.</param>
    /// <param name="area">Where the field's caret is, in SDL window coordinates.</param>
    /// <param name="typeChanged">Whether the keyboard has to change layout too.</param>
    /// <returns>Whether the platform did it. If not, a change of type closes the keyboard and opens it again.</returns>
    protected virtual bool RetargetScreenKeyboard(TextInputType type, SDL_Rect area, bool typeChanged) => false;

    /// <summary>
    /// The refresh rate of the display the window is on when the platform knows it better than SDL does, or null
    /// to use SDL's. Called on the event thread whenever the rate may have changed.
    /// </summary>
    protected virtual float? GetPlatformRefreshRate() => null;

    /// <summary>
    /// Has the refresh rate read again on the event thread. Safe to call from any thread, for a subclass whose
    /// <see cref="GetPlatformRefreshRate"/> learns of a change SDL doesn't report.
    /// </summary>
    protected unsafe void InvalidateRefreshRate()
    {
        if (window == null)
            return;

        var e = new SDL_Event();
        e.display.type = SDL_EventType.SDL_EVENT_DISPLAY_CURRENT_MODE_CHANGED;
        pushEvent(&e);
    }

    /// <summary>
    /// Has the safe area read again on the event thread. Safe to call from any thread, for a subclass whose
    /// <see cref="GetPlatformSafeAreaInsets"/> learns of a change SDL doesn't report.
    /// </summary>
    protected unsafe void InvalidateSafeArea()
    {
        if (window == null)
            return;

        var e = new SDL_Event();
        e.window.type = SDL_EventType.SDL_EVENT_WINDOW_SAFE_AREA_CHANGED;
        e.window.windowID = SDL_GetWindowID(window);
        pushEvent(&e);
    }

    /// <summary>
    /// Re-reads <see cref="contentScale"/>. Returns whether it changed.
    /// </summary>
    private unsafe bool updateContentScale()
    {
        if (window == null || RuntimeInfo.OS != RuntimeInfo.Platform.Android)
            return false;

        float scale = SDL_GetWindowDisplayScale(window);

        if (scale <= 0 || scale == contentScale)
            return false;

        contentScale = scale;
        Logger.Verbose($"Content scale: {scale}");
        return true;
    }

    private unsafe void applyMinimumSize()
    {
        if (window == null || fillsDisplay) return;
        SDL_SetWindowMinimumSize(window, (int)(minimumSize.X * contentScale), (int)(minimumSize.Y * contentScale));
    }

    private unsafe void applyRelativeMouseMode()
    {
        if (!initialized || window == null)
            return;

        if (relativeMouseMode)
        {
            // The virtual cursor continues from wherever the OS cursor currently is
            // (mouseState.Position is already tracking it).
            SDL_SetWindowRelativeMouseMode(window, true);
            Logger.Debug("Relative (raw input) mouse mode enabled");
        }
        else
        {
            SDL_SetWindowRelativeMouseMode(window, false);
            // Hand the virtual cursor position back to the OS cursor so leaving
            // relative mode doesn't make the pointer jump.
            SDL_WarpMouseInWindow(window, mouseState.Position.X * contentScale, mouseState.Position.Y * contentScale);
            Logger.Debug("Relative (raw input) mouse mode disabled");
        }
    }

    /// <summary>
    /// Provides the config manager so the window can persist and restore its position, size and mode.
    /// Must be called before <see cref="Create()"/>.
    /// </summary>
    public void SetWindowConfig(FrameworkConfigManager config)
    {
        windowConfig = config;

        var savedModeConfig = config.Get<WindowMode>(FrameworkSetting.WindowMode);
        var savedMode = supported(savedModeConfig.Value);

        // So a settings screen shows the mode actually used.
        savedModeConfig.Value = savedMode;

        windowMode = savedMode;
        WindowModeReactive.Value = savedMode;

        // Relative (raw input) mouse mode and sensitivity follow the config both at startup
        // and live (e.g. from a game's settings screen).
        var relativeModeConfig = config.Get<bool>(FrameworkSetting.RelativeMouseMode);
        relativeMouseMode = relativeModeConfig.Value;
        relativeModeConfig.ValueChanged += e => RelativeMouseMode = e.NewValue;

        var sensitivityConfig = config.Get<double>(FrameworkSetting.CursorSensitivity);
        cursorSensitivity = sensitivityConfig.Value;
        sensitivityConfig.ValueChanged += e => cursorSensitivity = e.NewValue;
    }

    /// <summary>
    /// Must be called by <see cref="AppHost"/> before <see cref="Create()"/> to configure
    /// which graphics API flag to apply to the SDL window.
    /// </summary>
    public void SetGraphicsApi(RendererType type)
    {
        graphicsApi = type;

        // A window being made again for another API mustn't keep the last one's flag.
        windowFlags &= ~graphics_api_flags;

        switch (type)
        {
            case RendererType.Metal:
                windowFlags |= SDL_WindowFlags.SDL_WINDOW_METAL;
                activeSurface = metalSurface;
                break;

            case RendererType.Direct3D11:
                // D3D11 renders into a plain Win32 window — no graphics-API window flag. The DXGI
                // swapchain is created against the HWND resolved in InitializeWin32Surface().
                activeSurface = win32Surface;
                break;

            case RendererType.Vulkan:
                // With this flag SDL loads the Vulkan loader while creating the window, and the window
                // creation fails if no loader can be found.
                windowFlags |= SDL_WindowFlags.SDL_WINDOW_VULKAN;
                activeSurface = vulkanSurface;
                break;

            default:
                // OpenGL
                windowFlags |= SDL_WindowFlags.SDL_WINDOW_OPENGL;
                activeSurface = glSurface;

                // SDL requires GL attributes to be set before SDL_CreateWindow.
                SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
                SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 3);
                SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_FLAGS, (int)SDL_GLContextFlag.SDL_GL_CONTEXT_FORWARD_COMPATIBLE_FLAG);
                SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_CORE);
                SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_FRAMEBUFFER_SRGB_CAPABLE, 1);
                break;
        }
    }

    public bool IsActive { get; private set; } = true;
    public bool IsExiting { get; private set; }
    public int DisplayHz { get; private set; } = 60;
    public Reactive<string> DisplayName { get; } = new Reactive<string>(string.Empty);

    // TODO: This update action also no longer needed since it's handled in host's main loop
    public event Action Update = delegate { };
    public event Action FocusLost = delegate { };
    public event Action FocusGained = delegate { };
    public event Action Minimized = delegate { };
    public event Action Restored = delegate { };
    public event Action ExitRequested = delegate { };
    public event Action EnteringBackground = delegate { };
    public event Action EnteredForeground = delegate { };
    public event Action Exited = delegate { };

    public event Action<KeyEvent> OnKeyDown = delegate { };
    public event Action<KeyEvent> OnKeyUp = delegate { };
    public event Action<int> DisplayChanged = delegate { };
    public event Action LowMemory = delegate { };
    public event Action<int, int> Resized = delegate { };

    public event Action<TouchEvent> OnTouchDown = delegate { };
    public event Action<TouchEvent> OnTouchMove = delegate { };
    public event Action<TouchEvent> OnTouchUp = delegate { };
    public event Action<SakuraMouseButtonEvent> OnMouseDown = delegate { };
    public event Action<SakuraMouseButtonEvent> OnMouseUp = delegate { };
    public event Action<MouseEvent> OnMouseMove = delegate { };
    public event Action<ScrollEvent> OnScroll = delegate { };
    public event Action<DragDropFileEvent> OnDragDropFile = delegate { };
    public event Action<DragDropTextEvent> OnDragDropText = delegate { };
    public event Action<TextInputEvent> OnTextInput = delegate { };
    public event Action<TextEditingEvent> OnTextEditing = delegate { };

    public event Action<GamepadButtonEvent> OnGamepadButtonDown = delegate { };
    public event Action<GamepadButtonEvent> OnGamepadButtonUp = delegate { };
    public event Action<GamepadAxisEvent> OnGamepadAxisMotion = delegate { };
    public event Action<GamepadConnectedEvent> OnGamepadConnected = delegate { };
    public event Action<GamepadDisconnectedEvent> OnGamepadDisconnected = delegate { };

    public event Action RenderRequested = delegate { };

    public void StartTextInput(TextInputType type = TextInputType.Text)
    {
        lock (textInputLock)
        {
            pendingTextInputState = true;
            pendingTextInputType = type;
        }
    }

    public void StopTextInput()
    {
        lock (textInputLock)
        {
            pendingTextInputState = false;
        }
    }

    public void SetTextInputRect(RectangleF rect)
    {
        lock (textInputLock)
        {
            pendingTextInputRect = rect;
        }
    }

    public unsafe void Initialize()
    {
        setApplicationName(applicationName);

        // SDL would make the first finger a mouse that presses on touch-down; the framework turns fingers into
        // mouse input itself, the way touchscreens behave (TouchMouseEmulation).
        SDL_SetHint(SDL_HINT_TOUCH_MOUSE_EVENTS, "0");

        // SDL keeps the display on from initialisation unless told otherwise; set so it starts the way the app asked.
        SDL_SetHint(SDL_HINT_VIDEO_ALLOW_SCREENSAVER, keepScreenOn ? "0" : "1");

        ConfigureHints();

        // Note: SDL3 is always per-monitor DPI aware; the SDL2-era Windows DPI hints are gone.
        // Make sure SDL video backend is fully initialized
        // To make it support for OpenGL context and process advance hint like profile version
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_GAMEPAD))
        {
            throw new Exception($"Failed to initialize SDL: {SDL_GetError()}");
        }

        int version = SDL_GetVersion();

        anchorEventTimestamps();

        Logger.Verbose("🪟 SDL initialized");

        // From here, so a backgrounding that comes while the app is still starting isn't missed.
        SDL_AddEventWatch(&lifecycleEventWatch, IntPtr.Zero);
        AddTouchTimeWatch();
        Logger.Verbose($"SDL Version: {version / 1000000}.{version / 1000 % 1000}.{version % 1000}");
        Logger.Verbose($"SDL Revision: {SDL_GetRevision()}");
        Logger.Verbose($"SDL Video Driver: {SDL_GetCurrentVideoDriver()}");

        CursorState.ValueChanged += _ => pendingCursorChange = true;

        eventThreadId = Environment.CurrentManagedThreadId;
        initialized = true;
    }

    public unsafe void Create()
    {
        windowFlags |= SDL_WindowFlags.SDL_WINDOW_HIDDEN;

        if (Resizable)
        {
            windowFlags |= SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        }

        // In SDL3 both borderless-desktop and exclusive fullscreen use SDL_WINDOW_FULLSCREEN;
        // the distinction is made after creation via SDL_SetWindowFullscreenMode
        // (null mode = borderless desktop, explicit mode = exclusive).
        if (windowMode != WindowMode.Windowed)
            windowFlags |= SDL_WindowFlags.SDL_WINDOW_FULLSCREEN;

        int spawnX = windowpos_centered;
        int spawnY = windowpos_centered;
        int spawnW = 800;
        int spawnH = 600;

        if (remembersBounds)
        {
            int savedX = windowConfig.Get<int>(FrameworkSetting.WindowX).Value;
            int savedY = windowConfig.Get<int>(FrameworkSetting.WindowY).Value;
            int savedW = windowConfig.Get<int>(FrameworkSetting.WindowWidth).Value;
            int savedH = windowConfig.Get<int>(FrameworkSetting.WindowHeight).Value;

            if (savedX != -1 && savedY != -1 && isPositionOnConnectedDisplay(savedX, savedY))
            {
                spawnX = savedX;
                spawnY = savedY;
            }
            else if (savedX != -1 && savedY != -1)
            {
                // fallback to center if the saved position is invalid (e.g. monitor was disconnected)
                Logger.Warning($"Saved window position ({savedX},{savedY}) is not on any connected display. Resetting to center.");
                windowConfig.Get<int>(FrameworkSetting.WindowX).Value = -1;
                windowConfig.Get<int>(FrameworkSetting.WindowY).Value = -1;
            }
            if (savedW > 0 && savedH > 0)
            {
                spawnW = savedW;
                spawnH = savedH;
            }
        }

        // SDL3's SDL_CreateWindow no longer takes a position; set it right after creation.
        window = SDL_CreateWindow(title, spawnW, spawnH, windowFlags);

        if (window == null)
        {
            Logger.Error("Failed to create SDL window: " + SDL_GetError());
            throw new Exception("SDL window creation failed.");
        }

        // Before anything converts a size or position between SDL's units and the framework's.
        updateContentScale();

        applyMinimumSize();

        if (relativeMouseMode)
            SDL_SetWindowRelativeMouseMode(window, true);

        // updateSdlCursor early-returns while there is no window, so any cursor state set before
        // this point was silently dropped and never re-applied. Queue it now that there is one.
        pendingCursorChange = true;

        updateSafeArea();

        if (!fillsDisplay)
            SDL_SetWindowPosition(window, spawnX, spawnY);

        if (windowMode == WindowMode.Fullscreen)
        {
            WindowMode effectiveMode = applyExclusiveFullscreen();

            if (effectiveMode != windowMode)
            {
                windowMode = effectiveMode;
                WindowModeReactive.Value = effectiveMode;
                if (windowConfig != null)
                    windowConfig.Get<WindowMode>(FrameworkSetting.WindowMode).Value = effectiveMode;
            }
        }

        // It exists to keep drawing through a desktop window's live resize. On Android it would run on the UI
        // thread, where no frame may run, and the polled resize events are enough there.
        if (RuntimeInfo.OS != RuntimeInfo.Platform.Android)
            SDL_AddEventWatch(&resizeEventWatch, IntPtr.Zero);

        DisplayHz = getDisplayRefreshRate();
        Logger.Verbose($"Display refresh rate: {DisplayHz} Hz");
        updateDisplayName();

        int w, h;
        SDL_GetWindowSize(window, &w, &h);
        logicalWidth = (int)MathF.Round(w / contentScale);
        logicalHeight = (int)MathF.Round(h / contentScale);

        // Physical size is resolved after the graphics context is initialised.
        currentWidth = w;
        currentHeight = h;

        // Seed lastNotified* so the first real resize event isn't incorrectly suppressed.
        lastNotifiedLogW = w;
        lastNotifiedLogH = h;
        // lastNotifiedDraw* will be updated in InitializeGLContext/InitializeMetalSurface
        // once the true pixel size is known.

        Logger.Verbose("SDL window created successfully");
    }

    /// <summary>
    /// Creates the OpenGL context and wires up the <see cref="SDLGraphicsSurface"/>.
    /// Must be called after <see cref="Create()"/> when the selected renderer is OpenGL.
    /// </summary>
    public unsafe void InitializeGLContext()
    {
        glContext = SDL_GL_CreateContext(window);
        if (glContext == null)
        {
            Logger.Error("Failed to create OpenGL context: " + SDL_GetError());
            throw new Exception("OpenGL context creation failed.");
        }

        SDL_GL_MakeCurrent(window, glContext);

        // Wire up the GL surface callbacks used by GLRenderer.
        glSurface.GetFunctionAddress = proc => SDL_GL_GetProcAddress(proc);
        glSurface.MakeCurrent = () => SDL_GL_MakeCurrent(window, glContext);
        glSurface.ClearCurrent = () => SDL_GL_MakeCurrent(window, null);

        // Pixel size may differ from logical size on HiDPI displays.
        int phyW, phyH;
        SDL_GetWindowSizeInPixels(window, &phyW, &phyH);
        currentWidth = phyW;
        currentHeight = phyH;
        lastNotifiedDrawW = phyW;
        lastNotifiedDrawH = phyH;

        Logger.Verbose("OpenGL context created successfully");
    }

    /// <summary>
    /// Creates the Metal view via SDL and stores the <c>CAMetalLayer</c> pointer
    /// in the <see cref="MetalGraphicsSurface"/>. Must be called after <see cref="Create()"/>
    /// when the selected renderer is Metal.
    /// </summary>
    public unsafe void InitializeMetalSurface()
    {
        // SDL3 exposes the Metal API directly — no manual P/Invoke required anymore.
        IntPtr view = SDL_Metal_CreateView(window);
        if (view == IntPtr.Zero)
        {
            Logger.Error("Failed to create Metal view: " + SDL_GetError());
            throw new Exception("Metal view creation failed.");
        }

        IntPtr layer = SDL_Metal_GetLayer(view);
        if (layer == IntPtr.Zero)
        {
            Logger.Error("Failed to get CAMetalLayer from Metal view.");
            throw new Exception("CAMetalLayer retrieval failed.");
        }

        metalSurface.MetalLayer = layer;
        metalView = view;

        Logger.Verbose("Metal surface initialised successfully");
    }

    /// <summary>
    /// Resolves the native Win32 window handle (HWND) via SDL's window-properties API and stores it
    /// on the <see cref="Win32GraphicsSurface"/>. Must be called after <see cref="Create()"/> when
    /// the selected renderer is Direct3D11.
    /// </summary>
    public unsafe void InitializeWin32Surface()
    {
        SDL_PropertiesID props = SDL_GetWindowProperties(window);
        if ((uint)props == 0)
        {
            Logger.Error("Failed to get SDL window properties: " + SDL_GetError());
            throw new Exception("SDL window property retrieval failed.");
        }

        // The property-name constants are UTF-8 (u8) ReadOnlySpan<byte>; the API takes a byte*,
        // so pin the span for the call.
        IntPtr hwnd;
        fixed (byte* namePtr = SDL_PROP_WINDOW_WIN32_HWND_POINTER)
            hwnd = SDL_GetPointerProperty(props, namePtr, IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            Logger.Error("Failed to get Win32 HWND from SDL window.");
            throw new Exception("HWND retrieval failed.");
        }

        win32Surface.WindowHandle = hwnd;

        Logger.Verbose("Win32 (Direct3D11) surface initialised successfully");
    }

    /// <summary>
    /// Reads the instance extensions the window system needs into the <see cref="VulkanGraphicsSurface"/>.
    /// The surface itself is created later by the renderer, on the instance it creates with those
    /// extensions. Must be called after <see cref="Create()"/> when the selected renderer is Vulkan.
    /// </summary>
    public unsafe void InitializeVulkanSurface()
    {
        // SDL3-CS binds VkSurfaceKHR as a pointer, but it is a 64-bit handle on every platform, so the
        // two only agree in a 64-bit process.
        if (!Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Vulkan rendering requires a 64-bit process.");

        uint count;
        byte** names = SDL_Vulkan_GetInstanceExtensions(&count);

        if (names == null)
        {
            Logger.Error("Failed to get Vulkan instance extensions: " + SDL_GetError());
            throw new Exception("Vulkan instance extension query failed.");
        }

        string[] extensions = new string[count];

        for (int i = 0; i < count; i++)
            extensions[i] = Marshal.PtrToStringUTF8((IntPtr)names[i]);

        vulkanSurface.RequiredInstanceExtensions = extensions;

        Logger.Verbose($"Vulkan surface initialised successfully (instance extensions: {string.Join(", ", extensions)})");
    }

    internal unsafe ulong CreateVulkanSurface(nint vkInstance)
    {
        ulong surface = 0;

        if (!SDL_Vulkan_CreateSurface(window, (VkInstance_T*)vkInstance, null, (VkSurfaceKHR_T**)&surface))
        {
            Logger.Error("Failed to create Vulkan surface: " + SDL_GetError());
            throw new Exception("Vulkan surface creation failed.");
        }

        return surface;
    }

    internal unsafe bool TryCreateVulkanSurface(nint vkInstance, out ulong surface)
    {
        ulong created = 0;
        bool ok = SDL_Vulkan_CreateSurface(window, (VkInstance_T*)vkInstance, null, (VkSurfaceKHR_T**)&created);
        surface = created;
        return ok;
    }

    internal unsafe void DestroyVulkanSurface(nint vkInstance, ulong surface)
        => SDL_Vulkan_DestroySurface((VkInstance_T*)vkInstance, (VkSurfaceKHR_T*)surface, null);

    public void Close()
    {
        IsExiting = true;
    }

    public unsafe void GetDrawableSize(out int width, out int height)
    {
        if (window == null)
        {
            width = 0;
            height = 0;
            return;
        }

        // SDL3 unifies this for all backends (SDL_GL_GetDrawableSize no longer exists).
        int drawableWidth, drawableHeight;
        SDL_GetWindowSizeInPixels(window, &drawableWidth, &drawableHeight);

        width = drawableWidth;
        height = drawableHeight;
    }

    public unsafe void GetPhysicalSize(out int width, out int height)
    {
        // Query SDL directly for the true pixel size rather than returning the cached
        // currentWidth/currentHeight which aren't populated until the first resize event fires.
        // At startup that cache still holds the logical size, so callers (e.g. the initial onResize,
        // which drives DPI for glyph rasterisation) would otherwise get a half-resolution value on
        // HiDPI displays, making the first-rendered text/sprites permanently soft.
        if (window != null)
        {
            int pixelWidth, pixelHeight;
            SDL_GetWindowSizeInPixels(window, &pixelWidth, &pixelHeight);

            if (pixelWidth > 0 && pixelHeight > 0)
            {
                width = pixelWidth;
                height = pixelHeight;
                return;
            }
        }

        width = currentWidth;
        height = currentHeight;
    }

    private unsafe (float scaleX, float scaleY) getDisplayScale()
    {
        // On high DPI display, the pixel size and logical window size differ.
        // This returns the scale factor between them so mouse positions can be corrected.
        if (window == null) return (1.0f, 1.0f);

        int windowWidth, windowHeight;
        SDL_GetWindowSize(window, &windowWidth, &windowHeight);

        if (windowWidth == 0 || windowHeight == 0) return (1.0f, 1.0f);

        int drawableWidth, drawableHeight;
        SDL_GetWindowSizeInPixels(window, &drawableWidth, &drawableHeight);

        return ((float)drawableWidth / windowWidth, (float)drawableHeight / windowHeight);
    }

    /// <summary>
    /// Raises <see cref="EnteringBackground"/>, <see cref="EnteredForeground"/> and <see cref="LowMemory"/> the moment SDL
    /// reports them. On Android that is on the UI thread, before SDL blocks its own thread for the background, so a
    /// handler can still stop everything that draws to the window before its surface goes.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe SDLBool lifecycleEventWatch(IntPtr userdata, SDL_Event* e)
    {
        try
        {
            switch ((SDL_EventType)e->type)
            {
                // The system's last word before it ends an app that's still running. It has normally said the app was
                // going to the background first, which stopped everything and saved; if it hasn't, this does both.
                case SDL_EventType.SDL_EVENT_TERMINATING:
                case SDL_EventType.SDL_EVENT_WILL_ENTER_BACKGROUND:
                    instance?.EnteringBackground.Invoke();
                    break;

                case SDL_EventType.SDL_EVENT_DID_ENTER_FOREGROUND:
                    instance?.EnteredForeground.Invoke();
                    break;

                // Here rather than when polled: in the background, which is when Android asks most, SDL's thread is
                // blocked until the app comes back.
                case SDL_EventType.SDL_EVENT_LOW_MEMORY:
                    instance?.LowMemory.Invoke();
                    break;
            }
        }
        catch (Exception ex)
        {
            // An exception can't cross back into SDL.
            Logger.Error("Handling an app lifecycle event failed.", ex);
        }

        return true;
    }

    // When the touch being handed to SDL on this thread really happened, on the Stopwatch timeline, or 0 if unknown.
    [ThreadStatic]
    private static long pendingTouchTime;

    private static readonly GlobalStatistic<double> stat_touch_delay = GlobalStatistics.Get<double>("Input", "Touch Delay", StatisticKind.Gauge, StatisticUnit.Milliseconds);

    // A gap this long isn't a touch's delivery but a clock that doesn't match, and is left alone.
    private const long max_touch_delay_ns = 1_000_000_000;

    /// <summary>
    /// For a platform that knows when a touch really happened. SDL stamps a touch with the time it's handed over,
    /// which on Android comes after the system has passed it along, a few milliseconds later and by a varying amount;
    /// a rhythm game judges on that time. Call on the thread that hands SDL the touch, just before it does, and again
    /// with 0 once it has: every touch event SDL makes from it in between gets that time instead.
    /// </summary>
    /// <param name="stopwatchTimestamp">When the touch happened, on the <see cref="Stopwatch"/> timeline.</param>
    protected static void SetTouchTime(long stopwatchTimestamp) => pendingTouchTime = stopwatchTimestamp;

    internal static unsafe void AddTouchTimeWatch() => SDL_AddEventWatch(&touchTimeEventWatch, IntPtr.Zero);

    internal static unsafe void RemoveTouchTimeWatch() => SDL_RemoveEventWatch(&touchTimeEventWatch, IntPtr.Zero);

    /// <summary>
    /// Moves the touch events SDL is queueing onto the time from <see cref="SetTouchTime"/>. Runs as SDL queues each
    /// one, on the thread queueing it, with the event SDL is about to queue, so the change is what the app later reads.
    /// </summary>
    /// <remarks>
    /// The time is worked out on SDL's clock from scratch rather than taken off SDL's stamp, so running this more than
    /// once on an event changes nothing after the first. SDL runs every registration of a watch, and one that's never
    /// removed stays registered for the life of the process. It also leaves out however long SDL took between
    /// stamping the event and running this.
    /// </remarks>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe SDLBool touchTimeEventWatch(IntPtr userdata, SDL_Event* e)
    {
        long happened = pendingTouchTime;

        if (happened == 0)
            return true;

        switch ((SDL_EventType)e->type)
        {
            case SDL_EventType.SDL_EVENT_FINGER_DOWN:
            case SDL_EventType.SDL_EVENT_FINGER_MOTION:
            case SDL_EventType.SDL_EVENT_FINGER_UP:
            case SDL_EventType.SDL_EVENT_FINGER_CANCELED:
                // How long ago the touch happened, back from SDL's clock read alongside it.
                ulong now = SDL_GetTicksNS();
                long ago = (long)Stopwatch.GetElapsedTime(happened).TotalNanoseconds;

                if (ago <= 0 || ago >= max_touch_delay_ns || (ulong)ago >= now)
                    break;

                ulong touched = now - (ulong)ago;

                // Only ever earlier: a touch can't have reached SDL before it happened.
                if (touched < e->tfinger.timestamp)
                {
                    stat_touch_delay.Value = (e->tfinger.timestamp - touched) / 1_000_000.0;
                    e->tfinger.timestamp = touched;
                }

                break;
        }

        return true;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe SDLBool resizeEventWatch(IntPtr userdata, SDL_Event* e)
    {
        var type = (SDL_EventType)e->type;

        // During a live window resize the OS takes over the event loop, blocking PollEvents
        // (this watch fires from inside SDL_PumpEvents on the thread running the modal loop).
        // To keep the scene laid out and rendered at the new size mid-drag we must run the
        // FULL size-change notification from here — cached sizes AND Resized. This is safe:
        // AppHost's Resized subscriber only reads the published sizes and enqueues the actual
        // onResize work onto a ConcurrentQueue drained by the update thread, and the renderer
        // resize is marshalled via ScheduleToDrawThread. Deferring Resized until PollEvents
        // would mean the root container keeps its old size for the whole drag (stretched frame).
        if (type == SDL_EventType.SDL_EVENT_WINDOW_RESIZED ||
            type == SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED ||
            type == SDL_EventType.SDL_EVENT_WINDOW_EXPOSED)
        {
            if (instance != null)
            {
                if (type != SDL_EventType.SDL_EVENT_WINDOW_EXPOSED)
                    instance.handleSizeChanged();

                instance.RenderRequested.Invoke();
            }
        }

        return true;
    }

    /// <summary>
    /// Queries the current window sizes, publishes them, persists config and fires
    /// <see cref="Resized"/> when the size actually changed since the last notification.
    /// Called from both the event watch (mid-drag, possibly before PollEvents can run)
    /// and the polled event path — the lastNotified* fields dedupe between the two so
    /// every size change is notified exactly once.
    /// </summary>
    /// <returns>Whether the size had changed (and a notification was fired).</returns>
    private unsafe bool handleSizeChanged()
    {
        if (window == null) return false;

        int drawW, drawH, logW, logH;
        SDL_GetWindowSizeInPixels(window, &drawW, &drawH);
        SDL_GetWindowSize(window, &logW, &logH);

        if (drawW == lastNotifiedDrawW && drawH == lastNotifiedDrawH &&
            logW == lastNotifiedLogW && logH == lastNotifiedLogH)
            return false;

        // currentWidth/currentHeight are read from the update/draw threads via
        // GetPhysicalSize, so publish them with Interlocked.
        Interlocked.Exchange(ref currentWidth, drawW);
        Interlocked.Exchange(ref currentHeight, drawH);
        logicalWidth = (int)MathF.Round(logW / contentScale);
        logicalHeight = (int)MathF.Round(logH / contentScale);

        lastNotifiedDrawW = drawW;
        lastNotifiedDrawH = drawH;
        lastNotifiedLogW = logW;
        lastNotifiedLogH = logH;

        Logger.Verbose($"Window resized to {drawW}x{drawH} (logical size: {logicalWidth}x{logicalHeight})");

        if (remembersBounds)
        {
            windowConfig.Get<int>(FrameworkSetting.WindowWidth).Value = logW;
            windowConfig.Get<int>(FrameworkSetting.WindowHeight).Value = logH;
        }

        Resized.Invoke(logicalWidth, logicalHeight);
        return true;
    }

    /// <summary>
    /// Applies exclusive fullscreen: pins the window to the display's current mode. Returns the mode
    /// that was actually applied — <see cref="WindowMode.Fullscreen"/> on success, or
    /// <see cref="WindowMode.Borderless"/> if the display mode could not be resolved (in which case
    /// borderless-desktop fullscreen is applied as a safe fallback).
    /// </summary>
    /// <remarks>
    /// Exclusive fullscreen on macOS historically misbehaved under SDL (wrong drawable size, black
    /// screens, stale refresh rate). We harden the transition here so it is reliable on every
    /// platform: validate the display mode before handing it to SDL, fall back to borderless if it is
    /// unavailable, then <c>SDL_SyncWindow</c> to force the mode change to complete before we read
    /// back any sizes, and finally re-query the drawable size and refresh rate (both can change with
    /// the mode, especially across HiDPI/Retina backing-scale boundaries).
    /// </remarks>
    private unsafe WindowMode applyExclusiveFullscreen()
    {
        var mode = SDL_GetCurrentDisplayMode(SDL_GetDisplayForWindow(window));

        if (mode == null)
        {
            Logger.Warning("Could not resolve the display mode for exclusive fullscreen; falling back to Borderless.");
            SDL_SetWindowFullscreenMode(window, null);
            SDL_SetWindowFullscreen(window, true);
            SDL_SyncWindow(window);
            return WindowMode.Borderless;
        }

        SDL_SetWindowFullscreenMode(window, mode);
        SDL_SetWindowFullscreen(window, true);

        // Block until the fullscreen transition is fully applied. Without this the window may still
        // report its pre-fullscreen (or zero) size on the next query, which on macOS manifests as a
        // black screen or half-resolution surface.
        SDL_SyncWindow(window);

        // The active display mode and Retina backing scale can differ in fullscreen, so refresh the
        // cached refresh rate; size is refreshed by the caller via handleSizeChanged().
        DisplayHz = getDisplayRefreshRate();

        return WindowMode.Fullscreen;
    }

    private unsafe void setWindowMode(WindowMode newMode)
    {
        newMode = supported(newMode);

        if (windowMode == newMode)
            return;

        windowMode = newMode;

        // The effective mode may differ from the requested one if exclusive fullscreen falls back to
        // borderless (see applyExclusiveFullscreen). We persist the effective mode below so config and
        // the reactive reflect what is actually on screen.
        WindowMode effectiveMode = newMode;

        if (window != null)
        {
            switch (newMode)
            {
                case WindowMode.Windowed:
                    SDL_SetWindowFullscreen(window, false);

                    if (fillsDisplay)
                        break;

                    SDL_SetWindowBordered(window, true);

                    if (windowConfig != null)
                    {
                        int savedX = windowConfig.Get<int>(FrameworkSetting.WindowX).Value;
                        int savedY = windowConfig.Get<int>(FrameworkSetting.WindowY).Value;
                        int savedW = windowConfig.Get<int>(FrameworkSetting.WindowWidth).Value;
                        int savedH = windowConfig.Get<int>(FrameworkSetting.WindowHeight).Value;

                        if (savedW > 0 && savedH > 0)
                            SDL_SetWindowSize(window, savedW, savedH);

                        if (savedX != -1 && savedY != -1 && isPositionOnConnectedDisplay(savedX, savedY))
                            SDL_SetWindowPosition(window, savedX, savedY);
                        else
                            SDL_SetWindowPosition(window, windowpos_centered, windowpos_centered);
                    }
                    else
                    {
                        SDL_SetWindowPosition(window, windowpos_centered, windowpos_centered);
                    }
                    break;

                case WindowMode.Borderless:
                    // null fullscreen mode = borderless fullscreen desktop in SDL3.
                    SDL_SetWindowFullscreenMode(window, null);
                    SDL_SetWindowFullscreen(window, true);
                    break;

                case WindowMode.Fullscreen:
                    effectiveMode = applyExclusiveFullscreen();
                    break;
            }
        }

        windowMode = effectiveMode;
        WindowModeReactive.Value = effectiveMode;

        if (windowConfig != null)
        {
            windowConfig.Get<WindowMode>(FrameworkSetting.WindowMode).Value = effectiveMode;
        }
        else
        {
            Logger.Warning("windowConfig is null — WindowMode change will not be persisted.");
        }

        if (window == null) return;

        // Mode changes usually generate RESIZED events as well; handleSizeChanged dedupes
        // so whichever path runs first wins and the other is a no-op.
        handleSizeChanged();

        Logger.Debug($"Window mode changed to {effectiveMode}");
    }

    private unsafe void handleSdlEvents()
    {
        SDL_Event sdlEvent;
        while (SDL_PollEvent(&sdlEvent))
        {
            var type = (SDL_EventType)sdlEvent.type;

            switch (type)
            {
                case SDL_EventType.SDL_EVENT_QUIT:
                    ExitRequested.Invoke();
                    break;

                case SDL_EventType.SDL_EVENT_KEY_DOWN:
                    // macOS: open the system Character Viewer (emoji picker) on Ctrl+Cmd+Space.
                    // Handled here (and swallowed) because there is no dedicated macOS window type yet.
                    if (tryHandleMacEmojiPalette(sdlEvent.key))
                        break;
                    handleKeyEvent(sdlEvent.key, OnKeyDown);
                    break;

                case SDL_EventType.SDL_EVENT_KEY_UP:
                    handleKeyEvent(sdlEvent.key, OnKeyUp);
                    break;

                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                    handleMouseButtonEvent(sdlEvent.button, OnMouseDown);
                    break;

                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                    handleMouseButtonEvent(sdlEvent.button, OnMouseUp);
                    break;

                case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                    handleMouseMotionEvent(sdlEvent.motion);
                    break;

                case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                    handleMouseWheelEvent(sdlEvent.wheel);
                    break;

                case SDL_EventType.SDL_EVENT_FINGER_DOWN:
                    handleFingerDown(sdlEvent.tfinger);
                    break;

                case SDL_EventType.SDL_EVENT_FINGER_MOTION:
                    handleFingerMotion(sdlEvent.tfinger);
                    break;

                case SDL_EventType.SDL_EVENT_FINGER_UP:
                    handleFingerUp(sdlEvent.tfinger, false);
                    break;

                case SDL_EventType.SDL_EVENT_FINGER_CANCELED:
                    handleFingerUp(sdlEvent.tfinger, true);
                    break;

                case SDL_EventType.SDL_EVENT_DROP_FILE:
                    handleDropFileEvent(sdlEvent.drop);
                    break;

                case SDL_EventType.SDL_EVENT_DROP_TEXT:
                    handleDropTextEvent(sdlEvent.drop);
                    break;

                case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                    handleTextInputEvent(sdlEvent.text);
                    break;

                case SDL_EventType.SDL_EVENT_TEXT_EDITING:
                    handleTextEditingEvent(sdlEvent.edit);
                    break;

                // A display's mode changed: a refresh rate picked in the system's display settings, or a phone's
                // panel switching between 60 and 120 Hz. It may be another display's, but re-reading the window's
                // own costs little, and only a real change is reported.
                case SDL_EventType.SDL_EVENT_DISPLAY_CURRENT_MODE_CHANGED:
                    updateDisplayHz();
                    break;

                case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
                    handleGamepadAdded(sdlEvent.gdevice);
                    break;

                case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
                    handleGamepadRemoved(sdlEvent.gdevice);
                    break;

                case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN:
                    handleGamepadButtonEvent(sdlEvent.gbutton, pressed: true);
                    break;

                case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP:
                    handleGamepadButtonEvent(sdlEvent.gbutton, pressed: false);
                    break;

                case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION:
                    handleGamepadAxisEvent(sdlEvent.gaxis);
                    break;

                default:
                    // SDL3 promoted window events to first-class event types (no more SDL_WINDOWEVENT).
                    if (type >= SDL_EventType.SDL_EVENT_WINDOW_FIRST && type <= SDL_EventType.SDL_EVENT_WINDOW_LAST)
                        handleWindowEvent(type, sdlEvent.window);
                    break;
            }
        }
    }

    private unsafe void handleWindowEvent(SDL_EventType type, SDL_WindowEvent sdlWindowEvent)
    {
        switch (type)
        {
            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
                IsActive = true;
                Logger.Debug("Window focus gained");

                // Some platforms drop relative capture while unfocused — re-assert it.
                if (relativeMouseMode)
                    SDL_SetWindowRelativeMouseMode(window, true);

                FocusGained.Invoke();
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                IsActive = false;
                Logger.Debug("Window focus lost");
                FocusLost.Invoke();
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_MINIMIZED:
                IsActive = false;
                Logger.Debug("Window minimized");
                Minimized.Invoke();
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_RESTORED:
                IsActive = true;
                Logger.Debug("Window restored from minimized state");
                Restored.Invoke();
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_DISPLAY_CHANGED:
                updateDisplayHz();
                updateDisplayName();

                // When the window moves to a display with a different pixel density (e.g. from
                // a non-HiDPI external monitor to the HiDPI MacBook screen), SDL may or may not
                // fire SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED. Refresh the physical size here so
                // the renderer always uses the correct DPI scale for the new display.
                if (handleSizeChanged())
                    RenderRequested.Invoke();
                break;

            // DISPLAY_SCALE_CHANGED is the one SDL documents for this. It is the only one that
            // necessarily fires when the density changes without the window moving or resizing — a
            // scale change made in the OS display settings while the app is running. The other two are
            // what actually fires in the common case of dragging between displays, and handleSizeChanged
            // dedupes, so listening to all three costs nothing and covers cases none of them covers
            // alone.
            case SDL_EventType.SDL_EVENT_WINDOW_DISPLAY_SCALE_CHANGED:
            case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
            case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
                // A new density changes the size in framework units even when SDL's size stays the same,
                // so the dedupe below must not swallow it.
                if (updateContentScale())
                    lastNotifiedLogW = -1;

                // The event watch usually handled this already mid-drag; handleSizeChanged
                // dedupes via lastNotified*, so this only fires for size changes the watch
                // didn't see and is a no-op for queued duplicates of watch-handled events.
                handleSizeChanged();
                // The safe area is reported relative to the window, so a resize (or moving
                // between displays, e.g., onto a notched laptop screen) can change it.
                updateSafeArea();
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_SAFE_AREA_CHANGED:
                updateSafeArea();
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_MOVED:
                if (remembersBounds)
                {
                    int posX, posY;
                    SDL_GetWindowPosition(window, &posX, &posY);
                    windowConfig.Get<int>(FrameworkSetting.WindowX).Value = posX;
                    windowConfig.Get<int>(FrameworkSetting.WindowY).Value = posY;
                }
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_ENTER:
                CursorInWindow = true;
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE:
                CursorInWindow = false;
                break;
        }
    }

    /// <summary>
    /// On macOS, opens the system Character Viewer (emoji &amp; symbols picker) when the user presses
    /// the standard Ctrl+Cmd+Space shortcut while a text field is focused. A plain (unbundled) SDL app
    /// has no standard Edit menu, so the OS has nothing to bind that shortcut to, we invoke
    /// <c>[NSApp orderFrontCharacterPalette:]</c> ourselves instead. Picked characters arrive as normal
    /// <c>SDL_EVENT_TEXT_INPUT</c> events, so no other wiring is needed.
    /// Returns true if the event was consumed (so normal key dispatch is skipped).
    /// </summary>
    private bool tryHandleMacEmojiPalette(SDL_KeyboardEvent keyboardEvent)
    {
        if (!RuntimeInfo.IsMacOS)
            return false;

        // Only when a text field is focused, so the picker has somewhere to insert.
        if (!textInputActive)
            return false;

        if (keyboardEvent.scancode != SDL_Scancode.SDL_SCANCODE_SPACE)
            return false;

        bool ctrl = (keyboardEvent.mod & SDL_Keymod.SDL_KMOD_CTRL) != 0;
        bool cmd = (keyboardEvent.mod & SDL_Keymod.SDL_KMOD_GUI) != 0;

        if (!ctrl || !cmd)
            return false;

        // Ignore auto-repeat: open once per physical press, but still swallow repeats so the combo
        // never leaks into normal key handling.
        if (!keyboardEvent.repeat)
            MacOSNative.ShowCharacterPalette();

        return true;
    }

    private void handleKeyEvent(SDL_KeyboardEvent keyboardEvent, Action<KeyEvent> action)
    {
        var key = SDLEnumMapping.ToSakuraKey(keyboardEvent.scancode);
        if (key == Key.Unknown)
            return;

        // SDL3 carries the modifier state on the event itself — no SDL_GetModState round-trip.
        var modifiers = KeyModifiers.None;
        var modState = keyboardEvent.mod;

        if ((modState & SDL_Keymod.SDL_KMOD_CTRL) != 0) modifiers |= KeyModifiers.Control;
        if ((modState & SDL_Keymod.SDL_KMOD_SHIFT) != 0) modifiers |= KeyModifiers.Shift;
        if ((modState & SDL_Keymod.SDL_KMOD_ALT) != 0) modifiers |= KeyModifiers.Alt;

        bool isRepeat = keyboardEvent.repeat;

        // The event carries a nanosecond hardware/OS timestamp — far closer to the physical
        // press than our processing time. Mapped onto the shared TimeSource timeline so
        // gameplay can judge against it (see GameplayClock.GetTimeAt).
        action.Invoke(new KeyEvent(key, modifiers, isRepeat, convertEventTimestamp(keyboardEvent.timestamp)));
    }

    private void handleMouseButtonEvent(SDL_MouseButtonEvent buttonEvent, Action<SakuraMouseButtonEvent> action)
    {
        var button = SDLEnumMapping.ToSakuraMouseButton(buttonEvent.button);
        if (button == MouseButton.Unknown) return;

        // SDL3 reports mouse coordinates as floats (subpixel precision).
        // In relative mode the OS position is meaningless (cursor is captured) —
        // the virtual cursor maintained by motion events is authoritative.
        if (!relativeMouseMode)
            mouseState.Position = new Vector2(buttonEvent.x / contentScale, buttonEvent.y / contentScale);
        mouseState.SetPressed(button, buttonEvent.down);
        action.Invoke(new SakuraMouseButtonEvent(mouseState.Clone(), button, buttonEvent.clicks, convertEventTimestamp(buttonEvent.timestamp)));
    }

    /// <summary>
    /// Converts an SDL event timestamp (nanoseconds on the SDL_GetTicksNS timeline)
    /// onto the shared <see cref="TimeSource"/> timeline in milliseconds.
    /// </summary>
    private double convertEventTimestamp(ulong timestampNs) => timestampNs / 1_000_000.0 + sdlTimestampOffset;

    // How often SDL's event clock is anchored onto the framework's again.
    private const double timestamp_anchor_interval_ms = 1000;

    private double lastTimestampAnchor;

    /// <summary>
    /// Anchors SDL's event timestamps (SDL_GetTicksNS, ns since SDL init) onto the framework's shared
    /// <see cref="TimeSource"/> timeline, so input times compare directly with every framework clock. Again each second:
    /// SDL's clock can be another than .NET's (CLOCK_MONOTONIC_RAW against CLOCK_MONOTONIC on Linux and Android, which
    /// the system slews), so anchored once, input times would drift from the music over a long session.
    /// </summary>
    private void anchorEventTimestamps()
    {
        double now = TimeSource.CurrentTime;

        sdlTimestampOffset = now - SDL_GetTicksNS() / 1_000_000.0;
        lastTimestampAnchor = now;
    }

    private void handleMouseMotionEvent(SDL_MouseMotionEvent motionEvent)
    {
        Vector2 delta;

        if (relativeMouseMode)
        {
            // Relative mode: xrel/yrel are unaccelerated hardware counts. Advance the
            // virtual cursor by the scaled delta, clamped to the window bounds.
            float sensitivity = (float)cursorSensitivity;
            delta = new Vector2(motionEvent.xrel * sensitivity, motionEvent.yrel * sensitivity);

            mouseState.Position = new Vector2(
                Math.Clamp(mouseState.Position.X + delta.X, 0, logicalWidth),
                Math.Clamp(mouseState.Position.Y + delta.Y, 0, logicalHeight));
        }
        else
        {
            mouseState.Position = new Vector2(motionEvent.x / contentScale, motionEvent.y / contentScale);
            delta = new Vector2(motionEvent.xrel / contentScale, motionEvent.yrel / contentScale);
        }

        OnMouseMove.Invoke(new MouseEvent(mouseState.Clone(), delta, convertEventTimestamp(motionEvent.timestamp)));
    }

    /// <summary>
    /// The SDL finger each <see cref="TouchSource"/> is, for the sources in <see cref="touchesDown"/>.
    /// </summary>
    private readonly ulong[] touchFingers = new ulong[InputState.MAX_TOUCHES];

    /// <summary>
    /// The <see cref="TouchSource"/>s with a finger down, as bits.
    /// </summary>
    private int touchesDown;

    private void handleFingerDown(SDL_TouchFingerEvent finger)
    {
        // Only touchscreens. A trackpad's fingers (macOS) are positions on the pad, not the window, and the pad
        // already moves the mouse.
        if (SDL_GetTouchDeviceType(finger.touchID) != SDL_TouchDeviceType.SDL_TOUCH_DEVICE_DIRECT)
            return;

        // A finger beyond the last source is ignored until one lifts.
        for (int i = 0; i < touchFingers.Length; i++)
        {
            if ((touchesDown & (1 << i)) != 0)
                continue;

            touchesDown |= 1 << i;
            touchFingers[i] = (ulong)finger.fingerID;

            OnTouchDown.Invoke(new TouchEvent((TouchSource)i, fingerPosition(finger), timestamp: convertEventTimestamp(finger.timestamp)));
            return;
        }
    }

    private void handleFingerMotion(SDL_TouchFingerEvent finger)
    {
        if (findTouch(finger) is TouchSource source)
            OnTouchMove.Invoke(new TouchEvent(source, fingerPosition(finger), new Vector2(finger.dx * logicalWidth, finger.dy * logicalHeight), convertEventTimestamp(finger.timestamp)));
    }

    private void handleFingerUp(SDL_TouchFingerEvent finger, bool cancelled)
    {
        if (findTouch(finger) is not TouchSource source)
            return;

        touchesDown &= ~(1 << (int)source);
        OnTouchUp.Invoke(new TouchEvent(source, fingerPosition(finger), timestamp: convertEventTimestamp(finger.timestamp), cancelled: cancelled));
    }

    private TouchSource? findTouch(SDL_TouchFingerEvent finger)
    {
        ulong id = (ulong)finger.fingerID;

        for (int i = 0; i < touchFingers.Length; i++)
        {
            if ((touchesDown & (1 << i)) != 0 && touchFingers[i] == id)
                return (TouchSource)i;
        }

        return null;
    }

    // SDL gives a finger's position as a fraction of the window.
    private Vector2 fingerPosition(SDL_TouchFingerEvent finger) => new Vector2(finger.x * logicalWidth, finger.y * logicalHeight);

    private void handleMouseWheelEvent(SDL_MouseWheelEvent wheelEvent)
    {
        // SDL3 wheel events carry the mouse position directly (meaningless in relative mode).
        if (!relativeMouseMode)
            mouseState.Position = new Vector2(wheelEvent.mouse_x / contentScale, wheelEvent.mouse_y / contentScale);
        OnScroll.Invoke(new ScrollEvent(mouseState, new Vector2(wheelEvent.x, wheelEvent.y)));
    }

    private unsafe void handleDropFileEvent(SDL_DropEvent dropEvent)
    {
        // SDL3: drop event strings are owned by SDL (temporary memory) — do not free.
        string filePath = Marshal.PtrToStringUTF8((nint)dropEvent.data);
        if (string.IsNullOrEmpty(filePath))
            return;
        OnDragDropFile.Invoke(new DragDropFileEvent(filePath, new Vector2(dropEvent.x / contentScale, dropEvent.y / contentScale)));
    }

    private unsafe void handleDropTextEvent(SDL_DropEvent dropEvent)
    {
        string text = Marshal.PtrToStringUTF8((nint)dropEvent.data);
        if (string.IsNullOrEmpty(text))
            return;
        OnDragDropText.Invoke(new DragDropTextEvent(text, new Vector2(dropEvent.x / contentScale, dropEvent.y / contentScale)));
    }

    private unsafe void startTextInput(TextInputType type)
    {
        bool typeChanged = type != activeTextInputType;
        activeTextInputType = type;

        // A virtual keyboard that's up stays on the field it opened for, with that field's layout.
        bool retargeted = SDL_TextInputActive(window) && SDL_ScreenKeyboardShown(window)
                          && (typeChanged || !sameArea(appliedTextInputArea, screenKeyboardArea))
                          && RetargetScreenKeyboard(type, appliedTextInputArea, typeChanged);

        // Without that, it only takes another field's layout by opening again.
        if (typeChanged && !retargeted && SDL_TextInputActive(window))
            SDL_StopTextInput(window);

        screenKeyboardArea = appliedTextInputArea;

        var props = SDL_CreateProperties();

        fixed (byte* name = SDL_PROP_TEXTINPUT_TYPE_NUMBER)
            SDL_SetNumberProperty(props, name, (long)toSdlTextInputType(type));

        // Shows the virtual keyboard again if it's hidden. Dismissing it (Back on Android) stops SDL's text input
        // while the field keeps its focus, so a tap on the field calls this again to bring it back.
        SDL_StartTextInputWithProperties(window, props);
        SDL_DestroyProperties(props);
    }

    private static bool sameArea(SDL_Rect a, SDL_Rect b) => a.x == b.x && a.y == b.y && a.w == b.w && a.h == b.h;

    private static SDL_TextInputType toSdlTextInputType(TextInputType type) => type switch
    {
        TextInputType.Name => SDL_TextInputType.SDL_TEXTINPUT_TYPE_TEXT_NAME,
        TextInputType.Email => SDL_TextInputType.SDL_TEXTINPUT_TYPE_TEXT_EMAIL,
        TextInputType.Username => SDL_TextInputType.SDL_TEXTINPUT_TYPE_TEXT_USERNAME,
        TextInputType.Password => SDL_TextInputType.SDL_TEXTINPUT_TYPE_TEXT_PASSWORD_HIDDEN,
        TextInputType.Number => SDL_TextInputType.SDL_TEXTINPUT_TYPE_NUMBER,
        TextInputType.NumberPassword => SDL_TextInputType.SDL_TEXTINPUT_TYPE_NUMBER_PASSWORD_HIDDEN,
        _ => SDL_TextInputType.SDL_TEXTINPUT_TYPE_TEXT
    };

    private unsafe void handleTextInputEvent(SDL_TextInputEvent textEvent)
    {
        string text = Marshal.PtrToStringUTF8((nint)textEvent.text);
        if (!string.IsNullOrEmpty(text))
        {
            OnTextInput.Invoke(new TextInputEvent(text));
        }
    }

    private unsafe void handleTextEditingEvent(SDL_TextEditingEvent editEvent)
    {
        string text = Marshal.PtrToStringUTF8((nint)editEvent.text);
        OnTextEditing.Invoke(new TextEditingEvent(text ?? string.Empty, editEvent.start, editEvent.length));
    }

    // The thread SDL was set up on, which its clipboard and cursor calls belong to.
    private int eventThreadId;

    // Clipboard work asked for from other threads, done in PollEvents.
    private readonly ConcurrentQueue<ClipboardRead> clipboardReads = new ConcurrentQueue<ClipboardRead>();
    private volatile string? pendingClipboardText;

    /// <summary>
    /// How long a paste from another thread waits for the event thread to read the clipboard.
    /// </summary>
    private static readonly TimeSpan clipboard_read_timeout = TimeSpan.FromSeconds(1);

    /// <remarks>
    /// Read on the event thread, as SDL needs, and only when asked for: Android tells the player each time an app reads
    /// the clipboard, so it isn't read ahead. From another thread this waits for the event thread, which is woken for it.
    /// </remarks>
    public string GetClipboardText()
    {
        if (!initialized || Environment.CurrentManagedThreadId == eventThreadId)
            return readClipboard();

        var read = new ClipboardRead();
        clipboardReads.Enqueue(read);
        wakeEventThread();

        return read.Done.Wait(clipboard_read_timeout) ? read.Text : string.Empty;
    }

    /// <remarks>
    /// Set on the event thread, as SDL needs: from another thread, it's set there next.
    /// </remarks>
    public void SetClipboardText(string text)
    {
        if (!initialized || Environment.CurrentManagedThreadId == eventThreadId)
        {
            SDL_SetClipboardText(text);
            return;
        }

        pendingClipboardText = text;
        wakeEventThread();
    }

    private static string readClipboard() => SDL_HasClipboardText() ? SDL_GetClipboardText() ?? string.Empty : string.Empty;

    private void applyClipboardWork()
    {
        if (pendingClipboardText is string text)
        {
            pendingClipboardText = null;
            SDL_SetClipboardText(text);
        }

        while (clipboardReads.TryDequeue(out var read))
        {
            read.Text = readClipboard();
            read.Done.Set();
        }
    }

    // An event nothing handles, to bring the event thread out of a wait for events.
    private static unsafe void wakeEventThread()
    {
        var e = new SDL_Event();
        e.type = (uint)SDL_EventType.SDL_EVENT_USER;
        pushEvent(&e);
    }

    // SDL_PushEvent ends SDL's own wait for events, but not the run loop's that the event thread waits in on iOS.
    private static unsafe void pushEvent(SDL_Event* e)
    {
        SDL_PushEvent(e);

        if (OperatingSystem.IsIOS())
            MainRunLoop.WakeUp();
    }

    private sealed class ClipboardRead
    {
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim();
        public string Text = string.Empty;
    }

    public unsafe void ShowOpenFileDialog(FileDialogOptions options, Action<FileDialogResult> callback)
        => SDLFileDialog.ShowOpenFile(window, options, callback);

    public unsafe void ShowSaveFileDialog(FileDialogOptions options, Action<FileDialogResult> callback)
        => SDLFileDialog.ShowSaveFile(window, options, callback);

    public unsafe void ShowOpenFolderDialog(FileDialogOptions options, Action<FileDialogResult> callback)
        => SDLFileDialog.ShowOpenFolder(window, options, callback);

    /// <summary>
    /// Returns true if the given screen position falls within the bounds of any connected display.
    /// Used to detect whether a saved window position is still valid after monitor changes.
    /// </summary>
    private static unsafe bool isPositionOnConnectedDisplay(int x, int y)
    {
        int numDisplays;
        var displays = SDL_GetDisplays(&numDisplays);

        if (displays == null)
            return false;

        try
        {
            for (int i = 0; i < numDisplays; i++)
            {
                SDL_Rect bounds;
                if (!SDL_GetDisplayBounds(displays[i], &bounds))
                    continue;

                // Check if the position is within this display's bounds.
                // Use a small margin (at least 64px of the title bar must be visible).
                const int margin = 64;
                if (x >= bounds.x - margin &&
                    x < bounds.x + bounds.w &&
                    y >= bounds.y - margin &&
                    y < bounds.y + bounds.h)
                    return true;
            }
        }
        finally
        {
            SDL_free((IntPtr)displays);
        }

        return false;
    }

    private void updateDisplayHz()
    {
        int oldHz = DisplayHz;
        DisplayHz = getDisplayRefreshRate();

        if (oldHz == DisplayHz)
            return;

        Logger.Debug($"Display refresh rate changed from {oldHz} Hz to {DisplayHz} Hz");
        DisplayChanged.Invoke(DisplayHz);
    }

    private unsafe void updateDisplayName()
    {
        string name = SDL_GetDisplayName(SDL_GetDisplayForWindow(window)) ?? string.Empty;

        if (name == DisplayName.Value)
            return;

        Logger.Verbose($"On display: {name}");
        DisplayName.Value = name;
    }

    private unsafe int getDisplayRefreshRate()
    {
        if (GetPlatformRefreshRate() is float platformRate && platformRate > 0)
            return (int)Math.Round(platformRate);

        var mode = SDL_GetCurrentDisplayMode(SDL_GetDisplayForWindow(window));

        if (mode != null && mode->refresh_rate > 0)
            return (int)Math.Round(mode->refresh_rate);

        return 60;
    }

    /// <summary>
    /// Process all pending window events.
    /// </summary>
    public unsafe void WaitForEvents(double timeoutMilliseconds)
    {
        if (OperatingSystem.IsIOS())
        {
            // SDL would poll here a thousand times a second. What another thread queued doesn't wake the run loop
            // unless it went through pushEvent, so it's looked for first.
            if (!SDL_HasEvents((uint)SDL_EventType.SDL_EVENT_FIRST, (uint)SDL_EventType.SDL_EVENT_LAST))
                MainRunLoop.Wait(Math.Max(0, timeoutMilliseconds) / 1000);

            return;
        }

        // Rounded up, as SDL waits whole milliseconds: rounded down, a wait of under one would return at once.
        int timeout = (int)Math.Ceiling(Math.Max(0, timeoutMilliseconds));
        SDL_WaitEventTimeout(null, timeout);
    }

    public unsafe void PollEvents()
    {
        // Apply relative mouse mode switches on the main thread (SDL window calls are not thread-safe).
        if (pendingRelativeModeChange)
        {
            pendingRelativeModeChange = false;
            applyRelativeMouseMode();
        }

        // Same reason: the cursor shape is chosen while dispatching hover, on the update thread, and its visibility there too.
        if (pendingCursorChange)
        {
            pendingCursorChange = false;
            updateSdlCursor();

            if (cursorVisible)
                SDL_ShowCursor();
            else
                SDL_HideCursor();
        }

        applyClipboardWork();

        if (TimeSource.CurrentTime - lastTimestampAnchor >= timestamp_anchor_interval_ms)
            anchorEventTimestamps();

        bool? targetState;
        TextInputType targetType;
        RectangleF? targetRect;

        // drain background thread commands safely
        lock (textInputLock)
        {
            targetState = pendingTextInputState;
            targetType = pendingTextInputType;
            pendingTextInputState = null;

            targetRect = pendingTextInputRect;
            pendingTextInputRect = null;
        }

        // apply IME composition box positions on the main thread, before the state: a virtual keyboard
        // places itself by the area SDL has as it opens
        if (targetRect.HasValue)
        {
            var rect = targetRect.Value;
            var sdlRect = new SDL_Rect
            {
                x = (int)(rect.X * contentScale),
                y = (int)(rect.Y * contentScale),
                w = (int)(rect.Width * contentScale),
                h = (int)(rect.Height * contentScale)
            };

            // A focused field sends its caret every frame, but it rarely moves.
            if (!textInputAreaApplied || !sameArea(sdlRect, appliedTextInputArea))
            {
                SDL_SetTextInputArea(window, &sdlRect, 0);
                appliedTextInputArea = sdlRect;
                textInputAreaApplied = true;
            }
        }

        // apply text input state switches on the main thread (SDL3: per-window)
        if (targetState.HasValue)
        {
            if (targetState.Value)
                startTextInput(targetType);
            else
                SDL_StopTextInput(window);

            textInputActive = targetState.Value;
        }

        // normal OS message processing
        handleSdlEvents();
    }

    public unsafe void Show()
    {
        if (window != null)
            SDL_ShowWindow(window);
    }

    public unsafe void SwapBuffers()
    {
        // Only OpenGL presents at the window layer. Metal and Direct3D11 present from inside their
        // renderers (command-buffer commit / DXGI swapchain), so there's nothing to do here.
        if (graphicsApi == RendererType.OpenGL)
        {
            if (window != null)
                SDL_GL_SwapWindow(window);
        }
    }

    /// <summary>
    /// Makes the graphics context current on the calling thread.
    /// For OpenGL: binds the GL context. For Metal and Direct3D11: no-op.
    /// </summary>
    public void MakeCurrent()
    {
        if (graphicsApi == RendererType.OpenGL)
            glSurface.MakeCurrent();
    }

    /// <summary>
    /// Releases the graphics context from the calling thread.
    /// For OpenGL: unbinds the GL context. For Metal and Direct3D11: no-op.
    /// </summary>
    public void ClearCurrent()
    {
        if (graphicsApi == RendererType.OpenGL)
            glSurface.ClearCurrent();
    }

    /// <summary>
    /// Enable or disable VSync.
    /// </summary>
    public void SetVSync(bool enabled)
    {
        // OpenGL only. Metal VSync is controlled via CAMetalLayer.displaySyncEnabled; Direct3D11
        // via the swapchain Present interval — both handled inside their renderers.
        if (graphicsApi == RendererType.OpenGL)
            SDL_GL_SetSwapInterval(enabled ? 1 : 0);
    }

    private unsafe void setTitle(string newTitle)
    {
        title = newTitle;

        if (window != null)
            SDL_SetWindowTitle(window, newTitle);
    }

    private void setApplicationName(string newAppName)
    {
        applicationName = newAppName;

        // SDL3 replaces the SDL_APP_NAME hint with first-class app metadata.
        if (!SDL_SetAppMetadata(newAppName, null, null))
            Logger.Warning("Failed to set SDL application name to " + newAppName);
    }

    private unsafe void setResizable(bool isResizable)
    {
        resizable = isResizable;

        if (window != null)
            SDL_SetWindowResizable(window, isResizable);
    }

    #region Gamepad

    private unsafe void handleGamepadAdded(SDL_GamepadDeviceEvent deviceEvent)
    {
        SDL_JoystickID joystickId = deviceEvent.which;
        SDL_Gamepad* gamepad = SDL_OpenGamepad(joystickId);

        if (gamepad == null)
        {
            Logger.Warning($"Failed to open gamepad (joystick id {joystickId}): {SDL_GetError()}");
            return;
        }

        // SDL_GetGamepadID returns the instance ID we use for all subsequent events.
        int instanceId = (int)(uint)SDL_GetGamepadID(gamepad);
        string name = SDL_GetGamepadName(gamepad) ?? string.Empty;

        openGamepads[instanceId] = (IntPtr)gamepad;
        gamepadStates[instanceId] = new GamepadState { DeviceId = instanceId };

        Logger.Debug($"Gamepad connected: id={instanceId}, name=\"{name}\"");
        OnGamepadConnected.Invoke(new GamepadConnectedEvent(instanceId, name));
    }

    private unsafe void handleGamepadRemoved(SDL_GamepadDeviceEvent deviceEvent)
    {
        int instanceId = (int)(uint)deviceEvent.which;

        if (openGamepads.TryGetValue(instanceId, out IntPtr ptr))
        {
            SDL_CloseGamepad((SDL_Gamepad*)ptr);
            openGamepads.Remove(instanceId);
        }

        gamepadStates.Remove(instanceId);

        Logger.Debug($"Gamepad disconnected: id={instanceId}");
        OnGamepadDisconnected.Invoke(new GamepadDisconnectedEvent(instanceId));
    }

    private void handleGamepadButtonEvent(SDL_GamepadButtonEvent buttonEvent, bool pressed)
    {
        int instanceId = (int)(uint)buttonEvent.which;
        var button = SDLEnumMapping.ToSakuraGamepadButton(buttonEvent.button);

        if (button == GamepadButton.Unknown)
            return;

        if (!gamepadStates.TryGetValue(instanceId, out var state))
        {
            state = new GamepadState { DeviceId = instanceId };
            gamepadStates[instanceId] = state;
        }

        state.SetPressed(button, pressed);

        var evt = new GamepadButtonEvent(state.Clone(), button, pressed, convertEventTimestamp(buttonEvent.timestamp));

        if (pressed)
            OnGamepadButtonDown.Invoke(evt);
        else
            OnGamepadButtonUp.Invoke(evt);
    }

    private void handleGamepadAxisEvent(SDL_GamepadAxisEvent axisEvent)
    {
        int instanceId = (int)(uint)axisEvent.which;
        var axis = SDLEnumMapping.ToSakuraGamepadAxis(axisEvent.axis);

        if (axis == GamepadAxis.Unknown)
            return;

        if (!gamepadStates.TryGetValue(instanceId, out var state))
        {
            state = new GamepadState { DeviceId = instanceId };
            gamepadStates[instanceId] = state;
        }

        // SDL3 axis values are Sint16 in [-32768, 32767]. Normalise to [-1, 1].
        // Triggers report [0, 32767] — normalised result is [0, 1] naturally.
        float normalized = axisEvent.value / 32767f;
        state.SetAxis(axis, normalized);

        OnGamepadAxisMotion.Invoke(new GamepadAxisEvent(state.Clone(), axis, normalized, convertEventTimestamp(axisEvent.timestamp)));
    }

    #endregion

    #region Cursor

    private bool cursorVisible = true;
    private readonly Dictionary<SakuraCursorState, IntPtr> sdlCursors = new Dictionary<SakuraCursorState, IntPtr>();

    public bool CursorVisible
    {
        get => cursorVisible;
        set
        {
            if (cursorVisible == value)
                return;

            cursorVisible = value;

            // Set where the cursor's shape is, on the event thread; this is set from the update thread.
            pendingCursorChange = true;
        }
    }

    public Reactive<SakuraCursorState> CursorState { get; } = new Reactive<SakuraCursorState>(SakuraCursorState.Default);

    /// <summary>
    /// Set when <see cref="CursorState"/> changes, and consumed on the main thread in
    /// <see cref="PollEvents"/>.
    /// </summary>
    /// <remarks>
    /// The cursor is driven by hovering, and hover is dispatched on the <em>update</em> thread —
    /// <c>AppHost</c> drains its input queue there. SDL window calls must run on the main thread, so
    /// applying the shape inline would put <c>SDL_CreateSystemCursor</c> and <c>SDL_SetCursor</c> on
    /// the wrong one on every hover. Deferred exactly as <see cref="RelativeMouseMode"/> is, and for
    /// the same reason. This was latent until something actually started setting the cursor.
    /// </remarks>
    private volatile bool pendingCursorChange;

    private unsafe void updateSdlCursor()
    {
        if (window == null)
            return;

        var cursorState = CursorState.Value;

        if (!sdlCursors.TryGetValue(cursorState, out IntPtr cursorPtr))
        {
            SDL_SystemCursor sysCursor;
            switch (cursorState)
            {
                case SakuraCursorState.Pointer:
                    sysCursor = SDL_SystemCursor.SDL_SYSTEM_CURSOR_POINTER;
                    break;

                case SakuraCursorState.Text:
                    sysCursor = SDL_SystemCursor.SDL_SYSTEM_CURSOR_TEXT;
                    break;

                case SakuraCursorState.Wait:
                    sysCursor = SDL_SystemCursor.SDL_SYSTEM_CURSOR_WAIT;
                    break;

                case SakuraCursorState.Crosshair:
                    sysCursor = SDL_SystemCursor.SDL_SYSTEM_CURSOR_CROSSHAIR;
                    break;

                case SakuraCursorState.NotAllowed:
                    sysCursor = SDL_SystemCursor.SDL_SYSTEM_CURSOR_NOT_ALLOWED;
                    break;

                case SakuraCursorState.Grab:
                    sysCursor = SDL_SystemCursor.SDL_SYSTEM_CURSOR_GRAB;
                    break;

                case SakuraCursorState.Grabbing:
                    sysCursor = SDL_SystemCursor.SDL_SYSTEM_CURSOR_GRABBING;
                    break;

                default:
                    sysCursor = SDL_SystemCursor.SDL_SYSTEM_CURSOR_DEFAULT;
                    break;
            }

            SDL_Cursor* newCursor = SDL_CreateSystemCursor(sysCursor);
            cursorPtr = (IntPtr)newCursor;
            sdlCursors[cursorState] = cursorPtr;
        }

        SDL_SetCursor((SDL_Cursor*)cursorPtr);
    }

    #endregion

    public unsafe void Destroy()
    {
        // Added again by the next Create.
        SDL_RemoveEventWatch(&resizeEventWatch, IntPtr.Zero);

        if (glContext != null)
        {
            SDL_GL_DestroyContext(glContext);
            glContext = null;
        }

        if (metalView != IntPtr.Zero)
        {
            SDL_Metal_DestroyView(metalView);
            metalView = IntPtr.Zero;
            metalSurface.MetalLayer = IntPtr.Zero;
        }

        if (window != null)
        {
            SDL_DestroyWindow(window);
            window = null;
        }

        Logger.Verbose("SDL window destroyed");
    }

    public unsafe void Dispose()
    {
        SDL_RemoveEventWatch(&resizeEventWatch, IntPtr.Zero);
        SDL_RemoveEventWatch(&lifecycleEventWatch, IntPtr.Zero);
        RemoveTouchTimeWatch();

        foreach (IntPtr ptr in openGamepads.Values)
            SDL_CloseGamepad((SDL_Gamepad*)ptr);
        openGamepads.Clear();
        gamepadStates.Clear();

        foreach (IntPtr cursorPtr in sdlCursors.Values)
        {
            SDL_DestroyCursor((SDL_Cursor*)cursorPtr);
        }
        sdlCursors.Clear();

        if (glContext != null && graphicsApi != RendererType.Metal)
        {
            SDL_GL_DestroyContext(glContext);
            glContext = null;
        }

        if (window != null)
        {
            SDL_DestroyWindow(window);
            window = null;
        }
    }
}
