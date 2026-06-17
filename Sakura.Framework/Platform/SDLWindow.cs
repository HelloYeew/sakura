// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Sakura.Framework.Configurations;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
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
    private IGraphicsSurface activeSurface;

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

    // Tracks the last size for which Resized was fired (main-thread only).
    // Separate from currentWidth/currentHeight which the watch callback updates
    // mid-drag so the renderer can draw at the correct size before PollEvents runs.
    private int lastNotifiedDrawW;
    private int lastNotifiedDrawH;
    private int lastNotifiedLogW;
    private int lastNotifiedLogH;
    private bool? pendingTextInputState;
    private RectangleF? pendingTextInputRect;
    private readonly Lock textInputLock = new Lock();

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

        SDL_Rect safeRect;
        if (!SDL_GetWindowSafeArea(window, &safeRect))
            return;

        int windowW, windowH;
        SDL_GetWindowSize(window, &windowW, &windowH);

        SafeAreaPadding.Value = new MarginPadding
        {
            Left = Math.Max(0, safeRect.x),
            Top = Math.Max(0, safeRect.y),
            Right = Math.Max(0, windowW - (safeRect.x + safeRect.w)),
            Bottom = Math.Max(0, windowH - (safeRect.y + safeRect.h)),
        };
    }

    private unsafe void applyMinimumSize()
    {
        if (window == null) return;
        SDL_SetWindowMinimumSize(window, (int)minimumSize.X, (int)minimumSize.Y);
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
            SDL_WarpMouseInWindow(window, mouseState.Position.X, mouseState.Position.Y);
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

        var savedMode = config.Get<WindowMode>(FrameworkSetting.WindowMode).Value;
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

        switch (type)
        {
            case RendererType.Metal:
                windowFlags |= SDL_WindowFlags.SDL_WINDOW_METAL;
                activeSurface = metalSurface;
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

    // TODO: This update action also no longer needed since it's handled in host's main loop
    public event Action Update = delegate { };
    public event Action FocusLost = delegate { };
    public event Action FocusGained = delegate { };
    public event Action Minimized = delegate { };
    public event Action Restored = delegate { };
    public event Action ExitRequested = delegate { };
    public event Action Exited = delegate { };

    public event Action<KeyEvent> OnKeyDown = delegate { };
    public event Action<KeyEvent> OnKeyUp = delegate { };
    public event Action<int> DisplayChanged = delegate { };
    public event Action<int, int> Resized = delegate { };

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

    public void StartTextInput()
    {
        lock (textInputLock)
        {
            pendingTextInputState = true;
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

        // Note: SDL3 is always per-monitor DPI aware; the SDL2-era Windows DPI hints are gone.
        // Make sure SDL video backend is fully initialized
        // To make it support for OpenGL context and process advance hint like profile version
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_GAMEPAD))
        {
            throw new Exception($"Failed to initialize SDL: {SDL_GetError()}");
        }

        int version = SDL_GetVersion();

        // Anchor SDL's event timestamp timeline (SDL_GetTicksNS, ns since SDL init) onto the
        // framework's shared TimeSource timeline so input event timestamps are directly
        // comparable with every framework clock.
        sdlTimestampOffset = TimeSource.CurrentTime - SDL_GetTicksNS() / 1_000_000.0;

        Logger.Verbose("🪟 SDL initialized");
        Logger.Verbose($"SDL Version: {version / 1000000}.{version / 1000 % 1000}.{version % 1000}");
        Logger.Verbose($"SDL Revision: {SDL_GetRevision()}");
        Logger.Verbose($"SDL Video Driver: {SDL_GetCurrentVideoDriver()}");

        CursorState.ValueChanged += _ => updateSdlCursor();

        initialized = true;
    }

    public unsafe void Create()
    {
        windowFlags |= SDL_WindowFlags.SDL_WINDOW_HIDDEN;

        if (Resizable)
        {
            windowFlags |= SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        }

        if (RuntimeInfo.IsMacOS && windowMode == WindowMode.Fullscreen)
        {
            // SDL's full screen on MacOS has a lot of issues so we force MacOS to use only window and borderless window modes
            Logger.Warning("Fullscreen mode is not supported on MacOS due to a lot of issues with SDL implementation, falling back to Borderless window mode.");
            windowMode = WindowMode.Borderless;
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

        if (windowConfig != null && windowMode == WindowMode.Windowed)
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

        applyMinimumSize();

        if (relativeMouseMode)
            SDL_SetWindowRelativeMouseMode(window, true);

        updateSafeArea();

        SDL_SetWindowPosition(window, spawnX, spawnY);

        if (windowMode == WindowMode.Fullscreen)
        {
            // Exclusive fullscreen: pin the window to the display's current mode.
            var mode = SDL_GetCurrentDisplayMode(SDL_GetDisplayForWindow(window));
            SDL_SetWindowFullscreenMode(window, mode);
            SDL_SetWindowFullscreen(window, true);
        }

        SDL_AddEventWatch(&resizeEventWatch, IntPtr.Zero);

        DisplayHz = getDisplayRefreshRate();
        Logger.Verbose($"Display refresh rate: {DisplayHz} Hz");

        int w, h;
        SDL_GetWindowSize(window, &w, &h);
        logicalWidth = w;
        logicalHeight = h;

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

        Logger.Verbose("Metal surface initialised successfully");
    }

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

    public void GetPhysicalSize(out int width, out int height)
    {
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
        logicalWidth = logW;
        logicalHeight = logH;

        lastNotifiedDrawW = drawW;
        lastNotifiedDrawH = drawH;
        lastNotifiedLogW = logW;
        lastNotifiedLogH = logH;

        Logger.Verbose($"Window resized to {drawW}x{drawH} (logical size: {logW}x{logH})");

        if (windowConfig != null && windowMode == WindowMode.Windowed)
        {
            windowConfig.Get<int>(FrameworkSetting.WindowWidth).Value = logW;
            windowConfig.Get<int>(FrameworkSetting.WindowHeight).Value = logH;
        }

        Resized.Invoke(logicalWidth, logicalHeight);
        return true;
    }

    private unsafe void setWindowMode(WindowMode newMode)
    {
        if (windowMode == newMode)
            return;

        if (RuntimeInfo.IsMacOS && newMode == WindowMode.Fullscreen)
        {
            newMode = WindowMode.Borderless;
        }

        windowMode = newMode;
        WindowModeReactive.Value = newMode;

        if (windowConfig != null)
        {
            windowConfig.Get<WindowMode>(FrameworkSetting.WindowMode).Value = newMode;
        }
        else
        {
            Logger.Warning("windowConfig is null — WindowMode change will not be persisted.");
        }

        if (window == null) return;

        switch (newMode)
        {
            case WindowMode.Windowed:
                SDL_SetWindowFullscreen(window, false);
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
                var mode = SDL_GetCurrentDisplayMode(SDL_GetDisplayForWindow(window));
                SDL_SetWindowFullscreenMode(window, mode);
                SDL_SetWindowFullscreen(window, true);
                break;
        }

        // Mode changes usually generate RESIZED events as well; handleSizeChanged dedupes
        // so whichever path runs first wins and the other is a no-op.
        handleSizeChanged();

        Logger.Debug($"Window mode changed to {newMode}");
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
                    IsExiting = true;
                    break;

                case SDL_EventType.SDL_EVENT_KEY_DOWN:
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
                int oldHz = DisplayHz;
                DisplayHz = getDisplayRefreshRate();

                if (oldHz != DisplayHz)
                {
                    Logger.Debug($"Display refresh rate changed from {oldHz} Hz to {DisplayHz} Hz");
                    DisplayChanged.Invoke(DisplayHz);
                }

                // When the window moves to a display with a different pixel density (e.g. from
                // a non-HiDPI external monitor to the HiDPI MacBook screen), SDL may or may not
                // fire SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED. Refresh the physical size here so
                // the renderer always uses the correct DPI scale for the new display.
                if (handleSizeChanged())
                    RenderRequested.Invoke();
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
            case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
                // The event watch usually handled this already mid-drag; handleSizeChanged
                // dedupes via lastNotified*, so this only fires for size changes the watch
                // didn't see and is a no-op for queued duplicates of watch-handled events.
                handleSizeChanged();
                // The safe area is reported relative to the window, so a resize (or moving
                // between displays, e.g. onto a notched laptop screen) can change it.
                updateSafeArea();
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_SAFE_AREA_CHANGED:
                updateSafeArea();
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_MOVED:
                if (windowConfig != null && windowMode == WindowMode.Windowed)
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
            mouseState.Position = new Vector2(buttonEvent.x, buttonEvent.y);
        mouseState.SetPressed(button, buttonEvent.down);
        action.Invoke(new SakuraMouseButtonEvent(mouseState.Clone(), button, buttonEvent.clicks, convertEventTimestamp(buttonEvent.timestamp)));
    }

    /// <summary>
    /// Converts an SDL event timestamp (nanoseconds on the SDL_GetTicksNS timeline)
    /// onto the shared <see cref="TimeSource"/> timeline in milliseconds.
    /// </summary>
    private double convertEventTimestamp(ulong timestampNs) => timestampNs / 1_000_000.0 + sdlTimestampOffset;

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
            mouseState.Position = new Vector2(motionEvent.x, motionEvent.y);
            delta = new Vector2(motionEvent.xrel, motionEvent.yrel);
        }

        OnMouseMove.Invoke(new MouseEvent(mouseState.Clone(), delta, convertEventTimestamp(motionEvent.timestamp)));
    }

    private void handleMouseWheelEvent(SDL_MouseWheelEvent wheelEvent)
    {
        // SDL3 wheel events carry the mouse position directly (meaningless in relative mode).
        if (!relativeMouseMode)
            mouseState.Position = new Vector2(wheelEvent.mouse_x, wheelEvent.mouse_y);
        OnScroll.Invoke(new ScrollEvent(mouseState, new Vector2(wheelEvent.x, wheelEvent.y)));
    }

    private unsafe void handleDropFileEvent(SDL_DropEvent dropEvent)
    {
        // SDL3: drop event strings are owned by SDL (temporary memory) — do not free.
        string filePath = Marshal.PtrToStringUTF8((nint)dropEvent.data);
        if (string.IsNullOrEmpty(filePath))
            return;
        OnDragDropFile.Invoke(new DragDropFileEvent(filePath, new Vector2(dropEvent.x, dropEvent.y)));
    }

    private unsafe void handleDropTextEvent(SDL_DropEvent dropEvent)
    {
        string text = Marshal.PtrToStringUTF8((nint)dropEvent.data);
        if (string.IsNullOrEmpty(text))
            return;
        OnDragDropText.Invoke(new DragDropTextEvent(text, new Vector2(dropEvent.x, dropEvent.y)));
    }

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

    public string GetClipboardText()
    {
        if (!SDL_HasClipboardText()) return string.Empty;

        return SDL_GetClipboardText() ?? string.Empty;
    }

    public void SetClipboardText(string text)
    {
        SDL_SetClipboardText(text);
    }

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

    private unsafe int getDisplayRefreshRate()
    {
        var mode = SDL_GetCurrentDisplayMode(SDL_GetDisplayForWindow(window));

        if (mode != null && mode->refresh_rate > 0)
            return (int)Math.Round(mode->refresh_rate);

        return 60;
    }

    /// <summary>
    /// Process all pending window events.
    /// </summary>
    public unsafe void PollEvents()
    {
        // Apply relative mouse mode switches on the main thread (SDL window calls are not thread-safe).
        if (pendingRelativeModeChange)
        {
            pendingRelativeModeChange = false;
            applyRelativeMouseMode();
        }

        bool? targetState;
        RectangleF? targetRect;

        // drain background thread commands safely
        lock (textInputLock)
        {
            targetState = pendingTextInputState;
            pendingTextInputState = null;

            targetRect = pendingTextInputRect;
            pendingTextInputRect = null;
        }

        // apply text input state switches on the main thread (SDL3: per-window)
        if (targetState.HasValue)
        {
            if (targetState.Value)
                SDL_StartTextInput(window);
            else
                SDL_StopTextInput(window);
        }

        // apply IME composition box positions on the main thread
        if (targetRect.HasValue)
        {
            var rect = targetRect.Value;
            var sdlRect = new SDL_Rect
            {
                x = (int)rect.X,
                y = (int)rect.Y,
                w = (int)rect.Width,
                h = (int)rect.Height
            };
            SDL_SetTextInputArea(window, &sdlRect, 0);
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
        if (graphicsApi == RendererType.Metal)
        {
            // Metal: presentation is handled by the renderer's command buffer commit.
            // Nothing to do at the window layer.
        }
        else
        {
            if (window != null)
                SDL_GL_SwapWindow(window);
        }
    }

    /// <summary>
    /// Makes the graphics context current on the calling thread.
    /// For OpenGL: binds the GL context.
    /// For Metal: no-op.
    /// </summary>
    public void MakeCurrent()
    {
        if (graphicsApi != RendererType.Metal)
            glSurface.MakeCurrent();
    }

    /// <summary>
    /// Releases the graphics context from the calling thread.
    /// For OpenGL: unbinds the GL context.
    /// For Metal: no-op.
    /// </summary>
    public void ClearCurrent()
    {
        if (graphicsApi != RendererType.Metal)
            glSurface.ClearCurrent();
    }

    /// <summary>
    /// Enable or disable VSync.
    /// </summary>
    public void SetVSync(bool enabled)
    {
        if (graphicsApi != RendererType.Metal)
            SDL_GL_SetSwapInterval(enabled ? 1 : 0);
        // Metal VSync is controlled via CAMetalLayer.displaySyncEnabled, will handled in MetalRenderer.
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
            cursorVisible = value;
            if (initialized)
            {
                if (value)
                    SDL_ShowCursor();
                else
                    SDL_HideCursor();
            }
        }
    }

    public Reactive<SakuraCursorState> CursorState { get; } = new Reactive<SakuraCursorState>(SakuraCursorState.Default);

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

    public unsafe void Dispose()
    {
        SDL_RemoveEventWatch(&resizeEventWatch, IntPtr.Zero);

        foreach (var ptr in openGamepads.Values)
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
